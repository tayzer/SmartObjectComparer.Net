using System.Collections.Concurrent;
using System.Diagnostics;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
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
    private readonly IContractProfileRegistry? contractProfileRegistry;
    private readonly IObservabilityRecorder observabilityRecorder;

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
        IContractProfileRegistry? contractProfileRegistry,
        IObservabilityRecorder? observabilityRecorder = null)
    {
        this.requestBatchStore = requestBatchStore;
        this.endpointRequestSender = endpointRequestSender;
        this.runArtifactStore = runArtifactStore;
        this.runDetailStore = runDetailStore;
        this.responseComparer = responseComparer is RawTextResponseComparer
            ? responseComparer
            : new RawTextResponseComparer(runArtifactStore, responseComparer);
        this.contractProfileRegistry = contractProfileRegistry;
        this.observabilityRecorder = observabilityRecorder ?? NoOpObservabilityRecorder.Instance;
    }

    public async Task<RunResultSummary> ExecuteAsync(
        ComparisonRun run,
        IRunProgressReporter progressReporter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(progressReporter);

        Stopwatch totalStopwatch = Stopwatch.StartNew();
        IContractProfile? contractProfile = ResolveContractProfile(run.Options);
        RunOptions comparisonOptions = contractProfile is null
            ? run.Options
            : CreateRunOptionsWithProfileDefaults(run.Options, contractProfile);

        await progressReporter
            .ReportAsync(RunStatus.Parsing, new RunProgress(5, "Loading request batch."), cancellationToken, force: true)
            .ConfigureAwait(false);

        RequestBatchManifest manifest = await requestBatchStore
            .LoadManifestAsync(run.Options.RequestBatch, cancellationToken)
            .ConfigureAwait(false);

        int totalRequests = manifest.Requests.Count;
        await progressReporter
            .ReportAsync(RunStatus.Executing, new RunProgress(10, "Executing requests.", 0, totalRequests), cancellationToken, force: true)
            .ConfigureAwait(false);

        RunExecutionCounters counters = new RunExecutionCounters();
        RunSummaryAccumulator summaryAccumulator = new RunSummaryAccumulator();
        int completedRequests = 0;
        int chunkSize = Math.Max(1, comparisonOptions.LargeRun.ChunkSize);
        IReadOnlyList<IReadOnlyList<RequestItem>> chunks = Partition(manifest.Requests, chunkSize);
        ParallelOptions parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = comparisonOptions.MaxConcurrency,
        };

        Stopwatch requestExecutionStopwatch = Stopwatch.StartNew();
        Stopwatch comparisonStopwatch = new Stopwatch();
        Stopwatch finalizationStopwatch = new Stopwatch();
        await using IRunDetailWriter detailWriter = await runDetailStore
            .CreateWriterAsync(run.Id, comparisonOptions.LargeRun.DetailPageSize, cancellationToken)
            .ConfigureAwait(false);

        for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            IReadOnlyList<RequestItem> chunk = chunks[chunkIndex];
            ConcurrentBag<RequestPairResult> chunkResults = new ConcurrentBag<RequestPairResult>();
            await Parallel.ForEachAsync(chunk, parallelOptions, async (request, token) =>
            {
                Stopwatch requestPathStopwatch = Stopwatch.StartNew();
                RequestPairResult result = await ExecutePairAsync(run, comparisonOptions, request, contractProfile, counters, token).ConfigureAwait(false);
                requestPathStopwatch.Stop();
                observabilityRecorder.RecordRequestPath(run.Id, request.RelativePath, requestPathStopwatch.Elapsed);
                chunkResults.Add(result);

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

            comparisonStopwatch.Start();
            List<RequestPairResult> orderedChunkResults = chunkResults
                .OrderBy(result => result.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            comparisonStopwatch.Stop();

            summaryAccumulator.Add(orderedChunkResults);
            await detailWriter.AppendAsync(orderedChunkResults, cancellationToken).ConfigureAwait(false);

            await progressReporter
                .ReportAsync(
                    RunStatus.Comparing,
                    new RunProgress(
                        CalculateExecutionPercent(completedRequests, totalRequests),
                        chunks.Count > 1
                            ? $"Persisted chunk {chunkIndex + 1} of {chunks.Count}."
                            : "Persisted comparison results.",
                        completedRequests,
                        totalRequests),
                    cancellationToken,
                    force: true)
                .ConfigureAwait(false);
        }

        requestExecutionStopwatch.Stop();

        finalizationStopwatch.Start();
        await progressReporter
            .ReportAsync(RunStatus.Finalizing, new RunProgress(95, "Saving result details.", totalRequests, totalRequests), cancellationToken, force: true)
            .ConfigureAwait(false);

        RunDetailReference detailReference = await detailWriter.CompleteAsync(cancellationToken).ConfigureAwait(false);
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

        RecordRunPhases(run.Id, executionMetrics);
        return summaryAccumulator.ToSummary(detailReference, executionMetrics);
    }

    private async Task<RequestPairResult> ExecutePairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RequestItem request,
        IContractProfile? contractProfile,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        Task<EndpointExecutionResult> endpointATask = ExecuteEndpointAsync(run, comparisonOptions, request, EndpointSlot.A, contractProfile, counters, cancellationToken);
        Task<EndpointExecutionResult> endpointBTask = ExecuteEndpointAsync(run, comparisonOptions, request, EndpointSlot.B, contractProfile, counters, cancellationToken);

        await Task.WhenAll(endpointATask, endpointBTask).ConfigureAwait(false);

        EndpointExecutionResult endpointA = await endpointATask.ConfigureAwait(false);
        EndpointExecutionResult endpointB = await endpointBTask.ConfigureAwait(false);

        if (contractProfile is not null)
        {
            return await CompleteContractProfilePairAsync(
                run,
                comparisonOptions,
                request,
                contractProfile,
                endpointA,
                endpointB,
                counters,
                cancellationToken)
                .ConfigureAwait(false);
        }

        string? errorMessage = BuildErrorMessage(endpointA, endpointB);
        RequestPairResult pairResult = await responseComparer
            .CompareAsync(
                request,
                comparisonOptions,
                endpointA.Metadata,
                endpointB.Metadata,
                errorMessage,
                cancellationToken)
            .ConfigureAwait(false);

        return await AttachFocusedRawContentAsync(pairResult, run.Id, comparisonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EndpointExecutionResult> ExecuteEndpointAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RequestItem request,
        EndpointSlot endpoint,
        IContractProfile? contractProfile,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        try
        {
            EndpointDefinition endpointDefinition = endpoint == EndpointSlot.A
                ? run.Options.EndpointA
                : run.Options.EndpointB;

            PreparedRequest preparedRequest = contractProfile is not null
                ? await PrepareContractRequestAsync(run, request, endpoint, endpointDefinition, contractProfile, cancellationToken).ConfigureAwait(false)
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
                    comparisonOptions.Comparison.MaskRules,
                    counters,
                    cancellationToken)
                    .ConfigureAwait(false);

                return EndpointExecutionResult.Persisted(endpoint, metadata);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            observabilityRecorder.RecordException(run.Id, "EndpointExecution", ex, request.RelativePath, endpoint);
            return EndpointExecutionResult.Failure(endpoint, ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            observabilityRecorder.RecordException(run.Id, "EndpointExecution", ex, request.RelativePath, endpoint);
            return EndpointExecutionResult.Failure(endpoint, ex.Message);
        }
    }

    private async Task<RequestPairResult> CompleteContractProfilePairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RequestItem request,
        IContractProfile profile,
        EndpointExecutionResult endpointA,
        EndpointExecutionResult endpointB,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        string? errorMessage = BuildErrorMessage(endpointA, endpointB);
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            RequestPairResult pairResult = await responseComparer
                .CompareAsync(request, comparisonOptions, endpointA.Metadata, endpointB.Metadata, errorMessage, cancellationToken)
                .ConfigureAwait(false);

            return await AttachFocusedRawContentAsync(pairResult, run.Id, comparisonOptions, cancellationToken).ConfigureAwait(false);
        }

        if (!endpointA.IsSuccessStatusCode || !endpointB.IsSuccessStatusCode)
        {
            RequestPairResult pairResult = await responseComparer
                .CompareAsync(request, comparisonOptions, endpointA.Metadata, endpointB.Metadata, null, cancellationToken)
                .ConfigureAwait(false);

            return await AttachFocusedRawContentAsync(pairResult, run.Id, comparisonOptions, cancellationToken).ConfigureAwait(false);
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

            RequestPairResult pairResult = await responseComparer
                .CompareAsync(request, comparisonOptions, canonicalA, canonicalB, null, cancellationToken)
                .ConfigureAwait(false);

            return await AttachFocusedRawContentAsync(pairResult, run.Id, comparisonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            observabilityRecorder.RecordException(run.Id, "ContractProfileComparison", ex, request.RelativePath);
            return new RequestPairResult(
                request.RelativePath,
                RequestPairOutcome.ExecutionFailed,
                errorMessage: ex.Message);
        }
    }

    private Task<RequestPairResult> AttachFocusedRawContentAsync(
        RequestPairResult result,
        RunId runId,
        RunOptions comparisonOptions,
        CancellationToken cancellationToken) =>
        FocusedRawContentBuilder.TryAttachFocusedRawContentAsync(
            result,
            runId,
            comparisonOptions.Comparison,
            runArtifactStore,
            cancellationToken);

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

    private async Task<PreparedRequest> PrepareContractRequestAsync(
        ComparisonRun run,
        RequestItem request,
        EndpointSlot endpoint,
        EndpointDefinition endpointDefinition,
        IContractProfile profile,
        CancellationToken cancellationToken)
    {
        PayloadFormat sourceFormat = DetectPayloadFormat(request.ContentType, request.RelativePath)
            ?? throw new InvalidOperationException(
                $"Request '{request.RelativePath}' does not have a supported serialization format for contract profile processing.");

        if (!profile.EndpointA.SupportedSourceRequestFormats.Contains(sourceFormat))
        {
            throw new InvalidOperationException(
                $"Contract profile '{profile.ProfileId}' does not support source request format '{sourceFormat}' for request '{request.RelativePath}'.");
        }

        async ValueTask<Stream> OpenSourceRequestBodyAsync(CancellationToken token) =>
            await requestBatchStore
                .OpenRequestBodyAsync(run.Options.RequestBatch, request, token)
                .ConfigureAwait(false);

        string sourceContentType = run.Options.RequestExecution.ContentTypeOverride ?? request.ContentType;
        PreparedContractRequest prepared = await profile
            .PrepareRequestAsync(
                endpoint,
                new ContractRequestPreparationContext(request, OpenSourceRequestBodyAsync, sourceFormat, sourceContentType),
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, string> headers = MergeHeaders(endpointDefinition.Headers, request.Headers, request.GetHeaders(endpoint));
        if (prepared.Headers is not null)
        {
            foreach (KeyValuePair<string, string> header in prepared.Headers)
            {
                headers[header.Key] = header.Value;
            }
        }

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
        IContractProfile profile,
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
            ? profile.EndpointB.ResponseFormat
            : DetectPayloadFormat(endpointResult.ContentType, request.RelativePath) ?? profile.CanonicalResponseFormat;

        async ValueTask<Stream> OpenSourceResponseBodyAsync(CancellationToken token) =>
            await runArtifactStore
                .OpenReadAsync(endpointResult.Metadata.Artifact, token)
                .ConfigureAwait(false);

        ContractResponseNormalizationContext context = new ContractResponseNormalizationContext(
            request,
            endpointResult.Endpoint,
            OpenSourceResponseBodyAsync,
            endpointResult.ContentType,
            sourceFormat);

        NormalizedContractResponse normalized = await profile
            .NormalizeResponseAsync(endpointResult.Endpoint, context, cancellationToken)
            .ConfigureAwait(false);

        await using ContractPayload normalizedPayload = normalized.Body;
        await using Stream normalizedStream = await normalizedPayload.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        RequestItem canonicalArtifactRequest = CreateCanonicalArtifactRequest(request, endpointResult.Endpoint, normalizedPayload.ContentType);
        return await PersistResponseAsync(
            run.Id,
            endpointResult.Endpoint,
            canonicalArtifactRequest,
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

    private static RequestItem CreateCanonicalArtifactRequest(
        RequestItem request,
        EndpointSlot endpoint,
        string contentType) =>
        new RequestItem(
            $"canonical/{endpoint}/{request.RelativePath}",
            contentType,
            request.ContentLength,
            request.Headers,
            request.HeadersA,
            request.HeadersB);

    private IContractProfile? ResolveContractProfile(RunOptions options)
    {
        if (contractProfileRegistry is null)
        {
            if (options.ContractProfile is null)
            {
                return null;
            }

            throw new InvalidOperationException("A contract profile registry is required when contract profile options are configured.");
        }

        return contractProfileRegistry.Resolve(options.ResponseModelName, options.ContractProfile);
    }

    private static RunOptions CreateRunOptionsWithProfileDefaults(
        RunOptions options,
        IContractProfile profile)
    {
        ComparisonOptions current = options.Comparison;
        ComparisonRuleDefaults profileDefaults = profile.DefaultComparisonRules;
        ComparisonOptions comparisonOptions = new ComparisonOptions(
            profileDefaults.IgnoreCollectionOrder || current.IgnoreCollectionOrder,
            profileDefaults.IgnoreStringCase || current.IgnoreStringCase,
            profileDefaults.IgnoreTrailingWhitespaceAtEnd || current.IgnoreTrailingWhitespaceAtEnd,
            profileDefaults.TreatNullAndEmptyCollectionsAsEqual || current.TreatNullAndEmptyCollectionsAsEqual,
            profileDefaults.IgnoreXmlNamespaces || current.IgnoreXmlNamespaces,
            current.MaxDifferences,
            profileDefaults.IgnoreRules.Concat(current.IgnoreRules),
            profileDefaults.SmartIgnoreRules.Concat(current.SmartIgnoreRules),
            profileDefaults.MaskRules.Concat(current.MaskRules));

        return new RunOptions(
            options.RequestBatch,
            options.EndpointA,
            options.EndpointB,
            options.Timeout,
            options.MaxConcurrency,
            options.ResponseModelName,
            comparisonOptions,
            options.RequestExecution,
            options.ContractProfile,
            options.LargeRun);
    }

    private void RecordRunPhases(RunId runId, RunExecutionMetrics executionMetrics)
    {
        observabilityRecorder.RecordRunPhase(runId, "Total", executionMetrics.TotalDuration);
        observabilityRecorder.RecordRunPhase(runId, "RequestExecution", executionMetrics.RequestExecutionDuration);
        observabilityRecorder.RecordRunPhase(runId, "Comparison", executionMetrics.ComparisonDuration);
        observabilityRecorder.RecordRunPhase(runId, "Finalization", executionMetrics.FinalizationDuration);
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

    private static IReadOnlyList<IReadOnlyList<RequestItem>> Partition(
        IReadOnlyList<RequestItem> requests,
        int chunkSize)
    {
        if (requests.Count == 0)
        {
            return Array.Empty<IReadOnlyList<RequestItem>>();
        }

        List<IReadOnlyList<RequestItem>> chunks = new List<IReadOnlyList<RequestItem>>((int)Math.Ceiling(requests.Count / (double)chunkSize));
        for (int index = 0; index < requests.Count; index += chunkSize)
        {
            chunks.Add(requests.Skip(index).Take(Math.Min(chunkSize, requests.Count - index)).ToList());
        }

        return chunks;
    }

    private sealed class RunSummaryAccumulator
    {
        private int totalPairs;
        private int equalPairs;
        private int differentPairs;
        private int errorPairs;
        private int statusCodeMismatchPairs;
        private int bothNonSuccessPairs;

        public void Add(IEnumerable<RequestPairResult> results)
        {
            foreach (RequestPairResult result in results)
            {
                totalPairs++;
                switch (result.Outcome)
                {
                    case RequestPairOutcome.Equal:
                        equalPairs++;
                        break;
                    case RequestPairOutcome.Different:
                        differentPairs++;
                        break;
                    case RequestPairOutcome.ExecutionFailed:
                        errorPairs++;
                        break;
                    case RequestPairOutcome.StatusCodeMismatch:
                        statusCodeMismatchPairs++;
                        break;
                    case RequestPairOutcome.BothNonSuccess:
                        bothNonSuccessPairs++;
                        break;
                }
            }
        }

        public RunResultSummary ToSummary(
            RunDetailReference detailReference,
            RunExecutionMetrics executionMetrics) =>
            new RunResultSummary(
                totalPairs,
                equalPairs,
                differentPairs,
                errorPairs,
                statusCodeMismatchPairs,
                bothNonSuccessPairs,
                detailReference,
                executionMetrics);
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
