using System.Collections.Concurrent;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

public sealed class BasicComparisonRunExecutor : IComparisonRunExecutor
{
    private readonly IRequestBatchStore requestBatchStore;
    private readonly IEndpointRequestSender endpointRequestSender;
    private readonly IRunArtifactStore runArtifactStore;
    private readonly IRunDetailStore runDetailStore;
    private readonly IResponseComparer responseComparer;

    public BasicComparisonRunExecutor(
        IRequestBatchStore requestBatchStore,
        IEndpointRequestSender endpointRequestSender,
        IRunArtifactStore runArtifactStore,
        IRunDetailStore runDetailStore)
        : this(
            requestBatchStore,
            endpointRequestSender,
            runArtifactStore,
            runDetailStore,
            new HashOnlyResponseComparer())
    {
    }

    public BasicComparisonRunExecutor(
        IRequestBatchStore requestBatchStore,
        IEndpointRequestSender endpointRequestSender,
        IRunArtifactStore runArtifactStore,
        IRunDetailStore runDetailStore,
        IResponseComparer responseComparer)
    {
        this.requestBatchStore = requestBatchStore;
        this.endpointRequestSender = endpointRequestSender;
        this.runArtifactStore = runArtifactStore;
        this.runDetailStore = runDetailStore;
        this.responseComparer = responseComparer;
    }

    public async Task<RunResultSummary> ExecuteAsync(
        ComparisonRun run,
        IRunProgressReporter progressReporter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(progressReporter);

        await progressReporter
            .ReportAsync(RunStatus.Parsing, new RunProgress(5, "Loading request batch."), cancellationToken)
            .ConfigureAwait(false);

        RequestBatchManifest manifest = await requestBatchStore
            .LoadManifestAsync(run.Options.RequestBatch, cancellationToken)
            .ConfigureAwait(false);

        int totalRequests = manifest.Requests.Count;
        await progressReporter
            .ReportAsync(RunStatus.Executing, new RunProgress(10, "Executing requests.", 0, totalRequests), cancellationToken)
            .ConfigureAwait(false);

        ConcurrentBag<RequestPairResult> results = new ConcurrentBag<RequestPairResult>();
        int completedRequests = 0;
        ParallelOptions parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = run.Options.MaxConcurrency,
        };

        await Parallel.ForEachAsync(manifest.Requests, parallelOptions, async (request, token) =>
        {
            RequestPairResult result = await ExecutePairAsync(run, request, token).ConfigureAwait(false);
            results.Add(result);

            int completed = Interlocked.Increment(ref completedRequests);
            await progressReporter
                .ReportAsync(
                    RunStatus.Executing,
                    new RunProgress(
                        CalculateExecutionPercent(completed, totalRequests),
                        $"Executed {completed} of {totalRequests} requests.",
                        completed,
                        totalRequests),
                    token)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);

        List<RequestPairResult> orderedResults = results
            .OrderBy(result => result.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await progressReporter
            .ReportAsync(RunStatus.Comparing, new RunProgress(90, "Classifying response pairs.", totalRequests, totalRequests), cancellationToken)
            .ConfigureAwait(false);

        await progressReporter
            .ReportAsync(RunStatus.Finalizing, new RunProgress(95, "Saving result details.", totalRequests, totalRequests), cancellationToken)
            .ConfigureAwait(false);

        RunDetailReference detailReference = await runDetailStore
            .SaveDetailsAsync(run.Id, orderedResults, cancellationToken)
            .ConfigureAwait(false);

        return RequestPairResult.Summarize(orderedResults, detailReference);
    }

    private async Task<RequestPairResult> ExecutePairAsync(
        ComparisonRun run,
        RequestItem request,
        CancellationToken cancellationToken)
    {
        Task<EndpointExecutionResult> endpointATask = ExecuteEndpointAsync(run, request, EndpointSlot.A, cancellationToken);
        Task<EndpointExecutionResult> endpointBTask = ExecuteEndpointAsync(run, request, EndpointSlot.B, cancellationToken);

        await Task.WhenAll(endpointATask, endpointBTask).ConfigureAwait(false);

        EndpointExecutionResult endpointA = await endpointATask.ConfigureAwait(false);
        EndpointExecutionResult endpointB = await endpointBTask.ConfigureAwait(false);
        string? errorMessage = BuildErrorMessage(endpointA, endpointB);

        return await responseComparer
            .CompareAsync(
                request,
                run.Options,
                endpointA.Metadata,
                endpointB.Metadata,
                errorMessage,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<EndpointExecutionResult> ExecuteEndpointAsync(
        ComparisonRun run,
        RequestItem request,
        EndpointSlot endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            EndpointDefinition endpointDefinition = endpoint == EndpointSlot.A
                ? run.Options.EndpointA
                : run.Options.EndpointB;

            await using Stream requestBody = await requestBatchStore
                .OpenRequestBodyAsync(run.Options.RequestBatch, request, cancellationToken)
                .ConfigureAwait(false);

            EndpointRequest endpointRequest = new EndpointRequest(
                endpoint,
                endpointDefinition,
                request,
                requestBody,
                run.Options.RequestExecution.ContentTypeOverride ?? request.ContentType,
                run.Options.Timeout,
                MergeHeaders(endpointDefinition.Headers, request.Headers, request.GetHeaders(endpoint)));

            await using EndpointResponse response = await endpointRequestSender
                .SendAsync(endpointRequest, cancellationToken)
                .ConfigureAwait(false);

            await using Stream? maskedBody = await ResponseMasker
                .MaskAsync(response.Body, response.ContentType, run.Options.Comparison.MaskRules, cancellationToken)
                .ConfigureAwait(false);
            Stream bodyToPersist = maskedBody ?? response.Body;

            ResponseArtifactMetadata metadata = await runArtifactStore
                .SaveResponseAsync(
                    run.Id,
                    endpoint,
                    request,
                    response.StatusCode,
                    response.ContentType,
                    bodyToPersist,
                    cancellationToken)
                .ConfigureAwait(false);

            return EndpointExecutionResult.Success(metadata);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return EndpointExecutionResult.Failure(ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return EndpointExecutionResult.Failure(ex.Message);
        }
    }

    private int CalculateExecutionPercent(int completedRequests, int totalRequests)
    {
        if (totalRequests == 0)
        {
            return 80;
        }

        return 10 + (int)Math.Round((completedRequests / (double)totalRequests) * 75);
    }

    private IReadOnlyDictionary<string, string> MergeHeaders(
        params IReadOnlyDictionary<string, string>[] headerSets)
    {
        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IReadOnlyDictionary<string, string> headerSet in headerSets)
        {
            foreach (KeyValuePair<string, string> header in headerSet)
            {
                headers[header.Key] = header.Value;
            }
        }

        return headers;
    }

    private string? BuildErrorMessage(
        EndpointExecutionResult endpointA,
        EndpointExecutionResult endpointB)
    {
        string[] errors = new[] { endpointA.ErrorMessage, endpointB.ErrorMessage }
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => error!)
            .ToArray();

        return errors.Length == 0 ? null : string.Join("; ", errors);
    }

    private sealed class EndpointExecutionResult
    {
        private EndpointExecutionResult(ResponseArtifactMetadata? metadata, string? errorMessage)
        {
            Metadata = metadata;
            ErrorMessage = errorMessage;
        }

        public ResponseArtifactMetadata? Metadata { get; }

        public string? ErrorMessage { get; }

        public static EndpointExecutionResult Success(ResponseArtifactMetadata metadata) =>
            new EndpointExecutionResult(metadata, null);

        public static EndpointExecutionResult Failure(string errorMessage) =>
            new EndpointExecutionResult(null, errorMessage);
    }
}
