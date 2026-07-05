using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Cli;

public sealed class RequestCommandRunner
{
    private readonly IRequestComparisonWorkflowUseCases workflowUseCases;

    public RequestCommandRunner(IRequestComparisonWorkflowUseCases workflowUseCases)
    {
        this.workflowUseCases = workflowUseCases ?? throw new ArgumentNullException(nameof(workflowUseCases));
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

        if (!Directory.Exists(options.RequestDirectory))
        {
            await error.WriteLineAsync($"Request directory was not found: {Path.GetFullPath(options.RequestDirectory)}").ConfigureAwait(false);
            return 2;
        }

        try
        {
            RequestComparisonRunRequest request = new RequestComparisonRunRequest(
                options.RequestDirectory,
                options.EndpointA,
                options.EndpointB,
                options.Timeout,
                options.MaxConcurrency,
                options.ModelName,
                new ComparisonOptions(),
                new RequestExecutionOptions(options.ContentTypeOverride),
                commonHeaders: ParseHeaders(options.CommonHeaders),
                endpointAHeaders: ParseHeaders(options.EndpointAHeaders),
                endpointBHeaders: ParseHeaders(options.EndpointBHeaders));

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