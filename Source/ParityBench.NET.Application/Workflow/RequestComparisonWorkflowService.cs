using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Options;

using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Workflow;

public sealed class RequestComparisonWorkflowService : IRequestComparisonWorkflowUseCases
{
    private const string AutoModelName = "Auto";
    private readonly IRequestBatchStore requestBatchStore;
    private readonly IComparisonRunUseCases runUseCases;
    private readonly IRequestBatchReferenceGenerator requestBatchReferenceGenerator;
    private readonly IStaticReportBundleWriter reportBundleWriter;
    private readonly IReportAssetLocator reportAssetLocator;
    private readonly IResponseModelRegistry responseModelRegistry;
    private readonly IBaselineStore? baselineStore;

    public RequestComparisonWorkflowService(
        IRequestBatchStore requestBatchStore,
        IComparisonRunUseCases runUseCases,
        IRequestBatchReferenceGenerator requestBatchReferenceGenerator,
        IStaticReportBundleWriter reportBundleWriter,
        IReportAssetLocator reportAssetLocator,
        IResponseModelRegistry responseModelRegistry,
        IOptions<RetentionConfiguration>? retentionOptions = null,
        IBaselineStore? baselineStore = null)
    {
        this.baselineStore = baselineStore;
        this.requestBatchStore = requestBatchStore ?? throw new ArgumentNullException(nameof(requestBatchStore));
        this.runUseCases = runUseCases ?? throw new ArgumentNullException(nameof(runUseCases));
        this.requestBatchReferenceGenerator = requestBatchReferenceGenerator ?? throw new ArgumentNullException(nameof(requestBatchReferenceGenerator));
        this.reportBundleWriter = reportBundleWriter ?? throw new ArgumentNullException(nameof(reportBundleWriter));
        this.reportAssetLocator = reportAssetLocator ?? throw new ArgumentNullException(nameof(reportAssetLocator));
        this.responseModelRegistry = responseModelRegistry ?? throw new ArgumentNullException(nameof(responseModelRegistry));
    }

    public async Task<ComparisonRun> CreateRunFromDirectoryAsync(
        RequestComparisonRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateModelName(request.ModelName);
        ValidateBaselineSelection(request);

        ResolvedBaseline? resolvedBaseline = await ResolveBaselineAsync(request, cancellationToken).ConfigureAwait(false);

        RequestBatchReference batchReference = requestBatchReferenceGenerator.CreateReference();
        await StageRequestsAsync(request, resolvedBaseline, batchReference, cancellationToken).ConfigureAwait(false);

        EndpointDefinition endpointA = resolvedBaseline is null
            ? new EndpointDefinition(request.EndpointA, request.EndpointALabel, MergeHeaders(request.CommonHeaders, request.EndpointAHeaders))
            : CreateBaselineEndpoint(resolvedBaseline.Manifest);

        RunOptions runOptions = new RunOptions(
            batchReference,
            endpointA,
            new EndpointDefinition(request.EndpointB, request.EndpointBLabel, MergeHeaders(request.CommonHeaders, request.EndpointBHeaders)),
            request.Timeout,
            request.MaxConcurrency,
            request.ModelName,
            request.ComparisonOptions,
            request.RequestExecutionOptions,
            request.ContractProfileSelection,
            largeRunOptions: request.LargeRunOptions,
            runRetentionModeOverride: request.RunRetentionModeOverride,
            comparisonRulesSnapshotHash: ComputeComparisonRulesSnapshotHash(request.ComparisonOptions, request.ContractProfileSelection),
            pluginComparison: request.PluginComparison,
            baseline: CreateBinding(request.Baseline, resolvedBaseline));

        return await runUseCases
            .CreateRunAsync(runOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record ResolvedBaseline(BaselinePackageManifest Manifest);

    private void ValidateBaselineSelection(RequestComparisonRunRequest request)
    {
        if (request.Baseline is null || request.Baseline.Mode == BaselineRunMode.LiveVsLive)
        {
            return;
        }

        if (baselineStore is null)
        {
            throw new InvalidOperationException("A baseline store is required to capture or replay baselines.");
        }

        // Replay hands the engine a stored comparison model, which only means
        // something when a plugin comparison defines the type it was captured as.
        if (request.PluginComparison is null)
        {
            throw new InvalidOperationException(
                "Baseline capture and replay require a plugin comparison. Select a run profile before starting the run.");
        }
    }

    private async Task<ResolvedBaseline?> ResolveBaselineAsync(
        RequestComparisonRunRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Baseline is not { Mode: BaselineRunMode.BaselineVsLive } selection || baselineStore is null)
        {
            return null;
        }

        BaselineId baselineId = selection.BaselineId
            ?? throw new InvalidOperationException("A baseline replay run must name the baseline to replay.");

        BaselinePackageManifest manifest = await baselineStore
            .LoadManifestAsync(baselineId, selection.Version, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                selection.Version is null
                    ? $"Baseline '{baselineId.Value}' was not found."
                    : $"Baseline '{baselineId.Value}' v{selection.Version} was not found.");

        if (manifest.Scenarios.Count == 0)
        {
            throw new InvalidOperationException($"Baseline '{manifest.Name}' {manifest.DisplayVersion} contains no scenarios.");
        }

        // Comparing against a baseline captured for a different comparison would
        // deserialize the stored model into the wrong type, so it is refused here
        // rather than failing per scenario deep inside the run.
        if (!string.Equals(manifest.ComparisonId, request.PluginComparison!.ComparisonId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.PluginId, request.PluginComparison.PluginId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Baseline '{manifest.Name}' {manifest.DisplayVersion} was captured with comparison "
                + $"'{manifest.PluginId}/{manifest.ComparisonId}', but this run selected "
                + $"'{request.PluginComparison.PluginId}/{request.PluginComparison.ComparisonId}'.");
        }

        return new ResolvedBaseline(manifest);
    }

    private async Task StageRequestsAsync(
        RequestComparisonRunRequest request,
        ResolvedBaseline? resolvedBaseline,
        RequestBatchReference batchReference,
        CancellationToken cancellationToken)
    {
        if (resolvedBaseline is not null)
        {
            // The package owns the scenarios, so replay stages the requests it stored
            // rather than whatever happens to sit in a directory today.
            string stagingDirectory = Path.Combine(
                Path.GetTempPath(),
                "paritybench-baseline-replay",
                batchReference.Value);

            try
            {
                Directory.CreateDirectory(stagingDirectory);
                await baselineStore!
                    .ExportRequestsToDirectoryAsync(
                        resolvedBaseline.Manifest.Id,
                        resolvedBaseline.Manifest.Version,
                        stagingDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);

                await requestBatchStore
                    .StageDirectoryAsync(stagingDirectory, batchReference, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                TryDeleteDirectory(stagingDirectory);
            }

            return;
        }

        if (request.SourceFiles.Count == 0)
        {
            await requestBatchStore
                .StageDirectoryAsync(request.SourceDirectory, batchReference, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await requestBatchStore
                .StageFilesAsync(request.SourceDirectory, request.SourceFiles, batchReference, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // The label is what the report calls the expected side, so it carries the
    // provenance a reader needs at a glance: which package, which version, when.
    private static EndpointDefinition CreateBaselineEndpoint(BaselinePackageManifest manifest) =>
        new EndpointDefinition(
            manifest.CaptureEndpoint,
            $"Baseline: {manifest.Name} {manifest.DisplayVersion} (captured {manifest.CapturedAt:yyyy-MM-dd})");

    private static BaselineBinding? CreateBinding(
        BaselineRunSelection? selection,
        ResolvedBaseline? resolvedBaseline)
    {
        if (selection is null || selection.Mode == BaselineRunMode.LiveVsLive)
        {
            return null;
        }

        if (selection.Mode == BaselineRunMode.CaptureBaseline)
        {
            return BaselineBinding.ForCapture(selection.CaptureName!, selection.BaselineSlot);
        }

        BaselinePackageManifest manifest = resolvedBaseline!.Manifest;
        return BaselineBinding.ForReplay(manifest.Id, manifest.Version, selection.BaselineSlot);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The requests are already staged; a leftover temp directory is not worth
            // failing a run over.
        }
    }

    public Task<ComparisonRun> StartRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        runUseCases.StartRunAsync(runId, cancellationToken);

    public Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        runUseCases.CancelRunAsync(runId, cancellationToken);

    public Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        string? cancellationMessage,
        CancellationToken cancellationToken = default) =>
        runUseCases.CancelRunAsync(runId, cancellationMessage, cancellationToken);

    public async Task<StaticReportBundleWriteResult> GenerateReportAsync(
        RunId runId,
        string outputDirectory,
        string? reportAssetsDirectory = null,
        CancellationToken cancellationToken = default)
    {
        string resolvedAssetsDirectory = reportAssetLocator.Resolve(reportAssetsDirectory);
        return await reportBundleWriter
            .WriteAsync(runId, outputDirectory, resolvedAssetsDirectory, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private void ValidateModelName(string modelName)
    {
        if (string.Equals(modelName, AutoModelName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        responseModelRegistry.Resolve(modelName);
    }

    private static IReadOnlyDictionary<string, string> MergeHeaders(
        IReadOnlyDictionary<string, string> commonHeaders,
        IReadOnlyDictionary<string, string> endpointHeaders)
    {
        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> header in commonHeaders)
        {
            headers[header.Key] = header.Value;
        }

        foreach (KeyValuePair<string, string> header in endpointHeaders)
        {
            headers[header.Key] = header.Value;
        }

        return headers;
    }

    private static string ComputeComparisonRulesSnapshotHash(
        ComparisonOptions comparisonOptions,
        ContractProfileSelection? contractProfileSelection)
    {
        object snapshot = new
        {
            comparisonOptions.IgnoreCollectionOrder,
            comparisonOptions.IgnoreStringCase,
            comparisonOptions.IgnoreTrailingWhitespaceAtEnd,
            comparisonOptions.TreatNullAndEmptyCollectionsAsEqual,
            comparisonOptions.IgnoreXmlNamespaces,
            comparisonOptions.MaxDifferences,
            IgnoreRules = comparisonOptions.IgnoreRules,
            SmartIgnoreRules = comparisonOptions.SmartIgnoreRules,
            MaskRules = comparisonOptions.MaskRules,
            ContractProfile = contractProfileSelection,
            RetentionPolicyVersion = RetentionConfiguration.PolicyVersionV1,
        };

        byte[] snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        return Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant();
    }
}

