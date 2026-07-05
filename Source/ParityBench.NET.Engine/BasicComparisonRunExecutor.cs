using System.Collections.Concurrent;
using System.Diagnostics;

using ParityBench.NET.Application.AlternateContracts;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Comparison;
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
    private readonly IAlternateContractProfileRegistry? alternateContractProfileRegistry;

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
            new RawTextResponseComparer(runArtifactStore, new HashOnlyResponseComparer()))
    {
    }

    public BasicComparisonRunExecutor(
        IRequestBatchStore requestBatchStore,
        IEndpointRequestSender endpointRequestSender,
        IRunArtifactStore runArtifactStore,
        IRunDetailStore runDetailStore,
        IResponseComparer responseComparer)
        : this(
            requestBatchStore,
            endpointRequestSender,
            runArtifactStore,
            runDetailStore,
            responseComparer,
            null)
    {
    }

    public BasicComparisonRunExecutor(
        IRequestBatchStore requestBatchStore,
        IEndpointRequestSender endpointRequestSender,
        IRunArtifactStore runArtifactStore,
        IRunDetailStore runDetailStore,
        IResponseComparer responseComparer,
        IAlternateContractProfileRegistry? alternateContractProfileRegistry)
    {
        this.requestBatchStore = requestBatchStore;
        this.endpointRequestSender = endpointRequestSender;
        this.runArtifactStore = runArtifactStore;
        this.runDetailStore = runDetailStore;
        this.responseComparer = responseComparer is RawTextResponseComparer
            ? responseComparer
            : new RawTextResponseComparer(runArtifactStore, responseComparer);
        this.alternateContractProfileRegistry = alternateContractProfileRegistry;
    }

    public async Task<RunResultSummary> ExecuteAsync(
        ComparisonRun run,
        IRunProgressReporter progressReporter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(progressReporter);

        Stopwatch totalStopwatch = Stopwatch.StartNew();
        IAlternateContractProfile? alternateContractProfile = ResolveAlternateContractProfile(run.Options);
        RunOptions comparisonOptions = alternateContractProfile is null
            ? run.Options
            : CreateRunOptionsWithProfileDefaults(run.Options, alternateContractProfile);

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
        RunExecutionCounters counters = new RunExecutionCounters();
        int completedRequests = 0;
        ParallelOptions parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = run.Options.MaxConcurrency,
        };

        Stopwatch requestExecutionStopwatch = Stopwatch.StartNew();
        await Parallel.ForEachAsync(manifest.Requests, parallelOptions, async (request, token) =>
        {
            RequestPairResult result = await ExecutePairAsync(run, comparisonOptions, request, alternateContractProfile, counters, token).ConfigureAwait(false);
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
        requestExecutionStopwatch.Stop();

        Stopwatch comparisonStopwatch = Stopwatch.StartNew();
        List<RequestPairResult> orderedResults = results
            .OrderBy(result => result.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await progressReporter
            .ReportAsync(RunStatus.Comparing, new RunProgress(90, "Classifying response pairs.", totalRequests, totalRequests), cancellationToken)
            .ConfigureAwait(false);
        comparisonStopwatch.Stop();

        Stopwatch finalizationStopwatch = Stopwatch.StartNew();
        await progressReporter
            .ReportAsync(RunStatus.Finalizing, new RunProgress(95, "Saving result details.", totalRequests, totalRequests), cancellationToken)
            .ConfigureAwait(false);

        RunDetailReference detailReference = await runDetailStore
            .SaveDetailsAsync(run.Id, orderedResults, cancellationToken)
            .ConfigureAwait(false);
        finalizationStopwatch.Stop();
        totalStopwatch.Stop();

        RunExecutionMetrics executionMetrics = new RunExecutionMetrics(
            totalStopwatch.Elapsed,
            requestExecutionStopwatch.Elapsed,
            comparisonStopwatch.Elapsed,
            finalizationStopwatch.Elapsed,
            totalRequests,
            run.Options.MaxConcurrency,
            counters.ResponseBytesWritten);

        return RequestPairResult.Summarize(orderedResults, detailReference, executionMetrics);
    }
    private async Task<RequestPairResult> ExecutePairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RequestItem request,
        IAlternateContractProfile? alternateContractProfile,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        Task<EndpointExecutionResult> endpointATask = ExecuteEndpointAsync(run, request, EndpointSlot.A, alternateContractProfile, counters, cancellationToken);
        Task<EndpointExecutionResult> endpointBTask = ExecuteEndpointAsync(run, request, EndpointSlot.B, alternateContractProfile, counters, cancellationToken);

        await Task.WhenAll(endpointATask, endpointBTask).ConfigureAwait(false);

        EndpointExecutionResult endpointA = await endpointATask.ConfigureAwait(false);
        EndpointExecutionResult endpointB = await endpointBTask.ConfigureAwait(false);

        if (alternateContractProfile is not null)
        {
            return await CompleteAlternateContractPairAsync(
                run,
                comparisonOptions,
                request,
                alternateContractProfile,
                endpointA,
                endpointB,
                counters,
                cancellationToken)
                .ConfigureAwait(false);
        }

        string? errorMessage = BuildErrorMessage(endpointA, endpointB);
        return await responseComparer
            .CompareAsync(
                request,
                comparisonOptions,
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
        IAlternateContractProfile? alternateContractProfile,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        try
        {
            EndpointDefinition endpointDefinition = endpoint == EndpointSlot.A
                ? run.Options.EndpointA
                : run.Options.EndpointB;

            PreparedRequest preparedRequest = endpoint == EndpointSlot.B && alternateContractProfile is not null
                ? await PrepareAlternateEndpointBRequestAsync(run, request, endpointDefinition, alternateContractProfile, cancellationToken).ConfigureAwait(false)
                : await PrepareRegularRequestAsync(run, request, endpoint, endpointDefinition, cancellationToken).ConfigureAwait(false);

            await using (preparedRequest)
            {
                EndpointRequest endpointRequest = new EndpointRequest(
                    endpoint,
                    endpointDefinition,
                    request,
                    preparedRequest.Body,
                    preparedRequest.ContentType,
                    run.Options.Timeout,
                    preparedRequest.Headers);

                await using EndpointResponse response = await endpointRequestSender
                    .SendAsync(endpointRequest, cancellationToken)
                    .ConfigureAwait(false);

                ResponseArtifactMetadata metadata = await PersistResponseAsync(
                    run.Id,
                    endpoint,
                    request,
                    response.StatusCode,
                    response.ContentType,
                    response.Body,
                    run.Options.Comparison.MaskRules,
                    counters,
                    cancellationToken)
                    .ConfigureAwait(false);

                return EndpointExecutionResult.Persisted(endpoint, metadata);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return EndpointExecutionResult.Failure(endpoint, ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return EndpointExecutionResult.Failure(endpoint, ex.Message);
        }
    }
    private async Task<RequestPairResult> CompleteAlternateContractPairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RequestItem request,
        IAlternateContractProfile profile,
        EndpointExecutionResult endpointA,
        EndpointExecutionResult endpointB,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        string? errorMessage = BuildErrorMessage(endpointA, endpointB);
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            return await responseComparer
                .CompareAsync(request, comparisonOptions, endpointA.Metadata, endpointB.Metadata, errorMessage, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!endpointA.IsSuccessStatusCode || !endpointB.IsSuccessStatusCode)
        {
            return await responseComparer
                .CompareAsync(request, comparisonOptions, endpointA.Metadata, endpointB.Metadata, null, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            ResponseArtifactMetadata canonicalA = await NormalizeAndPersistResponseAsync(
                run,
                request,
                profile,
                endpointA,
                comparisonOptions.Comparison.MaskRules,
                counters,
                cancellationToken)
                .ConfigureAwait(false);
            ResponseArtifactMetadata canonicalB = await NormalizeAndPersistResponseAsync(
                run,
                request,
                profile,
                endpointB,
                comparisonOptions.Comparison.MaskRules,
                counters,
                cancellationToken)
                .ConfigureAwait(false);

            return await responseComparer
                .CompareAsync(request, comparisonOptions, canonicalA, canonicalB, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RequestPairResult(
                request.RelativePath,
                RequestPairOutcome.ExecutionFailed,
                errorMessage: ex.Message);
        }
    }
    private async Task<PreparedRequest> PrepareRegularRequestAsync(
        ComparisonRun run,
        RequestItem request,
        EndpointSlot endpoint,
        EndpointDefinition endpointDefinition,
        CancellationToken cancellationToken)
    {
        Stream requestBody = await requestBatchStore
            .OpenRequestBodyAsync(run.Options.RequestBatch, request, cancellationToken)
            .ConfigureAwait(false);

        return new PreparedRequest(
            requestBody,
            run.Options.RequestExecution.ContentTypeOverride ?? request.ContentType,
            MergeHeaders(endpointDefinition.Headers, request.Headers, request.GetHeaders(endpoint)));
    }

    private async Task<PreparedRequest> PrepareAlternateEndpointBRequestAsync(
        ComparisonRun run,
        RequestItem request,
        EndpointDefinition endpointDefinition,
        IAlternateContractProfile profile,
        CancellationToken cancellationToken)
    {
        PayloadFormat sourceFormat = DetectPayloadFormat(request.ContentType, request.RelativePath)
            ?? throw new InvalidOperationException(
                $"Request '{request.RelativePath}' does not have a supported serialization format for alternate contract processing.");

        if (!profile.SupportedSourceRequestFormats.Contains(sourceFormat))
        {
            throw new InvalidOperationException(
                $"Alternate contract profile '{profile.ProfileId}' does not support source request format '{sourceFormat}' for request '{request.RelativePath}'.");
        }

        async ValueTask<Stream> OpenSourceRequestBodyAsync(CancellationToken token) =>
            await requestBatchStore
                .OpenRequestBodyAsync(run.Options.RequestBatch, request, token)
                .ConfigureAwait(false);

        PreparedAlternateContractRequest prepared = await profile
            .PrepareEndpointBRequestAsync(
                new AlternateContractRequestPreparationContext(request, OpenSourceRequestBodyAsync, sourceFormat),
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, string> headers = MergeHeaders(endpointDefinition.Headers, request.Headers, request.GetHeaders(EndpointSlot.B));
        if (prepared.Headers is not null)
        {
            foreach (KeyValuePair<string, string> header in prepared.Headers)
            {
                headers[header.Key] = header.Value;
            }
        }

        headers.Remove("SOAPAction");

        Stream preparedBody = await prepared.Body.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        return new PreparedRequest(
            preparedBody,
            prepared.ContentType,
            headers,
            prepared.Body);
    }
    private async Task<ResponseArtifactMetadata> NormalizeAndPersistResponseAsync(
        ComparisonRun run,
        RequestItem request,
        IAlternateContractProfile profile,
        EndpointExecutionResult endpointResult,
        IReadOnlyList<MaskRuleDefinition> maskRules,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        if (endpointResult.Metadata is null)
        {
            throw new InvalidOperationException($"Endpoint {endpointResult.Endpoint} did not produce a response artifact to normalize.");
        }

        PayloadFormat sourceFormat = endpointResult.Endpoint == EndpointSlot.B
            ? profile.AlternateResponseFormat
            : DetectPayloadFormat(endpointResult.ContentType, request.RelativePath) ?? profile.CanonicalResponseFormat;

        async ValueTask<Stream> OpenSourceResponseBodyAsync(CancellationToken token) =>
            await runArtifactStore
                .OpenReadAsync(endpointResult.Metadata.Artifact, token)
                .ConfigureAwait(false);

        AlternateContractResponseNormalizationContext context = new AlternateContractResponseNormalizationContext(
            request,
            endpointResult.Endpoint,
            OpenSourceResponseBodyAsync,
            endpointResult.ContentType,
            sourceFormat);

        NormalizedAlternateContractResponse normalized = endpointResult.Endpoint == EndpointSlot.A
            ? await profile.NormalizeEndpointAResponseAsync(context, cancellationToken).ConfigureAwait(false)
            : await profile.NormalizeEndpointBResponseAsync(context, cancellationToken).ConfigureAwait(false);

        await using ContractPayload normalizedPayload = normalized.Body;
        await using Stream normalizedStream = await normalizedPayload.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        return await PersistResponseAsync(
            run.Id,
            endpointResult.Endpoint,
            request,
            endpointResult.StatusCode ?? 0,
            normalizedPayload.ContentType,
            normalizedStream,
            maskRules,
            counters,
            cancellationToken)
            .ConfigureAwait(false);
    }
    private async Task<ResponseArtifactMetadata> PersistResponseAsync(
        RunId runId,
        EndpointSlot endpoint,
        RequestItem request,
        int statusCode,
        string? contentType,
        Stream body,
        IReadOnlyList<MaskRuleDefinition> maskRules,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        await using Stream? maskedBody = await ResponseMasker
            .MaskAsync(body, contentType, maskRules, cancellationToken)
            .ConfigureAwait(false);
        Stream bodyToPersist = maskedBody ?? body;

        ResponseArtifactMetadata metadata = await runArtifactStore
            .SaveResponseAsync(
                runId,
                endpoint,
                request,
                statusCode,
                contentType,
                bodyToPersist,
                cancellationToken)
            .ConfigureAwait(false);

        counters.AddResponseBytes(metadata.ContentLength);
        return metadata;
    }
    private IAlternateContractProfile? ResolveAlternateContractProfile(RunOptions options)
    {
        if (options.AlternateContract is null)
        {
            return null;
        }

        if (alternateContractProfileRegistry is null)
        {
            throw new InvalidOperationException("An alternate contract profile registry is required when alternate contract options are configured.");
        }

        return alternateContractProfileRegistry.Resolve(options.ModelName, options.AlternateContract.ProfileId);
    }

    private static RunOptions CreateRunOptionsWithProfileDefaults(
        RunOptions options,
        IAlternateContractProfile profile)
    {
        ComparisonOptions current = options.Comparison;
        ComparisonOptions comparisonOptions = new ComparisonOptions(
            current.IgnoreCollectionOrder,
            current.IgnoreStringCase,
            current.IgnoreTrailingWhitespaceAtEnd,
            current.TreatNullAndEmptyCollectionsAsEqual,
            current.IgnoreXmlNamespaces,
            current.MaxDifferences,
            profile.DefaultIgnoreRules.Concat(current.IgnoreRules),
            current.SmartIgnoreRules,
            current.MaskRules);

        return new RunOptions(
            options.RequestBatch,
            options.EndpointA,
            options.EndpointB,
            options.Timeout,
            options.MaxConcurrency,
            options.ModelName,
            comparisonOptions,
            options.RequestExecution,
            options.AlternateContract);
    }

    private int CalculateExecutionPercent(int completedRequests, int totalRequests)
    {
        if (totalRequests == 0)
        {
            return 80;
        }

        return 10 + (int)Math.Round((completedRequests / (double)totalRequests) * 75);
    }

    private static Dictionary<string, string> MergeHeaders(
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

    private static PayloadFormat? DetectPayloadFormat(string? contentType, string relativePath)
    {
        if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            return PayloadFormat.Json;
        }

        if (contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true)
        {
            return PayloadFormat.Xml;
        }

        string extension = Path.GetExtension(relativePath);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return PayloadFormat.Json;
        }

        if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return PayloadFormat.Xml;
        }

        return null;
    }

    private sealed class RunExecutionCounters
    {
        private long responseBytesWritten;

        public long ResponseBytesWritten => Interlocked.Read(ref responseBytesWritten);

        public void AddResponseBytes(long bytesWritten) =>
            Interlocked.Add(ref responseBytesWritten, bytesWritten);
    }
    private sealed class PreparedRequest : IAsyncDisposable
    {
        private readonly IAsyncDisposable? owner;

        public PreparedRequest(
            Stream body,
            string contentType,
            IReadOnlyDictionary<string, string> headers,
            IAsyncDisposable? owner = null)
        {
            Body = body;
            ContentType = contentType;
            Headers = headers;
            this.owner = owner;
        }

        public Stream Body { get; }

        public string ContentType { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public async ValueTask DisposeAsync()
        {
            await Body.DisposeAsync().ConfigureAwait(false);

            if (owner is not null)
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class EndpointExecutionResult
    {
        private EndpointExecutionResult(
            EndpointSlot endpoint,
            ResponseArtifactMetadata? metadata,
            string? errorMessage)
        {
            Endpoint = endpoint;
            Metadata = metadata;
            ErrorMessage = errorMessage;
        }

        public EndpointSlot Endpoint { get; }

        public ResponseArtifactMetadata? Metadata { get; }

        public int? StatusCode => Metadata?.StatusCode;

        public string? ContentType => Metadata?.ContentType;

        public string? ErrorMessage { get; }

        public bool IsSuccessStatusCode => StatusCode is >= 200 and <= 299;

        public static EndpointExecutionResult Persisted(EndpointSlot endpoint, ResponseArtifactMetadata metadata) =>
            new EndpointExecutionResult(endpoint, metadata, null);

        public static EndpointExecutionResult Failure(EndpointSlot endpoint, string errorMessage) =>
            new EndpointExecutionResult(endpoint, null, errorMessage);
    }
}


