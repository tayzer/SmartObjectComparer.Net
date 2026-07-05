using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Workspaces;

public sealed class FileSystemRunStore : IRunStore
{
    private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string workspaceRoot;

    public FileSystemRunStore(string workspaceRoot)
    {
        this.workspaceRoot = FileSystemWorkspacePaths.NormalizeRoot(workspaceRoot);
    }

    public async Task SaveAsync(ComparisonRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        string runPath = GetRunPath(run.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(runPath) ?? workspaceRoot);

        await using FileStream stream = File.Create(runPath);
        await JsonSerializer
            .SerializeAsync(stream, ToDto(run), jsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ComparisonRun?> LoadAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        string runPath = GetRunPath(runId);
        if (!File.Exists(runPath))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(runPath);
        RunSnapshotDto? dto = await JsonSerializer
            .DeserializeAsync<RunSnapshotDto>(stream, jsonOptions, cancellationToken)
            .ConfigureAwait(false);

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

            await using FileStream stream = File.OpenRead(runPath);
            RunSnapshotDto? dto = await JsonSerializer
                .DeserializeAsync<RunSnapshotDto>(stream, jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (dto is not null)
            {
                runs.Add(RunListItem.FromRun(FromDto(dto)));
            }
        }

        return runs
            .OrderBy(run => run.CreatedAt)
            .ThenBy(run => run.Id.Value, StringComparer.Ordinal)
            .ToList();
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
        };

    private RunOptionsDto ToDto(RunOptions options) =>
        new RunOptionsDto
        {
            RequestBatch = options.RequestBatch.Value,
            EndpointA = ToDto(options.EndpointA),
            EndpointB = ToDto(options.EndpointB),
            TimeoutMilliseconds = options.Timeout.TotalMilliseconds,
            MaxConcurrency = options.MaxConcurrency,
            ModelName = options.ModelName,
            Comparison = ToDto(options.Comparison),
            RequestExecution = ToDto(options.RequestExecution),
            AlternateContract = options.AlternateContract is null ? null : ToDto(options.AlternateContract),
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
            IgnoreRules = options.IgnoreRules.Select(ToDto).ToList(),
            SmartIgnoreRules = options.SmartIgnoreRules.Select(ToDto).ToList(),
            MaskRules = options.MaskRules.Select(ToDto).ToList(),
        };

    private RequestExecutionOptionsDto ToDto(RequestExecutionOptions options) =>
        new RequestExecutionOptionsDto
        {
            ContentTypeOverride = options.ContentTypeOverride,
        };

    private AlternateContractOptionsDto ToDto(AlternateContractOptions options) =>
        new AlternateContractOptionsDto
        {
            ProfileId = options.ProfileId,
        };

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
        };

    private RunDetailReferenceDto ToDto(RunDetailReference reference) =>
        new RunDetailReferenceDto
        {
            DetailId = reference.DetailId,
            Artifact = reference.Artifact is null ? null : ToDto(reference.Artifact),
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
            dto.ErrorMessage);

    private RunOptions FromDto(RunOptionsDto dto) =>
        new RunOptions(
            new RequestBatchReference(dto.RequestBatch),
            FromDto(dto.EndpointA),
            FromDto(dto.EndpointB),
            TimeSpan.FromMilliseconds(dto.TimeoutMilliseconds),
            dto.MaxConcurrency,
            dto.ModelName,
            dto.Comparison is null ? null : FromDto(dto.Comparison),
            dto.RequestExecution is null ? null : FromDto(dto.RequestExecution),
            dto.AlternateContract is null ? null : FromDto(dto.AlternateContract));

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
            dto.MaskRules.Select(FromDto));

    private RequestExecutionOptions FromDto(RequestExecutionOptionsDto dto) =>
        new RequestExecutionOptions(dto.ContentTypeOverride);

    private AlternateContractOptions FromDto(AlternateContractOptionsDto dto) =>
        new AlternateContractOptions(dto.ProfileId);

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
            dto.ResponseBytesWritten);

    private RunDetailReference FromDto(RunDetailReferenceDto dto) =>
        new RunDetailReference(
            dto.DetailId,
            dto.Artifact is null ? null : FromDto(dto.Artifact));

    private ArtifactReference FromDto(ArtifactReferenceDto dto) =>
        new ArtifactReference(dto.ArtifactId, dto.ContentType);

    private sealed class RunSnapshotDto
    {
        public string Id { get; init; } = string.Empty;

        public RunOptionsDto Options { get; init; } = new RunOptionsDto();

        public RunStatus Status { get; init; }

        public RunProgressDto Progress { get; init; } = new RunProgressDto();

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public DateTimeOffset? StartedAt { get; init; }

        public DateTimeOffset? CompletedAt { get; init; }

        public RunResultSummaryDto? Summary { get; init; }

        public string? ErrorMessage { get; init; }
    }

    private sealed class RunOptionsDto
    {
        public string RequestBatch { get; init; } = string.Empty;

        public EndpointDefinitionDto EndpointA { get; init; } = new EndpointDefinitionDto();

        public EndpointDefinitionDto EndpointB { get; init; } = new EndpointDefinitionDto();

        public double TimeoutMilliseconds { get; init; }

        public int MaxConcurrency { get; init; }

        public string ModelName { get; init; } = "Auto";

        public ComparisonOptionsDto? Comparison { get; init; }

        public RequestExecutionOptionsDto? RequestExecution { get; init; }

        public AlternateContractOptionsDto? AlternateContract { get; init; }
    }

    private sealed class EndpointDefinitionDto
    {
        public string Uri { get; init; } = "https://example.test";

        public string? Label { get; init; }

        public Dictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ComparisonOptionsDto
    {
        public bool IgnoreCollectionOrder { get; init; }

        public bool IgnoreStringCase { get; init; }

        public bool IgnoreTrailingWhitespaceAtEnd { get; init; }

        public bool TreatNullAndEmptyCollectionsAsEqual { get; init; }

        public bool IgnoreXmlNamespaces { get; init; } = true;

        public int MaxDifferences { get; init; } = 100;

        public List<IgnoreRuleDefinitionDto> IgnoreRules { get; init; } = new List<IgnoreRuleDefinitionDto>();

        public List<SmartIgnoreRuleDefinitionDto> SmartIgnoreRules { get; init; } = new List<SmartIgnoreRuleDefinitionDto>();

        public List<MaskRuleDefinitionDto> MaskRules { get; init; } = new List<MaskRuleDefinitionDto>();
    }

    private sealed class RequestExecutionOptionsDto
    {
        public string? ContentTypeOverride { get; init; }
    }

    private sealed class AlternateContractOptionsDto
    {
        public string ProfileId { get; init; } = string.Empty;
    }

    private sealed class IgnoreRuleDefinitionDto
    {
        public string PropertyPath { get; init; } = string.Empty;

        public bool IgnoreCompletely { get; init; } = true;

        public bool IgnoreCollectionOrder { get; init; }

        public bool TreatNullAndEmptyCollectionsAsEqual { get; init; }
    }

    private sealed class SmartIgnoreRuleDefinitionDto
    {
        public SmartIgnoreRuleKind Kind { get; init; }

        public string Value { get; init; } = string.Empty;

        public bool IsEnabled { get; init; } = true;

        public string? Description { get; init; }
    }

    private sealed class MaskRuleDefinitionDto
    {
        public string PropertyPath { get; init; } = string.Empty;

        public int PreserveLastCharacters { get; init; }

        public string MaskCharacter { get; init; } = "*";
    }

    private sealed class RunProgressDto
    {
        public int PercentComplete { get; init; }

        public string Message { get; init; } = string.Empty;

        public int? CompletedItems { get; init; }

        public int? TotalItems { get; init; }
    }

    private sealed class RunResultSummaryDto
    {
        public int TotalPairs { get; init; }

        public int EqualPairs { get; init; }

        public int DifferentPairs { get; init; }

        public int ErrorPairs { get; init; }

        public int StatusCodeMismatchPairs { get; init; }

        public int BothNonSuccessPairs { get; init; }

        public RunDetailReferenceDto? DetailIndexReference { get; init; }

        public RunExecutionMetricsDto? ExecutionMetrics { get; init; }
    }

    private sealed class RunExecutionMetricsDto
    {
        public double TotalDurationMilliseconds { get; init; }

        public double RequestExecutionDurationMilliseconds { get; init; }

        public double ComparisonDurationMilliseconds { get; init; }

        public double FinalizationDurationMilliseconds { get; init; }

        public int RequestCount { get; init; }

        public int MaxConcurrency { get; init; }

        public long ResponseBytesWritten { get; init; }
    }

    private sealed class RunDetailReferenceDto
    {
        public string DetailId { get; init; } = string.Empty;

        public ArtifactReferenceDto? Artifact { get; init; }
    }

    private sealed class ArtifactReferenceDto
    {
        public string ArtifactId { get; init; } = string.Empty;

        public string? ContentType { get; init; }
    }
}

