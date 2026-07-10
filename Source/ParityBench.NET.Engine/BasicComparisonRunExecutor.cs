using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.Engine.Pipeline;

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
    private readonly IRunCleanupStage cleanupStage;

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
        IObservabilityRecorder? observabilityRecorder = null,
        IRunCleanupStage? cleanupStage = null)
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
        this.cleanupStage = cleanupStage ?? NoOpRunCleanupStage.Instance;
    }

    public async Task<RunResultSummary> ExecuteAsync(
        ComparisonRun run,
        IRunProgressReporter progressReporter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(progressReporter);

        Stopwatch totalStopwatch = Stopwatch.StartNew();

        await progressReporter
            .ReportAsync(RunStatus.Parsing, new RunProgress(5, "Loading request batch."), cancellationToken, force: true)
            .ConfigureAwait(false);

        Stopwatch planningStopwatch = Stopwatch.StartNew();
        (RunOptions comparisonOptions, IContractProfile? contractProfile, IReadOnlyList<PlannedRequest> plannedRequests) =
            await PlanAsync(run, cancellationToken).ConfigureAwait(false);
        planningStopwatch.Stop();
        observabilityRecorder.RecordRunPhase(run.Id, "Planning", planningStopwatch.Elapsed);

        int totalRequests = plannedRequests.Count;
        await progressReporter
            .ReportAsync(RunStatus.Executing, new RunProgress(10, "Executing requests.", 0, totalRequests), cancellationToken, force: true)
            .ConfigureAwait(false);

        RunExecutionCounters counters = new RunExecutionCounters();
        RunSummaryAccumulator summaryAccumulator = new RunSummaryAccumulator();
        List<ComparedExecutionRecord> persistedRecords = new List<ComparedExecutionRecord>(totalRequests);
        CompareSubPhaseCounters? compareSubPhaseCounters = observabilityRecorder.IsDetailedCompareTimingEnabled
            ? new CompareSubPhaseCounters()
            : null;

        await using IRunDetailWriter detailWriter = await runDetailStore
            .CreateWriterAsync(run.Id, comparisonOptions.LargeRun.DetailPageSize, cancellationToken)
            .ConfigureAwait(false);

        // Two decoupled worker pools connected by a bounded channel: execute (network I/O,
        // sized to MaxConcurrency) and compare (CPU-bound diffing/normalization, sized to
        // the processor count) run continuously and independently instead of the whole
        // batch executing, then the whole batch comparing. A record can be comparing while
        // later records are still executing. Results land on disk in original request
        // order via a small reorder buffer, since compare workers finish out of order but
        // the paginated detail writer requires append order.
        RunPipelineResult pipelineResult = await RunPipelineAsync(
            run,
            comparisonOptions,
            contractProfile,
            plannedRequests,
            counters,
            summaryAccumulator,
            persistedRecords,
            detailWriter,
            progressReporter,
            totalRequests,
            compareSubPhaseCounters,
            cancellationToken).ConfigureAwait(false);

        TimeSpan executionDuration = pipelineResult.ExecutionDuration;
        TimeSpan comparisonDuration = pipelineResult.ComparisonDuration;
        TimeSpan persistenceDuration = pipelineResult.PersistenceDuration;

        observabilityRecorder.RecordRunPhase(run.Id, "Execution", executionDuration);
        observabilityRecorder.RecordRunPhase(run.Id, "Compare", comparisonDuration);

        await progressReporter
            .ReportAsync(RunStatus.Finalizing, new RunProgress(95, "Saving result details.", totalRequests, totalRequests), cancellationToken, force: true)
            .ConfigureAwait(false);

        Stopwatch finalizeStopwatch = Stopwatch.StartNew();
        RunDetailReference detailReference = await detailWriter.CompleteAsync(cancellationToken).ConfigureAwait(false);
        finalizeStopwatch.Stop();
        persistenceDuration += finalizeStopwatch.Elapsed;
        observabilityRecorder.RecordRunPhase(run.Id, "Persistence", persistenceDuration);

        await progressReporter
            .ReportAsync(RunStatus.Finalizing, new RunProgress(97, "Running cleanup stage.", totalRequests, totalRequests), cancellationToken, force: true)
            .ConfigureAwait(false);

        Stopwatch cleanupStopwatch = Stopwatch.StartNew();
        CleanupStageContext cleanupContext = new CleanupStageContext(
            comparisonOptions,
            detailReference,
            persistedRecords,
            DurableAppendCompleted: true);
        await cleanupStage.CleanupAsync(run, cleanupContext, cancellationToken).ConfigureAwait(false);
        cleanupStopwatch.Stop();
        observabilityRecorder.RecordRunPhase(run.Id, "Cleanup", cleanupStopwatch.Elapsed);

        IReadOnlyList<RequestPairResult> retentionResults = await runDetailStore
            .LoadDetailsAsync(detailReference, cancellationToken)
            .ConfigureAwait(false);
        ArtifactRetentionCounters retentionCounters = SummarizeArtifactRetention(retentionResults);

        totalStopwatch.Stop();
        observabilityRecorder.RecordRunPhase(run.Id, "Total", totalStopwatch.Elapsed);

        RunExecutionMetrics executionMetrics = new RunExecutionMetrics(
            totalStopwatch.Elapsed,
            executionDuration,
            comparisonDuration,
            persistenceDuration + cleanupStopwatch.Elapsed,
            totalRequests,
            run.Options.MaxConcurrency,
            counters.ResponseBytesWritten,
            retentionCounters.RetainedArtifactCount,
            retentionCounters.TrimmedByPolicyArtifactCount,
            retentionCounters.MissingUnexpectedlyArtifactCount,
            compareSubPhaseCounters?.ToMetrics());

        return summaryAccumulator.ToSummary(detailReference, executionMetrics);
    }

    private static ArtifactRetentionCounters SummarizeArtifactRetention(IReadOnlyList<RequestPairResult> results)
    {
        int retained = 0;
        int trimmedByPolicy = 0;
        int missingUnexpectedly = 0;

        foreach (RequestPairResult result in results)
        {
            CountIfPresent(result.ResponseA is not null, result.ArtifactRetentionState.RawResponseA, ref retained, ref trimmedByPolicy, ref missingUnexpectedly);
            CountIfPresent(result.ResponseB is not null, result.ArtifactRetentionState.RawResponseB, ref retained, ref trimmedByPolicy, ref missingUnexpectedly);

            bool hasCanonicalA = result.ResponseA?.Artifact.ArtifactId.Contains("/canonical/", StringComparison.OrdinalIgnoreCase) == true;
            bool hasCanonicalB = result.ResponseB?.Artifact.ArtifactId.Contains("/canonical/", StringComparison.OrdinalIgnoreCase) == true;
            CountIfPresent(hasCanonicalA, result.ArtifactRetentionState.CanonicalResponseA, ref retained, ref trimmedByPolicy, ref missingUnexpectedly);
            CountIfPresent(hasCanonicalB, result.ArtifactRetentionState.CanonicalResponseB, ref retained, ref trimmedByPolicy, ref missingUnexpectedly);

            CountIfPresent(result.FocusedResponseA is not null, result.ArtifactRetentionState.FocusedResponseA, ref retained, ref trimmedByPolicy, ref missingUnexpectedly);
            CountIfPresent(result.FocusedResponseB is not null, result.ArtifactRetentionState.FocusedResponseB, ref retained, ref trimmedByPolicy, ref missingUnexpectedly);
        }

        return new ArtifactRetentionCounters(retained, trimmedByPolicy, missingUnexpectedly);
    }

    private static void CountIfPresent(
        bool artifactPresent,
        ArtifactRetentionState state,
        ref int retained,
        ref int trimmedByPolicy,
        ref int missingUnexpectedly)
    {
        if (!artifactPresent)
        {
            return;
        }

        switch (state)
        {
            case ArtifactRetentionState.Retained:
                retained++;
                break;
            case ArtifactRetentionState.TrimmedByPolicy:
                trimmedByPolicy++;
                break;
            case ArtifactRetentionState.MissingUnexpectedly:
                missingUnexpectedly++;
                break;
        }
    }

    private async Task<(RunOptions ComparisonOptions, IContractProfile? ContractProfile, IReadOnlyList<PlannedRequest> PlannedRequests)> PlanAsync(
        ComparisonRun run,
        CancellationToken cancellationToken)
    {
        IContractProfile? contractProfile = ResolveContractProfile(run.Options);
        RunOptions comparisonOptions = contractProfile is null
            ? run.Options
            : CreateRunOptionsWithProfileDefaults(run.Options, contractProfile);

        RequestBatchManifest manifest = await requestBatchStore
            .LoadManifestAsync(run.Options.RequestBatch, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<PlannedRequest> plannedRequests = manifest.Requests
            .Select((request, index) => new PlannedRequest(index, request))
            .ToList();

        return (comparisonOptions, contractProfile, plannedRequests);
    }

    private sealed record RunPipelineResult(
        TimeSpan ExecutionDuration,
        TimeSpan ComparisonDuration,
        TimeSpan PersistenceDuration);

    private async Task<RunPipelineResult> RunPipelineAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        IContractProfile? contractProfile,
        IReadOnlyList<PlannedRequest> plannedRequests,
        RunExecutionCounters counters,
        RunSummaryAccumulator summaryAccumulator,
        List<ComparedExecutionRecord> persistedRecords,
        IRunDetailWriter detailWriter,
        IRunProgressReporter progressReporter,
        int totalRequests,
        CompareSubPhaseCounters? compareSubPhaseCounters,
        CancellationToken cancellationToken)
    {
        if (totalRequests == 0)
        {
            return new RunPipelineResult(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        }

        int executeConcurrency = Math.Max(1, comparisonOptions.MaxConcurrency);
        int compareConcurrency = Math.Max(1, Environment.ProcessorCount);
        int channelCapacity = Math.Max(1, comparisonOptions.LargeRun.ChunkSize);
        int flushBatchSize = Math.Max(1, comparisonOptions.LargeRun.DetailPageSize);

        Channel<ExecutionRecord> executedChannel = Channel.CreateBounded<ExecutionRecord>(
            new BoundedChannelOptions(channelCapacity)
            {
                SingleWriter = false,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

        long executionTicks = 0;
        long comparisonTicks = 0;
        long persistenceTicks = 0;
        int completedCount = 0;
        int nextRequestIndex = -1;

        OrderedResultSequencer sequencer = new OrderedResultSequencer(
            detailWriter,
            summaryAccumulator,
            persistedRecords,
            flushBatchSize,
            elapsed => Interlocked.Add(ref persistenceTicks, elapsed.Ticks));

        Task[] executeWorkers = Enumerable.Range(0, executeConcurrency)
            .Select(_ => Task.Run(
                async () =>
                {
                    int index;
                    while ((index = Interlocked.Increment(ref nextRequestIndex)) < plannedRequests.Count)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        PlannedRequest plannedRequest = plannedRequests[index];

                        Stopwatch executionStopwatch = Stopwatch.StartNew();
                        ExecutionRecord executionRecord = await ExecutePairAsync(run, comparisonOptions, plannedRequest, contractProfile, counters, cancellationToken).ConfigureAwait(false);
                        executionStopwatch.Stop();
                        Interlocked.Add(ref executionTicks, executionStopwatch.ElapsedTicks);
                        observabilityRecorder.RecordRequestPath(run.Id, plannedRequest.Request.RelativePath, executionStopwatch.Elapsed);

                        await executedChannel.Writer.WriteAsync(executionRecord, cancellationToken).ConfigureAwait(false);
                    }
                },
                cancellationToken))
            .ToArray();

        Task executeStageTask = RunWorkerStageAsync(executeWorkers, executedChannel.Writer);

        Task[] compareWorkers = Enumerable.Range(0, compareConcurrency)
            .Select(_ => Task.Run(
                async () =>
                {
                    await foreach (ExecutionRecord executionRecord in executedChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    {
                        Stopwatch compareStopwatch = Stopwatch.StartNew();
                        ComparedExecutionRecord comparedRecord = await CompareRecordAsync(run, comparisonOptions, contractProfile, executionRecord, counters, compareSubPhaseCounters, cancellationToken).ConfigureAwait(false);
                        compareStopwatch.Stop();
                        Interlocked.Add(ref comparisonTicks, compareStopwatch.ElapsedTicks);

                        int completed = Interlocked.Increment(ref completedCount);
                        await progressReporter
                            .ReportAsync(
                                RunStatus.Executing,
                                new RunProgress(
                                    CalculateExecutionPercent(completed, totalRequests),
                                    $"Processed {completed} of {totalRequests} requests.",
                                    completed,
                                    totalRequests),
                                cancellationToken)
                            .ConfigureAwait(false);

                        await sequencer.SubmitAsync(comparedRecord, cancellationToken).ConfigureAwait(false);
                    }
                },
                cancellationToken))
            .ToArray();

        Task compareStageTask = Task.WhenAll(compareWorkers);

        await Task.WhenAll(executeStageTask, compareStageTask).ConfigureAwait(false);
        await sequencer.FlushRemainingAsync(cancellationToken).ConfigureAwait(false);

        return new RunPipelineResult(
            TimeSpan.FromTicks(executionTicks),
            TimeSpan.FromTicks(comparisonTicks),
            TimeSpan.FromTicks(persistenceTicks));
    }

    private static async Task RunWorkerStageAsync(Task[] workers, ChannelWriter<ExecutionRecord> writer)
    {
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
            throw;
        }
    }

    // Compare workers finish out of request order, but the paginated detail writer
    // requires strictly increasing append order. This holds completed records until the
    // next expected ManifestOrdinal is available, then flushes contiguous runs in batches
    // so a single slow record only delays persistence of records after it, not the
    // execute/compare throughput or the visible progress counter.
    private sealed class OrderedResultSequencer
    {
        private readonly IRunDetailWriter detailWriter;
        private readonly RunSummaryAccumulator summaryAccumulator;
        private readonly List<ComparedExecutionRecord> persistedRecords;
        private readonly int flushBatchSize;
        private readonly Action<TimeSpan> recordPersistenceElapsed;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<int, ComparedExecutionRecord> pending = new Dictionary<int, ComparedExecutionRecord>();
        private readonly List<ComparedExecutionRecord> readyBuffer = new List<ComparedExecutionRecord>();
        private int nextOrdinal;

        public OrderedResultSequencer(
            IRunDetailWriter detailWriter,
            RunSummaryAccumulator summaryAccumulator,
            List<ComparedExecutionRecord> persistedRecords,
            int flushBatchSize,
            Action<TimeSpan> recordPersistenceElapsed)
        {
            this.detailWriter = detailWriter;
            this.summaryAccumulator = summaryAccumulator;
            this.persistedRecords = persistedRecords;
            this.flushBatchSize = flushBatchSize;
            this.recordPersistenceElapsed = recordPersistenceElapsed;
        }

        public async Task SubmitAsync(ComparedExecutionRecord record, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                pending[record.ManifestOrdinal] = record;
                while (pending.TryGetValue(nextOrdinal, out ComparedExecutionRecord? next))
                {
                    pending.Remove(nextOrdinal);
                    readyBuffer.Add(next);
                    nextOrdinal++;
                }

                if (readyBuffer.Count >= flushBatchSize)
                {
                    await FlushLockedAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task FlushRemainingAsync(CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await FlushLockedAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task FlushLockedAsync(CancellationToken cancellationToken)
        {
            if (readyBuffer.Count == 0)
            {
                return;
            }

            Stopwatch persistStopwatch = Stopwatch.StartNew();
            IReadOnlyList<RequestPairResult> resultsToPersist = readyBuffer
                .Select(record => record.Result)
                .ToList();
            summaryAccumulator.Add(resultsToPersist);
            persistedRecords.AddRange(readyBuffer);
            await detailWriter.AppendAsync(resultsToPersist, cancellationToken).ConfigureAwait(false);
            persistStopwatch.Stop();
            recordPersistenceElapsed(persistStopwatch.Elapsed);

            readyBuffer.Clear();
        }
    }

    private async Task<ExecutionRecord> ExecutePairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        PlannedRequest plannedRequest,
        IContractProfile? contractProfile,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        Task<EndpointExecutionRecord> endpointATask = ExecuteEndpointAsync(run, comparisonOptions, plannedRequest.Request, EndpointSlot.A, contractProfile, counters, cancellationToken);
        Task<EndpointExecutionRecord> endpointBTask = ExecuteEndpointAsync(run, comparisonOptions, plannedRequest.Request, EndpointSlot.B, contractProfile, counters, cancellationToken);

        await Task.WhenAll(endpointATask, endpointBTask).ConfigureAwait(false);

        EndpointExecutionRecord endpointA = await endpointATask.ConfigureAwait(false);
        EndpointExecutionRecord endpointB = await endpointBTask.ConfigureAwait(false);

        return new ExecutionRecord(plannedRequest.ManifestOrdinal, plannedRequest.Request, endpointA, endpointB);
    }

    private async Task<ComparedExecutionRecord> CompareRecordAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        IContractProfile? contractProfile,
        ExecutionRecord executionRecord,
        RunExecutionCounters counters,
        CompareSubPhaseCounters? subPhaseCounters,
        CancellationToken cancellationToken)
    {
        RequestPairResult pairResult = contractProfile is null
            ? await CompleteRegularPairAsync(run, comparisonOptions, executionRecord, subPhaseCounters, cancellationToken).ConfigureAwait(false)
            : await CompleteContractProfilePairAsync(
                run,
                comparisonOptions,
                executionRecord.Request,
                contractProfile,
                executionRecord.EndpointA,
                executionRecord.EndpointB,
                counters,
                subPhaseCounters,
                cancellationToken)
                .ConfigureAwait(false);

        return new ComparedExecutionRecord(executionRecord.ManifestOrdinal, pairResult);
    }

    // No-op pass-through when subPhaseCounters is null, so the toggle being off costs
    // nothing beyond this null check - no Stopwatch is ever created.
    private static async Task<T> TimeSubPhaseAsync<T>(
        CompareSubPhaseCounters? subPhaseCounters,
        Action<CompareSubPhaseCounters, TimeSpan> record,
        Func<Task<T>> action)
    {
        if (subPhaseCounters is null)
        {
            return await action().ConfigureAwait(false);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        T result = await action().ConfigureAwait(false);
        stopwatch.Stop();
        record(subPhaseCounters, stopwatch.Elapsed);
        return result;
    }

    private async Task<RequestPairResult> CompleteRegularPairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        ExecutionRecord executionRecord,
        CompareSubPhaseCounters? subPhaseCounters,
        CancellationToken cancellationToken)
    {
        string? errorMessage = BuildErrorMessage(executionRecord.EndpointA, executionRecord.EndpointB);
        RequestPairResult pairResult = await TimeSubPhaseAsync(
            subPhaseCounters,
            static (c, e) => c.AddDiff(e),
            () => responseComparer.CompareAsync(
                executionRecord.Request,
                comparisonOptions,
                executionRecord.EndpointA.Metadata,
                executionRecord.EndpointB.Metadata,
                errorMessage,
                cancellationToken)).ConfigureAwait(false);

        return await TimeSubPhaseAsync(
            subPhaseCounters,
            static (c, e) => c.AddFocusedContent(e),
            () => AttachFocusedRawContentAsync(pairResult, run.Id, comparisonOptions, cancellationToken)).ConfigureAwait(false);
    }

    private async Task<EndpointExecutionRecord> ExecuteEndpointAsync(
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

                using CancellationTokenSource bodyReadTimeoutSource = CancellationTokenSource
                    .CreateLinkedTokenSource(cancellationToken, response.Timeout);

                ResponseArtifactMetadata metadata = await PersistResponseAsync(
                    run.Id,
                    endpoint,
                    request,
                    response.StatusCode,
                    response.ContentType,
                    response.Body,
                    comparisonOptions.Comparison.MaskRules,
                    counters,
                    bodyReadTimeoutSource.Token)
                    .ConfigureAwait(false);

                return EndpointExecutionRecord.Persisted(endpoint, metadata);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            observabilityRecorder.RecordException(run.Id, "EndpointExecution", ex, request.RelativePath, endpoint);
            return EndpointExecutionRecord.Failure(endpoint, ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            observabilityRecorder.RecordException(run.Id, "EndpointExecution", ex, request.RelativePath, endpoint);
            return EndpointExecutionRecord.Failure(endpoint, ex.Message);
        }
    }

    private async Task<RequestPairResult> CompleteContractProfilePairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RequestItem request,
        IContractProfile profile,
        EndpointExecutionRecord endpointA,
        EndpointExecutionRecord endpointB,
        RunExecutionCounters counters,
        CompareSubPhaseCounters? subPhaseCounters,
        CancellationToken cancellationToken)
    {
        string? errorMessage = BuildErrorMessage(endpointA, endpointB);
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            RequestPairResult pairResult = await TimeSubPhaseAsync(
                subPhaseCounters,
                static (c, e) => c.AddDiff(e),
                () => responseComparer.CompareAsync(request, comparisonOptions, endpointA.Metadata, endpointB.Metadata, errorMessage, cancellationToken)).ConfigureAwait(false);

            return await TimeSubPhaseAsync(
                subPhaseCounters,
                static (c, e) => c.AddFocusedContent(e),
                () => AttachFocusedRawContentAsync(pairResult, run.Id, comparisonOptions, cancellationToken)).ConfigureAwait(false);
        }

        if (!endpointA.IsSuccessStatusCode || !endpointB.IsSuccessStatusCode)
        {
            RequestPairResult pairResult = await TimeSubPhaseAsync(
                subPhaseCounters,
                static (c, e) => c.AddDiff(e),
                () => responseComparer.CompareAsync(request, comparisonOptions, endpointA.Metadata, endpointB.Metadata, null, cancellationToken)).ConfigureAwait(false);

            return await TimeSubPhaseAsync(
                subPhaseCounters,
                static (c, e) => c.AddFocusedContent(e),
                () => AttachFocusedRawContentAsync(pairResult, run.Id, comparisonOptions, cancellationToken)).ConfigureAwait(false);
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
                subPhaseCounters,
                cancellationToken)
                .ConfigureAwait(false);
            ResponseArtifactMetadata canonicalB = await NormalizeAndPersistResponseAsync(
                run,
                request,
                profile,
                endpointB,
                comparisonOptions.Comparison.MaskRules,
                counters,
                subPhaseCounters,
                cancellationToken)
                .ConfigureAwait(false);

            RequestPairResult pairResult = await TimeSubPhaseAsync(
                subPhaseCounters,
                static (c, e) => c.AddDiff(e),
                () => responseComparer.CompareAsync(request, comparisonOptions, canonicalA, canonicalB, null, cancellationToken)).ConfigureAwait(false);

            return await TimeSubPhaseAsync(
                subPhaseCounters,
                static (c, e) => c.AddFocusedContent(e),
                () => AttachFocusedRawContentAsync(pairResult, run.Id, comparisonOptions, cancellationToken)).ConfigureAwait(false);
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
        EndpointExecutionRecord endpointResult,
        IReadOnlyList<MaskRuleDefinition> maskRules,
        RunExecutionCounters counters,
        CompareSubPhaseCounters? subPhaseCounters,
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

        NormalizedContractResponse normalized = await TimeSubPhaseAsync(
            subPhaseCounters,
            static (c, e) => c.AddNormalize(e),
            () => profile.NormalizeResponseAsync(endpointResult.Endpoint, context, cancellationToken).AsTask()).ConfigureAwait(false);

        await using ContractPayload normalizedPayload = normalized.Body;
        await using Stream normalizedStream = await normalizedPayload.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        RequestItem canonicalArtifactRequest = CreateCanonicalArtifactRequest(request, endpointResult.Endpoint, normalizedPayload.ContentType);
        return await TimeSubPhaseAsync(
            subPhaseCounters,
            static (c, e) => c.AddPersistCanonical(e),
            () => PersistResponseAsync(
                run.Id,
                endpointResult.Endpoint,
                canonicalArtifactRequest,
                endpointResult.StatusCode ?? 0,
                normalizedPayload.ContentType,
                normalizedStream,
                maskRules,
                counters,
                cancellationToken)).ConfigureAwait(false);
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
                options.LargeRun,
                options.RunRetentionModeOverride,
                options.ComparisonRulesSnapshotHash);
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
        EndpointExecutionRecord endpointA,
        EndpointExecutionRecord endpointB)
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

    private sealed record ArtifactRetentionCounters(
        int RetainedArtifactCount,
        int TrimmedByPolicyArtifactCount,
        int MissingUnexpectedlyArtifactCount);

    private sealed class RunExecutionCounters
    {
        private long responseBytesWritten;

        public long ResponseBytesWritten => Interlocked.Read(ref responseBytesWritten);

        public void AddResponseBytes(long bytesWritten) =>
            Interlocked.Add(ref responseBytesWritten, bytesWritten);
    }

    // Only instantiated when ObservabilityOptions.EnableDetailedCompareTiming is on -
    // otherwise CompareRecordAsync's TimeSubPhaseAsync helper never touches this and no
    // Stopwatch is created, so the toggle being off has no measurable cost.
    private sealed class CompareSubPhaseCounters
    {
        private long normalizeTicks;
        private long persistCanonicalTicks;
        private long diffTicks;
        private long focusedContentTicks;

        public void AddNormalize(TimeSpan elapsed) => Interlocked.Add(ref normalizeTicks, elapsed.Ticks);

        public void AddPersistCanonical(TimeSpan elapsed) => Interlocked.Add(ref persistCanonicalTicks, elapsed.Ticks);

        public void AddDiff(TimeSpan elapsed) => Interlocked.Add(ref diffTicks, elapsed.Ticks);

        public void AddFocusedContent(TimeSpan elapsed) => Interlocked.Add(ref focusedContentTicks, elapsed.Ticks);

        public CompareSubPhaseMetrics ToMetrics() => new CompareSubPhaseMetrics(
            TimeSpan.FromTicks(Interlocked.Read(ref normalizeTicks)),
            TimeSpan.FromTicks(Interlocked.Read(ref persistCanonicalTicks)),
            TimeSpan.FromTicks(Interlocked.Read(ref diffTicks)),
            TimeSpan.FromTicks(Interlocked.Read(ref focusedContentTicks)));
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

    private sealed class NoOpRunCleanupStage : IRunCleanupStage
    {
        public static NoOpRunCleanupStage Instance { get; } = new NoOpRunCleanupStage();

        private NoOpRunCleanupStage()
        {
        }

        public Task CleanupAsync(
            ComparisonRun run,
            CleanupStageContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(run);
            ArgumentNullException.ThrowIfNull(context);
            if (!context.DurableAppendCompleted)
            {
                throw new InvalidOperationException("Cleanup requires durable append completion.");
            }

            // PR2 only wires append-before-delete flow. Retention policy actions are implemented in PR3.
            return Task.CompletedTask;
        }
    }

}
