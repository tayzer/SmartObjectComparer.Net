using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

public sealed class FileSystemRunStore : IRunStore
{
    private const int SnapshotBufferSize = 81920;
    private const int FileOperationRetryCount = 5;
    private static readonly TimeSpan FileOperationRetryDelay = TimeSpan.FromMilliseconds(25);

    private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        // Not indented: this snapshot is rewritten on every progress tick (up to
        // dozens/sec across parallel workers), so skip the pretty-print cost.
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string workspaceRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> snapshotGates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<RunSnapshotRecoveryWarning> recoveryWarnings = new ConcurrentQueue<RunSnapshotRecoveryWarning>();

    public FileSystemRunStore(string workspaceRoot)
    {
        this.workspaceRoot = FileSystemWorkspacePaths.NormalizeRoot(workspaceRoot);
    }

    public async Task SaveAsync(ComparisonRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        string runPath = GetRunPath(run.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(runPath) ?? workspaceRoot);

        await WithSnapshotGateAsync(
            runPath,
            async () =>
            {
                await ExecuteFileOperationWithRetryAsync(
                    async () => await WriteSnapshotAtomicallyAsync(runPath, run, cancellationToken).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ComparisonRun?> LoadAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        string runPath = GetRunPath(runId);
        RunSnapshotDto? dto = await ReadSnapshotOrQuarantineAsync(runPath, cancellationToken).ConfigureAwait(false);

        return dto is null ? null : FromDto(dto);
    }

    public async Task<IReadOnlyList<RunListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        string runsRoot = Path.Combine(workspaceRoot, "runs");
        if (!Directory.Exists(runsRoot))
        {
            return Array.Empty<RunListItem>();
        }

        List<RunListItem> runs = new List<RunListItem>();
        foreach (string runPath in Directory.EnumerateFiles(runsRoot, "run.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            RunSnapshotDto? dto = await ReadSnapshotOrQuarantineAsync(runPath, cancellationToken).ConfigureAwait(false);

            if (dto is not null)
            {
                runs.Add(RunListItem.FromRun(FromDto(dto)));
            }
        }

        return runs
            .OrderByDescending(run => run.UpdatedAt)
            .ThenBy(run => run.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    public Task<IReadOnlyList<RunSnapshotRecoveryWarning>> DrainRecoveryWarningsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<RunSnapshotRecoveryWarning> warnings = new List<RunSnapshotRecoveryWarning>();
        while (recoveryWarnings.TryDequeue(out RunSnapshotRecoveryWarning? warning))
        {
            warnings.Add(warning);
        }

        return Task.FromResult<IReadOnlyList<RunSnapshotRecoveryWarning>>(warnings);
    }

    public async Task<RunResultSummary?> LoadSummaryAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun? run = await LoadAsync(runId, cancellationToken).ConfigureAwait(false);
        return run?.Summary;
    }

    private string GetRunPath(RunId runId) =>
        FileSystemWorkspacePaths.GetSafePath(
            workspaceRoot,
            FileSystemWorkspacePaths.ToLogicalPath("runs", runId.Value, "run.json"));

    private async Task WriteSnapshotAtomicallyAsync(
        string runPath,
        ComparisonRun run,
        CancellationToken cancellationToken)
    {
        string temporaryPath = $"{runPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = OpenTemporarySnapshotForWrite(temporaryPath))
            {
                await JsonSerializer
                    .SerializeAsync(stream, ToDto(run), jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(runPath))
            {
                File.Replace(temporaryPath, runPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, runPath);
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private async Task<RunSnapshotDto?> ReadSnapshotOrQuarantineAsync(string runPath, CancellationToken cancellationToken) =>
        await WithSnapshotGateAsync(
            runPath,
            async () =>
            {
                if (!File.Exists(runPath))
                {
                    return null;
                }

                try
                {
                    return await ReadSnapshotWithRetryAsync(runPath, cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException firstException)
                {
                    // The retry window may have observed another writer. Confirm the
                    // malformed content while this store's same-run gate is held
                    // before preserving it as evidence.
                    try
                    {
                        return await ReadSnapshotAsync(runPath, cancellationToken).ConfigureAwait(false);
                    }
                    catch (JsonException)
                    {
                        QuarantineMalformedSnapshot(runPath, firstException);
                        return null;
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);

    private async Task<RunSnapshotDto?> ReadSnapshotWithRetryAsync(string runPath, CancellationToken cancellationToken) =>
        await ExecuteFileOperationWithRetryAsync(
            () => ReadSnapshotAsync(runPath, cancellationToken),
            cancellationToken).ConfigureAwait(false);

    private async Task<RunSnapshotDto?> ReadSnapshotAsync(string runPath, CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenSnapshotForRead(runPath);
        return await JsonSerializer
            .DeserializeAsync<RunSnapshotDto>(stream, jsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private void QuarantineMalformedSnapshot(string runPath, JsonException exception)
    {
        string quarantinedPath = $"{runPath}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.corrupt";
        try
        {
            File.Move(runPath, quarantinedPath);
            recoveryWarnings.Enqueue(new RunSnapshotRecoveryWarning(
                runPath,
                quarantinedPath,
                $"Malformed run snapshot was quarantined: {exception.Message}"));
        }
        catch (Exception quarantineException) when (quarantineException is IOException or UnauthorizedAccessException)
        {
            recoveryWarnings.Enqueue(new RunSnapshotRecoveryWarning(
                runPath,
                null,
                $"Malformed run snapshot could not be quarantined: {quarantineException.Message}"));
        }
    }

    private static FileStream OpenTemporarySnapshotForWrite(string path) =>
        new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            SnapshotBufferSize,
            useAsync: true);

    private static FileStream OpenSnapshotForRead(string path) =>
        new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            SnapshotBufferSize,
            useAsync: true);

    private async Task<T> WithSnapshotGateAsync<T>(
        string path,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = snapshotGates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task WithSnapshotGateAsync(
        string path,
        Func<Task> action,
        CancellationToken cancellationToken) =>
        await WithSnapshotGateAsync(
            path,
            async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A later startup can safely ignore a never-promoted unique temp file.
        }
    }

    private static async Task<T> ExecuteFileOperationWithRetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (IOException) when (attempt < FileOperationRetryCount)
            {
                await Task.Delay(FileOperationRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < FileOperationRetryCount)
            {
                await Task.Delay(FileOperationRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException) when (attempt < FileOperationRetryCount)
            {
                await Task.Delay(FileOperationRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Task ExecuteFileOperationWithRetryAsync(
        Func<Task> operation,
        CancellationToken cancellationToken) =>
        ExecuteFileOperationWithRetryAsync(
            async () =>
            {
                await operation().ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    private RunSnapshotDto ToDto(ComparisonRun run) =>
        new RunSnapshotDto
        {
            Id = run.Id.Value,
            Options = ToDto(run.Options),
            Status = run.Status,
            Progress = ToDto(run.Progress),
            CreatedAt = run.CreatedAt,
            UpdatedAt = run.UpdatedAt,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            Summary = run.Summary is null ? null : ToDto(run.Summary),
            ErrorMessage = run.ErrorMessage,
            Diagnostics = run.Diagnostics is null ? null : ToDto(run.Diagnostics),
            RunRetentionMode = run.RunRetentionMode,
            RunRetentionPolicyVersion = run.RunRetentionPolicyVersion,
            ComparisonRulesSnapshotHash = run.ComparisonRulesSnapshotHash,
        };

    private RunOptionsDto ToDto(RunOptions options) =>
        new RunOptionsDto
        {
            RequestBatch = options.RequestBatch.Value,
            EndpointA = ToDto(options.EndpointA),
            EndpointB = ToDto(options.EndpointB),
            TimeoutMilliseconds = options.Timeout.TotalMilliseconds,
            MaxConcurrency = options.MaxConcurrency,
            ResponseModelName = options.ResponseModelName,
            ModelName = options.ResponseModelName,
            Comparison = ToDto(options.Comparison),
            RequestExecution = ToDto(options.RequestExecution),
            ContractProfile = options.ContractProfile is null ? null : ToDto(options.ContractProfile),
            PluginComparison = options.PluginComparison is null ? null : ToDto(options.PluginComparison),
            Baseline = BaselineBindingDto.FromBinding(options.Baseline),
            LargeRun = ToDto(options.LargeRun),
            RunRetentionModeOverride = options.RunRetentionModeOverride,
            ComparisonRulesSnapshotHash = options.ComparisonRulesSnapshotHash,
        };

    private EndpointDefinitionDto ToDto(EndpointDefinition endpoint) =>
        new EndpointDefinitionDto
        {
            Uri = endpoint.Uri.ToString(),
            Label = endpoint.Label,
            Headers = endpoint.Headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
        };

    private ComparisonOptionsDto ToDto(ComparisonOptions options) =>
        new ComparisonOptionsDto
        {
            IgnoreCollectionOrder = options.IgnoreCollectionOrder,
            IgnoreStringCase = options.IgnoreStringCase,
            IgnoreTrailingWhitespaceAtEnd = options.IgnoreTrailingWhitespaceAtEnd,
            TreatNullAndEmptyCollectionsAsEqual = options.TreatNullAndEmptyCollectionsAsEqual,
            IgnoreXmlNamespaces = options.IgnoreXmlNamespaces,
            MaxDifferences = options.MaxDifferences,
            IncludeAllDifferences = options.IncludeAllDifferences,
            IgnoreRules = options.IgnoreRules.Select(ToDto).ToList(),
            SmartIgnoreRules = options.SmartIgnoreRules.Select(ToDto).ToList(),
            MaskRules = options.MaskRules.Select(ToDto).ToList(),
        };

    private RequestExecutionOptionsDto ToDto(RequestExecutionOptions options) =>
        new RequestExecutionOptionsDto
        {
            ContentTypeOverride = options.ContentTypeOverride,
        };

    private ContractProfileSelectionDto ToDto(ContractProfileSelection selection) =>
        new ContractProfileSelectionDto
        {
            ProfileId = selection.ProfileId,
            ProfileVersion = selection.ProfileVersion,
            Options = selection.Options.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        };

    private PluginComparisonSelectionDto ToDto(PluginComparisonSelection selection) =>
        new PluginComparisonSelectionDto
        {
            PluginId = selection.PluginId,
            ComparisonId = selection.ComparisonId,
            PluginVersion = selection.PluginVersion,
            EnvironmentName = selection.EnvironmentName,
            EnabledStepIds = selection.EnabledStepIds.ToList(),
            StepConfiguration = (selection.StepConfiguration ?? new Dictionary<string, IReadOnlyDictionary<string, string>>())
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal),
        };

    private static PluginComparisonSelection FromDto(PluginComparisonSelectionDto dto) =>
        new PluginComparisonSelection(
            dto.PluginId,
            dto.ComparisonId,
            dto.PluginVersion,
            dto.EnabledStepIds,
            dto.StepConfiguration.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyDictionary<string, string>)entry.Value,
                StringComparer.Ordinal),
            dto.EnvironmentName);

    private IgnoreRuleDefinitionDto ToDto(IgnoreRuleDefinition rule) =>
        new IgnoreRuleDefinitionDto
        {
            PropertyPath = rule.PropertyPath,
            IgnoreCompletely = rule.IgnoreCompletely,
            IgnoreCollectionOrder = rule.IgnoreCollectionOrder,
            TreatNullAndEmptyCollectionsAsEqual = rule.TreatNullAndEmptyCollectionsAsEqual,
        };

    private SmartIgnoreRuleDefinitionDto ToDto(SmartIgnoreRuleDefinition rule) =>
        new SmartIgnoreRuleDefinitionDto
        {
            Kind = rule.Kind,
            Value = rule.Value,
            IsEnabled = rule.IsEnabled,
            Description = rule.Description,
        };

    private MaskRuleDefinitionDto ToDto(MaskRuleDefinition rule) =>
        new MaskRuleDefinitionDto
        {
            PropertyPath = rule.PropertyPath,
            PreserveLastCharacters = rule.PreserveLastCharacters,
            MaskCharacter = rule.MaskCharacter,
        };

    private RunProgressDto ToDto(RunProgress progress) =>
        new RunProgressDto
        {
            PercentComplete = progress.PercentComplete,
            Message = progress.Message,
            CompletedItems = progress.CompletedItems,
            TotalItems = progress.TotalItems,
        };

    private RunResultSummaryDto ToDto(RunResultSummary summary) =>
        new RunResultSummaryDto
        {
            TotalPairs = summary.TotalPairs,
            EqualPairs = summary.EqualPairs,
            DifferentPairs = summary.DifferentPairs,
            ErrorPairs = summary.ErrorPairs,
            StatusCodeMismatchPairs = summary.StatusCodeMismatchPairs,
            BothNonSuccessPairs = summary.BothNonSuccessPairs,
            DetailIndexReference = summary.DetailIndexReference is null ? null : ToDto(summary.DetailIndexReference),
            ExecutionMetrics = summary.ExecutionMetrics is null ? null : ToDto(summary.ExecutionMetrics),
        };

    private RunExecutionMetricsDto ToDto(RunExecutionMetrics metrics) =>
        new RunExecutionMetricsDto
        {
            TotalDurationMilliseconds = metrics.TotalDuration.TotalMilliseconds,
            RequestExecutionDurationMilliseconds = metrics.RequestExecutionDuration.TotalMilliseconds,
            ComparisonDurationMilliseconds = metrics.ComparisonDuration.TotalMilliseconds,
            FinalizationDurationMilliseconds = metrics.FinalizationDuration.TotalMilliseconds,
            RequestCount = metrics.RequestCount,
            MaxConcurrency = metrics.MaxConcurrency,
            ResponseBytesWritten = metrics.ResponseBytesWritten,
            ComparisonConcurrency = metrics.ComparisonConcurrency,
            RetainedArtifactCount = metrics.RetainedArtifactCount,
            TrimmedByPolicyArtifactCount = metrics.TrimmedByPolicyArtifactCount,
            MissingUnexpectedlyArtifactCount = metrics.MissingUnexpectedlyArtifactCount,
            CompareNormalizeDurationMilliseconds = metrics.CompareSubPhases?.NormalizeDuration.TotalMilliseconds,
            ComparePersistCanonicalDurationMilliseconds = metrics.CompareSubPhases?.PersistCanonicalDuration.TotalMilliseconds,
            CompareDiffDurationMilliseconds = metrics.CompareSubPhases?.DiffDuration.TotalMilliseconds,
            CompareFocusedContentDurationMilliseconds = metrics.CompareSubPhases?.FocusedContentDuration.TotalMilliseconds,
        };

    private RunDetailReferenceDto ToDto(RunDetailReference reference) =>
        new RunDetailReferenceDto
        {
            DetailId = reference.DetailId,
            Artifact = reference.Artifact is null ? null : ToDto(reference.Artifact),
            SchemaVersion = reference.SchemaVersion,
            PageSize = reference.PageSize,
            TotalCount = reference.TotalCount,
            AnalysisArtifact = reference.AnalysisArtifact is null ? null : ToDto(reference.AnalysisArtifact),
            DifferenceIndexArtifact = reference.DifferenceIndexArtifact is null ? null : ToDto(reference.DifferenceIndexArtifact),
        };

    private ArtifactReferenceDto ToDto(ArtifactReference reference) =>
        new ArtifactReferenceDto
        {
            ArtifactId = reference.ArtifactId,
            ContentType = reference.ContentType,
        };

    private ComparisonRun FromDto(RunSnapshotDto dto) =>
        ComparisonRun.Rehydrate(
            new RunId(dto.Id),
            FromDto(dto.Options),
            dto.Status,
            FromDto(dto.Progress),
            dto.CreatedAt,
            dto.UpdatedAt,
            dto.StartedAt,
            dto.CompletedAt,
            dto.Summary is null ? null : FromDto(dto.Summary),
            dto.ErrorMessage,
            dto.Diagnostics is null ? null : FromDto(dto.Diagnostics),
            dto.RunRetentionMode,
            string.IsNullOrWhiteSpace(dto.RunRetentionPolicyVersion)
                ? "v1"
                : dto.RunRetentionPolicyVersion,
            dto.ComparisonRulesSnapshotHash);

    private RunOptions FromDto(RunOptionsDto dto)
    {
        string responseModelName = string.IsNullOrWhiteSpace(dto.ResponseModelName) ? dto.ModelName : dto.ResponseModelName;
        ContractProfileSelection? contractProfile = dto.ContractProfile is not null
            ? FromDto(dto.ContractProfile)
            : dto.AlternateContract is null ? null : FromLegacyDto(dto.AlternateContract);

        return new RunOptions(
            new RequestBatchReference(dto.RequestBatch),
            FromDto(dto.EndpointA),
            FromDto(dto.EndpointB),
            TimeSpan.FromMilliseconds(dto.TimeoutMilliseconds),
            dto.MaxConcurrency,
            responseModelName,
            dto.Comparison is null ? null : FromDto(dto.Comparison),
            dto.RequestExecution is null ? null : FromDto(dto.RequestExecution),
            contractProfile,
            dto.LargeRun is null ? null : FromDto(dto.LargeRun),
            dto.RunRetentionModeOverride,
            dto.ComparisonRulesSnapshotHash,
            dto.PluginComparison is null ? null : FromDto(dto.PluginComparison),
            dto.Baseline?.ToBinding());
    }

    private EndpointDefinition FromDto(EndpointDefinitionDto dto) =>
        new EndpointDefinition(
            new Uri(dto.Uri, UriKind.Absolute),
            dto.Label,
            dto.Headers);

    private ComparisonOptions FromDto(ComparisonOptionsDto dto) =>
        new ComparisonOptions(
            dto.IgnoreCollectionOrder,
            dto.IgnoreStringCase,
            dto.IgnoreTrailingWhitespaceAtEnd,
            dto.TreatNullAndEmptyCollectionsAsEqual,
            dto.IgnoreXmlNamespaces,
            dto.MaxDifferences,
            dto.IgnoreRules.Select(FromDto),
            dto.SmartIgnoreRules.Select(FromDto),
            dto.MaskRules.Select(FromDto),
            dto.IncludeAllDifferences);

    private RequestExecutionOptions FromDto(RequestExecutionOptionsDto dto) =>
        new RequestExecutionOptions(dto.ContentTypeOverride);

    private LargeRunOptionsDto ToDto(LargeRunOptions options) =>
        new LargeRunOptionsDto
        {
            LargeRunThreshold = options.LargeRunThreshold,
            ChunkSize = options.ChunkSize,
            DetailPageSize = options.DetailPageSize,
            ComparisonConcurrency = options.ComparisonConcurrency,
            ProgressUpdateItemInterval = options.ProgressUpdateItemInterval,
            ProgressUpdateMillisecondsInterval = options.ProgressUpdateMillisecondsInterval,
        };

    private LargeRunOptions FromDto(LargeRunOptionsDto dto) =>
        new LargeRunOptions(
            dto.LargeRunThreshold <= 0 ? 1000 : dto.LargeRunThreshold,
            dto.ChunkSize <= 0 ? 500 : dto.ChunkSize,
            dto.DetailPageSize <= 0 ? 250 : dto.DetailPageSize,
            dto.ComparisonConcurrency,
            dto.ProgressUpdateItemInterval <= 0 ? 100 : dto.ProgressUpdateItemInterval,
            dto.ProgressUpdateMillisecondsInterval <= 0 ? 500 : dto.ProgressUpdateMillisecondsInterval);
    private ContractProfileSelection FromDto(ContractProfileSelectionDto dto) =>
        new ContractProfileSelection(dto.ProfileId, dto.ProfileVersion, dto.Options);

    private ContractProfileSelection FromLegacyDto(AlternateContractOptionsDto dto) =>
        new ContractProfileSelection(dto.ProfileId);

    private IgnoreRuleDefinition FromDto(IgnoreRuleDefinitionDto dto) =>
        new IgnoreRuleDefinition(
            dto.PropertyPath,
            dto.IgnoreCompletely,
            dto.IgnoreCollectionOrder,
            dto.TreatNullAndEmptyCollectionsAsEqual);

    private SmartIgnoreRuleDefinition FromDto(SmartIgnoreRuleDefinitionDto dto) =>
        new SmartIgnoreRuleDefinition(
            dto.Kind,
            dto.Value,
            dto.IsEnabled,
            dto.Description);

    private MaskRuleDefinition FromDto(MaskRuleDefinitionDto dto) =>
        new MaskRuleDefinition(
            dto.PropertyPath,
            dto.PreserveLastCharacters,
            dto.MaskCharacter);

    private RunProgress FromDto(RunProgressDto dto) =>
        new RunProgress(
            dto.PercentComplete,
            dto.Message,
            dto.CompletedItems,
            dto.TotalItems);

    private RunResultSummary FromDto(RunResultSummaryDto dto) =>
        new RunResultSummary(
            dto.TotalPairs,
            dto.EqualPairs,
            dto.DifferentPairs,
            dto.ErrorPairs,
            dto.StatusCodeMismatchPairs,
            dto.BothNonSuccessPairs,
            dto.DetailIndexReference is null ? null : FromDto(dto.DetailIndexReference),
            dto.ExecutionMetrics is null ? null : FromDto(dto.ExecutionMetrics));

    private RunExecutionMetrics FromDto(RunExecutionMetricsDto dto) =>
        new RunExecutionMetrics(
            TimeSpan.FromMilliseconds(dto.TotalDurationMilliseconds),
            TimeSpan.FromMilliseconds(dto.RequestExecutionDurationMilliseconds),
            TimeSpan.FromMilliseconds(dto.ComparisonDurationMilliseconds),
            TimeSpan.FromMilliseconds(dto.FinalizationDurationMilliseconds),
            dto.RequestCount,
            dto.MaxConcurrency,
            dto.ResponseBytesWritten,
            dto.RetainedArtifactCount,
            dto.TrimmedByPolicyArtifactCount,
            dto.MissingUnexpectedlyArtifactCount,
            dto.CompareNormalizeDurationMilliseconds is null
                ? null
                : new CompareSubPhaseMetrics(
                    TimeSpan.FromMilliseconds(dto.CompareNormalizeDurationMilliseconds.Value),
                    TimeSpan.FromMilliseconds(dto.ComparePersistCanonicalDurationMilliseconds ?? 0),
                    TimeSpan.FromMilliseconds(dto.CompareDiffDurationMilliseconds ?? 0),
                    TimeSpan.FromMilliseconds(dto.CompareFocusedContentDurationMilliseconds ?? 0)),
            dto.ComparisonConcurrency);

    private RunDetailReference FromDto(RunDetailReferenceDto dto) =>
        new RunDetailReference(
            dto.DetailId,
            dto.Artifact is null ? null : FromDto(dto.Artifact),
            dto.SchemaVersion <= 0 ? 1 : dto.SchemaVersion,
            dto.PageSize <= 0 ? 250 : dto.PageSize,
            Math.Max(0, dto.TotalCount),
            dto.AnalysisArtifact is null ? null : FromDto(dto.AnalysisArtifact),
            dto.DifferenceIndexArtifact is null ? null : FromDto(dto.DifferenceIndexArtifact));

    private ArtifactReference FromDto(ArtifactReferenceDto dto) =>
        new ArtifactReference(dto.ArtifactId, dto.ContentType);

    private RunDiagnosticsSnapshotDto ToDto(RunDiagnosticsSnapshot diagnostics) =>
        new RunDiagnosticsSnapshotDto
        {
            SlowRequestPaths = diagnostics.SlowRequestPaths.Select(ToDto).ToList(),
            Exceptions = diagnostics.Exceptions.Select(ToDto).ToList(),
        };

    private SlowRequestPathDiagnosticDto ToDto(SlowRequestPathDiagnostic slowPath) =>
        new SlowRequestPathDiagnosticDto
        {
            RelativePath = slowPath.RelativePath,
            DurationMilliseconds = slowPath.Duration.TotalMilliseconds,
        };

    private ExceptionDiagnosticDto ToDto(ExceptionDiagnostic exception) =>
        new ExceptionDiagnosticDto
        {
            Stage = exception.Stage,
            ExceptionType = exception.ExceptionType,
            Message = exception.Message,
            StackTrace = exception.StackTrace,
            RelativePath = exception.RelativePath,
            Endpoint = exception.Endpoint,
        };

    private RunDiagnosticsSnapshot FromDto(RunDiagnosticsSnapshotDto dto) =>
        new RunDiagnosticsSnapshot(
            dto.SlowRequestPaths.Select(FromDto).ToList(),
            dto.Exceptions.Select(FromDto).ToList());

    private SlowRequestPathDiagnostic FromDto(SlowRequestPathDiagnosticDto dto) =>
        new SlowRequestPathDiagnostic(
            dto.RelativePath,
            TimeSpan.FromMilliseconds(dto.DurationMilliseconds));

    private ExceptionDiagnostic FromDto(ExceptionDiagnosticDto dto) =>
        new ExceptionDiagnostic(
            dto.Stage,
            dto.ExceptionType,
            dto.Message,
            dto.StackTrace,
            dto.RelativePath,
            dto.Endpoint);
}
