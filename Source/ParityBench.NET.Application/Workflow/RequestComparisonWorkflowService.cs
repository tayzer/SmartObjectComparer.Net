using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Options;

using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Runs.Retention;
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

    public RequestComparisonWorkflowService(
        IRequestBatchStore requestBatchStore,
        IComparisonRunUseCases runUseCases,
        IRequestBatchReferenceGenerator requestBatchReferenceGenerator,
        IStaticReportBundleWriter reportBundleWriter,
        IReportAssetLocator reportAssetLocator,
        IResponseModelRegistry responseModelRegistry,
        IOptions<RetentionConfiguration>? retentionOptions = null)
    {
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

        RequestBatchReference batchReference = requestBatchReferenceGenerator.CreateReference();
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

        RunOptions runOptions = new RunOptions(
            batchReference,
            new EndpointDefinition(request.EndpointA, request.EndpointALabel, MergeHeaders(request.CommonHeaders, request.EndpointAHeaders)),
            new EndpointDefinition(request.EndpointB, request.EndpointBLabel, MergeHeaders(request.CommonHeaders, request.EndpointBHeaders)),
            request.Timeout,
            request.MaxConcurrency,
            request.ModelName,
            request.ComparisonOptions,
            request.RequestExecutionOptions,
            request.ContractProfileSelection,
            runRetentionModeOverride: request.RunRetentionModeOverride,
            comparisonRulesSnapshotHash: ComputeComparisonRulesSnapshotHash(request.ComparisonOptions, request.ContractProfileSelection),
            pluginComparison: request.PluginComparison);

        return await runUseCases
            .CreateRunAsync(runOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ComparisonRun> StartRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        runUseCases.StartRunAsync(runId, cancellationToken);

    public Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        runUseCases.CancelRunAsync(runId, cancellationToken);

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

