using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Cli;

public sealed class RequestCommandRunner
{
    private readonly IRequestComparisonWorkflowUseCases workflowUseCases;
    private readonly IRequestComparisonPresetRegistry presetRegistry;
    private readonly RunProfileResolver runProfileResolver;

    public RequestCommandRunner(
        IRequestComparisonWorkflowUseCases workflowUseCases,
        IRequestComparisonPresetRegistry presetRegistry,
        RunProfileResolver runProfileResolver)
    {
        this.workflowUseCases = workflowUseCases ?? throw new ArgumentNullException(nameof(workflowUseCases));
        this.presetRegistry = presetRegistry ?? throw new ArgumentNullException(nameof(presetRegistry));
        this.runProfileResolver = runProfileResolver ?? throw new ArgumentNullException(nameof(runProfileResolver));
    }

    public async Task<int> RunAsync(
        RequestCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!string.IsNullOrWhiteSpace(options.RunProfileId))
        {
            return await RunFromProfileAsync(options, output, error, cancellationToken).ConfigureAwait(false);
        }

        RequestComparisonPresetOption? preset = null;
        if (!string.IsNullOrWhiteSpace(options.PresetId))
        {
            preset = presetRegistry.ListPresets()
                .FirstOrDefault(candidate => string.Equals(candidate.PresetId, options.PresetId, StringComparison.OrdinalIgnoreCase));
            if (preset is null)
            {
                await error.WriteLineAsync($"Preset '{options.PresetId}' was not found.").ConfigureAwait(false);
                return 2;
            }
        }

        string? requestDirectory = options.RequestDirectory ?? preset?.RequestDirectory;
        Uri? endpointA = options.EndpointA ?? preset?.EndpointA;
        Uri? endpointB = options.EndpointB ?? preset?.EndpointB;
        string modelName = options.ModelName ?? preset?.ModelName ?? "Auto";
        string? contractProfileId = options.ContractProfileId ?? preset?.ContractProfileId;
        ComparisonOptions comparisonOptions = preset?.ComparisonOptions ?? new ComparisonOptions();
        string? contentTypeOverride = options.ContentTypeOverride ?? preset?.RequestExecutionOptions.ContentTypeOverride;

        if (requestDirectory is null || endpointA is null || endpointB is null)
        {
            await error.WriteLineAsync("Request directory, --endpoint-a, and --endpoint-b are required (directly or via --preset).").ConfigureAwait(false);
            return 2;
        }

        if (!Directory.Exists(requestDirectory))
        {
            await error.WriteLineAsync($"Request directory was not found: {Path.GetFullPath(requestDirectory)}").ConfigureAwait(false);
            return 2;
        }

        RequestComparisonRunRequest request = new RequestComparisonRunRequest(
            requestDirectory,
            endpointA,
            endpointB,
            options.Timeout,
            options.MaxConcurrency,
            modelName,
            comparisonOptions,
            new RequestExecutionOptions(contentTypeOverride),
            string.IsNullOrWhiteSpace(contractProfileId) ? null : new ContractProfileSelection(contractProfileId),
            commonHeaders: ParseHeaders(options.CommonHeaders),
            endpointAHeaders: MergeHeaders(preset?.EndpointAHeaders, ParseHeaders(options.EndpointAHeaders)),
            endpointBHeaders: MergeHeaders(preset?.EndpointBHeaders, ParseHeaders(options.EndpointBHeaders)));

        return await SubmitAndReportAsync(request, options, output, error, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RunFromProfileAsync(
        RequestCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ResolvedRunProfile resolved;
        try
        {
            // Resolving turns the profile's secret:// references into values right
            // before the run; those values never touch the profile file.
            resolved = await runProfileResolver.ResolveAsync(options.RunProfileId!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 2;
        }

        string? requestDirectory = options.RequestDirectory ?? resolved.Profile.RequestDirectory;
        if (string.IsNullOrWhiteSpace(requestDirectory))
        {
            await error.WriteLineAsync("The run profile does not set a request directory; pass one on the command line.").ConfigureAwait(false);
            return 2;
        }

        if (!Directory.Exists(requestDirectory))
        {
            await error.WriteLineAsync($"Request directory was not found: {Path.GetFullPath(requestDirectory)}").ConfigureAwait(false);
            return 2;
        }

        RequestComparisonRunRequest request = new RequestComparisonRunRequest(
            requestDirectory,
            options.EndpointA ?? resolved.EndpointA.Uri,
            options.EndpointB ?? resolved.EndpointB.Uri,
            options.Timeout,
            options.MaxConcurrency,
            comparisonOptions: resolved.Profile.Comparison,
            requestExecutionOptions: new RequestExecutionOptions(options.ContentTypeOverride),
            commonHeaders: ParseHeaders(options.CommonHeaders),
            endpointAHeaders: ParseHeaders(options.EndpointAHeaders),
            endpointBHeaders: ParseHeaders(options.EndpointBHeaders),
            pluginComparison: resolved.Selection);

        return await SubmitAndReportAsync(request, options, output, error, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> SubmitAndReportAsync(
        RequestComparisonRunRequest request,
        RequestCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            ComparisonRun run = await workflowUseCases
                .CreateRunFromDirectoryAsync(request, cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync($"Run: {run.Id.Value}").ConfigureAwait(false);

            run = await workflowUseCases
                .StartRunAsync(run.Id, cancellationToken)
                .ConfigureAwait(false);
            await WriteSummaryAsync(run, output).ConfigureAwait(false);

            if (run.Status == RunStatus.Completed && !string.IsNullOrWhiteSpace(options.ReportOutputDirectory))
            {
                StaticReportBundleWriteResult report = await workflowUseCases
                    .GenerateReportAsync(run.Id, options.ReportOutputDirectory, options.ReportAssetsDirectory, cancellationToken)
                    .ConfigureAwait(false);
                await output.WriteLineAsync($"Report: {report.OutputDirectory}").ConfigureAwait(false);
            }

            return run.Status == RunStatus.Completed ? 0 : 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task WriteSummaryAsync(ComparisonRun run, TextWriter output)
    {
        await output.WriteLineAsync($"Status: {run.Status}").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(run.ErrorMessage))
        {
            await output.WriteLineAsync($"Error: {run.ErrorMessage}").ConfigureAwait(false);
        }

        RunResultSummary? summary = run.Summary;
        if (summary is null)
        {
            return;
        }

        await output.WriteLineAsync($"Total: {summary.TotalPairs}").ConfigureAwait(false);
        await output.WriteLineAsync($"Equal: {summary.EqualPairs}").ConfigureAwait(false);
        await output.WriteLineAsync($"Different: {summary.DifferentPairs}").ConfigureAwait(false);
        await output.WriteLineAsync($"Status mismatches: {summary.StatusCodeMismatchPairs}").ConfigureAwait(false);
        await output.WriteLineAsync($"Both non-success: {summary.BothNonSuccessPairs}").ConfigureAwait(false);
        await output.WriteLineAsync($"Errors: {summary.ErrorPairs}").ConfigureAwait(false);

        if (summary.ExecutionMetrics is not null)
        {
            await output.WriteLineAsync($"Duration: {summary.ExecutionMetrics.TotalDuration.TotalMilliseconds:F0}ms").ConfigureAwait(false);
            await output.WriteLineAsync($"Request execution: {summary.ExecutionMetrics.RequestExecutionDuration.TotalMilliseconds:F0}ms").ConfigureAwait(false);
            await output.WriteLineAsync($"Comparison: {summary.ExecutionMetrics.ComparisonDuration.TotalMilliseconds:F0}ms").ConfigureAwait(false);
            await output.WriteLineAsync($"Finalization: {summary.ExecutionMetrics.FinalizationDuration.TotalMilliseconds:F0}ms").ConfigureAwait(false);
            await output.WriteLineAsync($"Artifacts retained: {summary.ExecutionMetrics.RetainedArtifactCount}").ConfigureAwait(false);
            await output.WriteLineAsync($"Artifacts trimmed by policy: {summary.ExecutionMetrics.TrimmedByPolicyArtifactCount}").ConfigureAwait(false);
            await output.WriteLineAsync($"Artifacts missing unexpectedly: {summary.ExecutionMetrics.MissingUnexpectedlyArtifactCount}").ConfigureAwait(false);
        }

        if (run.Diagnostics is not null)
        {
            await output.WriteLineAsync($"Slow paths: {run.Diagnostics.SlowRequestPaths.Count}").ConfigureAwait(false);
            await output.WriteLineAsync($"Exception diagnostics: {run.Diagnostics.Exceptions.Count}").ConfigureAwait(false);
        }
    }

    private static IReadOnlyDictionary<string, string> MergeHeaders(
        IReadOnlyDictionary<string, string>? presetHeaders,
        IReadOnlyDictionary<string, string> cliHeaders)
    {
        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (presetHeaders is not null)
        {
            foreach (KeyValuePair<string, string> header in presetHeaders)
            {
                headers[header.Key] = header.Value;
            }
        }

        foreach (KeyValuePair<string, string> header in cliHeaders)
        {
            headers[header.Key] = header.Value;
        }

        return headers;
    }

    private static IReadOnlyDictionary<string, string> ParseHeaders(IEnumerable<string> headerLines)
    {
        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string headerLine in headerLines)
        {
            int separatorIndex = headerLine.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                throw new InvalidOperationException($"Header '{headerLine}' must use Name: Value format.");
            }

            headers[headerLine[..separatorIndex].Trim()] = headerLine[(separatorIndex + 1)..].Trim();
        }

        return headers;
    }
}
