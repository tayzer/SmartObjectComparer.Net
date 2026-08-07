using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.Engine.Baselines;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Engine.Pipeline;
using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine;

public sealed partial class ComparisonRunExecutor : IComparisonRunExecutor
{
    private readonly IRequestBatchStore requestBatchStore;
    private readonly IEndpointRequestSender endpointRequestSender;
    private readonly IRunArtifactStore runArtifactStore;
    private readonly IRunDetailStore runDetailStore;
    private readonly IResponseComparer responseComparer;
    private readonly IComparisonPlanFactory? comparisonPlanFactory;
    private readonly IContractPayloadSerializer? contractPayloadSerializer;
    private readonly IObservabilityRecorder observabilityRecorder;
    private readonly IRunCleanupStage cleanupStage;
    private readonly IBaselineStore? baselineStore;

    public ComparisonRunExecutor(
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

    public ComparisonRunExecutor(
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

    public ComparisonRunExecutor(
        IRequestBatchStore requestBatchStore,
        IEndpointRequestSender endpointRequestSender,
        IRunArtifactStore runArtifactStore,
        IRunDetailStore runDetailStore,
        IResponseComparer responseComparer,
        IComparisonPlanFactory? comparisonPlanFactory,
        IObservabilityRecorder? observabilityRecorder = null,
        IRunCleanupStage? cleanupStage = null,
        IContractPayloadSerializer? contractPayloadSerializer = null,
        IBaselineStore? baselineStore = null)
    {
        this.baselineStore = baselineStore;
        this.requestBatchStore = requestBatchStore;
        this.endpointRequestSender = endpointRequestSender;
        this.runArtifactStore = runArtifactStore;
        this.runDetailStore = runDetailStore;
        this.responseComparer = responseComparer is RawTextResponseComparer
            ? responseComparer
            : new RawTextResponseComparer(runArtifactStore, responseComparer);
        this.comparisonPlanFactory = comparisonPlanFactory;
        this.contractPayloadSerializer = contractPayloadSerializer;
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
        (RunOptions comparisonOptions, ComparisonExecutionPlan? plan, IReadOnlyList<PlannedRequest> plannedRequests) =
            await PlanAsync(run, cancellationToken).ConfigureAwait(false);
        // Disposing the plan tears down the run's plugin scope, so it is held for
        // exactly as long as the run needs it and no longer.
        await using ComparisonExecutionPlan? ownedPlan = plan;
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
        RunPipelineExecution? pipelineExecution = plan is null
            ? null
            : RunPipelineExecution.Create(
                plan,
                run,
                comparisonOptions,
                endpointRequestSender,
                runArtifactStore,
                contractPayloadSerializer,
                counters);

        BaselineRunSessions baselineSessions = await OpenBaselineSessionsAsync(
            run,
            comparisonOptions,
            plan,
            cancellationToken).ConfigureAwait(false);

        try
        {
            return await RunAndFinalizeAsync(
                run,
                comparisonOptions,
                pipelineExecution,
                baselineSessions,
                plannedRequests,
                counters,
                summaryAccumulator,
                persistedRecords,
                detailWriter,
                progressReporter,
                totalRequests,
                compareSubPhaseCounters,
                totalStopwatch,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A capture that did not finish leaves no usable package behind: a partial
            // baseline would silently become someone's expected result.
            if (baselineSessions.Capture is not null)
            {
                await baselineSessions.Capture.AbandonAsync(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task<RunResultSummary> RunAndFinalizeAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RunPipelineExecution? pipelineExecution,
        BaselineRunSessions baselineSessions,
        IReadOnlyList<PlannedRequest> plannedRequests,
        RunExecutionCounters counters,
        RunSummaryAccumulator summaryAccumulator,
        List<ComparedExecutionRecord> persistedRecords,
        IRunDetailWriter detailWriter,
        IRunProgressReporter progressReporter,
        int totalRequests,
        CompareSubPhaseCounters? compareSubPhaseCounters,
        Stopwatch totalStopwatch,
        CancellationToken cancellationToken)
    {
        RunPipelineResult pipelineResult = await RunPipelineAsync(
            run,
            comparisonOptions,
            pipelineExecution,
            baselineSessions,
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
        CleanupStageResult cleanupResult = await cleanupStage.CleanupAsync(run, cleanupContext, cancellationToken).ConfigureAwait(false);
        cleanupStopwatch.Stop();
        observabilityRecorder.RecordRunPhase(run.Id, "Cleanup", cleanupStopwatch.Elapsed);

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
            cleanupResult.RetainedArtifactCount,
            cleanupResult.TrimmedByPolicyArtifactCount,
            cleanupResult.MissingUnexpectedlyArtifactCount,
            compareSubPhaseCounters?.ToMetrics(),
            pipelineResult.ComparisonConcurrency);

        // Sealed last: a package only becomes usable once the run that produced it
        // finished, so a run that failed while finalizing leaves nothing behind.
        if (baselineSessions.Capture is not null)
        {
            await baselineSessions.Capture.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

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

    private async Task<(RunOptions ComparisonOptions, ComparisonExecutionPlan? Plan, IReadOnlyList<PlannedRequest> PlannedRequests)> PlanAsync(
        ComparisonRun run,
        CancellationToken cancellationToken)
    {
        ComparisonExecutionPlan? plan = await ResolvePlanAsync(run.Options, cancellationToken).ConfigureAwait(false);
        RunOptions comparisonOptions = plan is null
            ? run.Options
            : CreateRunOptionsWithComparisonDefaults(run.Options, plan.Definition.DefaultComparisonRules);

        RequestBatchManifest manifest = await requestBatchStore
            .LoadManifestAsync(run.Options.RequestBatch, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<PlannedRequest> plannedRequests = manifest.Requests
            .Select((request, index) => new PlannedRequest(index, request))
            .ToList();

        return (comparisonOptions, plan, plannedRequests);
    }

    private Task<ComparisonExecutionPlan?> ResolvePlanAsync(RunOptions options, CancellationToken cancellationToken)
    {
        if (comparisonPlanFactory is null)
        {
            return options.PluginComparison is null
                ? Task.FromResult<ComparisonExecutionPlan?>(null)
                : throw new InvalidOperationException("A comparison plan factory is required when a run selects a plugin comparison.");
        }

        return comparisonPlanFactory.CreateAsync(options, cancellationToken);
    }

    // The comparison's own defaults are the floor, not a replacement: a run can
    // switch a rule on but never silently switch off one the comparison relies on.
    private static RunOptions CreateRunOptionsWithComparisonDefaults(
        RunOptions options,
        ComparisonRuleDefaults defaults)
    {
        ComparisonOptions current = options.Comparison;
        ComparisonOptions comparisonOptions = new ComparisonOptions(
            defaults.IgnoreCollectionOrder || current.IgnoreCollectionOrder,
            defaults.IgnoreStringCase || current.IgnoreStringCase,
            defaults.IgnoreTrailingWhitespaceAtEnd || current.IgnoreTrailingWhitespaceAtEnd,
            defaults.TreatNullAndEmptyCollectionsAsEqual || current.TreatNullAndEmptyCollectionsAsEqual,
            defaults.IgnoreXmlNamespaces || current.IgnoreXmlNamespaces,
            current.MaxDifferences,
            defaults.IgnoreRules.Concat(current.IgnoreRules),
            defaults.SmartIgnoreRules.Concat(current.SmartIgnoreRules),
            defaults.MaskRules.Concat(current.MaskRules));

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
            options.ComparisonRulesSnapshotHash,
            options.PluginComparison,
            options.Baseline);
    }

    private sealed record RunPipelineResult(
        TimeSpan ExecutionDuration,
        TimeSpan ComparisonDuration,
        TimeSpan PersistenceDuration,
        int ComparisonConcurrency);

    /// <summary>
    /// The baseline work a run has attached to it. Both are null for the ordinary
    /// live-vs-live run, which is what keeps that path unchanged.
    /// </summary>
    private sealed record BaselineRunSessions(
        BaselineCaptureSession? Capture,
        BaselineReplaySession? Replay)
    {
        public static readonly BaselineRunSessions None = new BaselineRunSessions(null, null);
    }

    private async Task<BaselineRunSessions> OpenBaselineSessionsAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        ComparisonExecutionPlan? plan,
        CancellationToken cancellationToken)
    {
        if (comparisonOptions.Baseline is not { } binding || binding.Mode == BaselineRunMode.LiveVsLive)
        {
            return BaselineRunSessions.None;
        }

        if (baselineStore is null)
        {
            throw new InvalidOperationException("A baseline store is required to capture or replay baselines.");
        }

        // The stored side is a comparison model, which only has a meaning while a
        // plugin comparison defines the type it belongs to.
        if (plan is null)
        {
            throw new InvalidOperationException("Baseline capture and replay require a plugin comparison.");
        }

        if (binding.Mode == BaselineRunMode.BaselineVsLive)
        {
            BaselineReplaySession replay = await BaselineReplaySession
                .OpenAsync(baselineStore, runArtifactStore, contractPayloadSerializer, plan, binding, cancellationToken)
                .ConfigureAwait(false);
            return new BaselineRunSessions(null, replay);
        }

        BaselineCaptureSession capture = await BaselineCaptureSession
            .BeginAsync(
                baselineStore,
                requestBatchStore,
                runArtifactStore,
                contractPayloadSerializer,
                run,
                comparisonOptions,
                plan,
                binding,
                cancellationToken)
            .ConfigureAwait(false);
        return new BaselineRunSessions(capture, null);
    }

    private async Task<RunPipelineResult> RunPipelineAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RunPipelineExecution? pipelineExecution,
        BaselineRunSessions baselineSessions,
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
            return new RunPipelineResult(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0);
        }

        int executeConcurrency = Math.Max(1, comparisonOptions.MaxConcurrency);
        int compareConcurrency = Math.Min(
            totalRequests,
            Math.Max(1, comparisonOptions.LargeRun.ComparisonConcurrency ?? Environment.ProcessorCount));
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
                        ExecutionRecord executionRecord = await ExecutePairAsync(run, comparisonOptions, plannedRequest, pipelineExecution, baselineSessions, counters, cancellationToken).ConfigureAwait(false);
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
                        ComparedExecutionRecord comparedRecord = await CompareRecordAsync(run, comparisonOptions, pipelineExecution, baselineSessions, executionRecord, counters, compareSubPhaseCounters, cancellationToken).ConfigureAwait(false);
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
            TimeSpan.FromTicks(persistenceTicks),
            compareConcurrency);
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
    private async Task<ExecutionRecord> ExecutePairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        PlannedRequest plannedRequest,
        RunPipelineExecution? pipelineExecution,
        BaselineRunSessions baselineSessions,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        // A capture run has only one side to execute: the version being recorded. The
        // second slot carries the same record so the rest of the pipeline shape holds.
        if (baselineSessions.Capture is { } captureSession)
        {
            EndpointExecutionRecord captured = await ExecuteEndpointAsync(
                run,
                comparisonOptions,
                plannedRequest.Request,
                captureSession.Binding.BaselineSlot,
                pipelineExecution,
                baselineSessions,
                counters,
                cancellationToken).ConfigureAwait(false);

            return new ExecutionRecord(plannedRequest.ManifestOrdinal, plannedRequest.Request, captured, captured);
        }

        Task<EndpointExecutionRecord> endpointATask = ExecuteEndpointAsync(run, comparisonOptions, plannedRequest.Request, EndpointSlot.A, pipelineExecution, baselineSessions, counters, cancellationToken);
        Task<EndpointExecutionRecord> endpointBTask = ExecuteEndpointAsync(run, comparisonOptions, plannedRequest.Request, EndpointSlot.B, pipelineExecution, baselineSessions, counters, cancellationToken);

        await Task.WhenAll(endpointATask, endpointBTask).ConfigureAwait(false);

        EndpointExecutionRecord endpointA = await endpointATask.ConfigureAwait(false);
        EndpointExecutionRecord endpointB = await endpointBTask.ConfigureAwait(false);

        return new ExecutionRecord(plannedRequest.ManifestOrdinal, plannedRequest.Request, endpointA, endpointB);
    }

    private async Task<ComparedExecutionRecord> CompareRecordAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RunPipelineExecution? pipelineExecution,
        BaselineRunSessions baselineSessions,
        ExecutionRecord executionRecord,
        RunExecutionCounters counters,
        CompareSubPhaseCounters? subPhaseCounters,
        CancellationToken cancellationToken)
    {
        RequestPairResult pairResult;
        if (baselineSessions.Capture is { } captureSession)
        {
            pairResult = await CompleteCapturePairAsync(
                run,
                comparisonOptions,
                pipelineExecution!,
                captureSession,
                executionRecord,
                subPhaseCounters,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            pairResult = pipelineExecution is null
                ? await CompleteRegularPairAsync(run, comparisonOptions, executionRecord, subPhaseCounters, cancellationToken).ConfigureAwait(false)
                : await CompletePipelinePairAsync(
                    run,
                    comparisonOptions,
                    pipelineExecution,
                    executionRecord,
                    subPhaseCounters,
                    cancellationToken)
                    .ConfigureAwait(false);
        }

        return new ComparedExecutionRecord(executionRecord.ManifestOrdinal, pairResult);
    }

    /// <summary>
    /// Finishes a capture-run scenario: map it, store it, and record it as a captured
    /// pair. Nothing is compared — there is only one side — so the pair is reported
    /// equal with the capture named as its outcome.
    /// </summary>
    private async Task<RequestPairResult> CompleteCapturePairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RunPipelineExecution pipelineExecution,
        BaselineCaptureSession captureSession,
        ExecutionRecord executionRecord,
        CompareSubPhaseCounters? subPhaseCounters,
        CancellationToken cancellationToken)
    {
        RequestItem request = executionRecord.Request;
        EndpointExecutionRecord endpoint = executionRecord.EndpointA;

        // A failed or non-success scenario says nothing about how the version behaves,
        // so it is reported but deliberately not written into the package.
        if (!string.IsNullOrWhiteSpace(endpoint.ErrorMessage) || !endpoint.IsSuccessStatusCode)
        {
            return await CompareRawPairAsync(
                run,
                comparisonOptions,
                request,
                endpoint.Metadata,
                endpoint.Metadata,
                endpoint.ErrorMessage,
                subPhaseCounters,
                cancellationToken).ConfigureAwait(false);
        }

        IEndpointPipelineContext context = RequirePipelineContext(endpoint);

        await TimeSubPhaseAsync(
            subPhaseCounters,
            static (c, e) => c.AddNormalize(e),
            async () =>
            {
                await pipelineExecution.Pipeline
                    .ExecuteEndpointAsync(context, PipelinePhase.Mapping, PipelinePhase.Mapping, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);

        if (context.IsFailed)
        {
            return new RequestPairResult(
                request.RelativePath,
                RequestPairOutcome.ExecutionFailed,
                context.ResponseArtifact,
                context.ResponseArtifact,
                context.FailureReason);
        }

        await captureSession.CaptureAsync(request, context, cancellationToken).ConfigureAwait(false);

        RequestPairResult pairResult = RequestPairResult.FromComparison(
            request,
            context.ResponseArtifact!,
            context.ResponseArtifact!,
            Array.Empty<ComparisonDifference>(),
            $"Captured into baseline '{captureSession.Manifest.Name}' {captureSession.Manifest.DisplayVersion}.");

        return await TimeSubPhaseAsync(
            subPhaseCounters,
            static (c, e) => c.AddFocusedContent(e),
            () => AttachFocusedRawContentAsync(pairResult, run.Id, comparisonOptions, cancellationToken)).ConfigureAwait(false);
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
        RunPipelineExecution? pipelineExecution,
        BaselineRunSessions baselineSessions,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        try
        {
            EndpointDefinition endpointDefinition = endpoint == EndpointSlot.A
                ? run.Options.EndpointA
                : run.Options.EndpointB;

            // The replayed slot is served from storage: no request is built, no token is
            // exchanged and nothing is called, which is what lets a decommissioned
            // version still take part in a comparison.
            if (baselineSessions.Replay is { } replaySession && replaySession.Binding.IsBaselineSlot(endpoint))
            {
                if (pipelineExecution is null)
                {
                    throw new InvalidOperationException("Replaying a baseline requires a plugin comparison.");
                }

                return await replaySession.ExecuteAsync(
                    run,
                    comparisonOptions,
                    request,
                    endpoint,
                    endpointDefinition,
                    pipelineExecution,
                    token => requestBatchStore.OpenRequestBodyAsync(run.Options.RequestBatch, request, token),
                    counters,
                    cancellationToken).ConfigureAwait(false);
            }

            if (pipelineExecution is not null)
            {
                return await ExecuteEndpointPipelineAsync(
                    run,
                    request,
                    endpoint,
                    endpointDefinition,
                    pipelineExecution,
                    cancellationToken).ConfigureAwait(false);
            }

            PreparedRequest preparedRequest = await PrepareRegularRequestAsync(run, request, endpoint, endpointDefinition, cancellationToken).ConfigureAwait(false);

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

    // Input through Response only: the response stream is open for the duration of
    // this call, so mapping is deliberately left to the compare pool, which reads
    // the persisted artifact instead.
    private async Task<EndpointExecutionRecord> ExecuteEndpointPipelineAsync(
        ComparisonRun run,
        RequestItem request,
        EndpointSlot endpoint,
        EndpointDefinition endpointDefinition,
        RunPipelineExecution pipelineExecution,
        CancellationToken cancellationToken)
    {
        EndpointPipelineContext context = pipelineExecution.CreateEndpointContext(
            run,
            request,
            endpoint,
            endpointDefinition,
            token => requestBatchStore.OpenRequestBodyAsync(run.Options.RequestBatch, request, token));

        await pipelineExecution.Pipeline
            .ExecuteEndpointAsync(context, PipelinePhase.Input, PipelinePhase.Response, cancellationToken)
            .ConfigureAwait(false);

        return EndpointExecutionRecord.FromPipeline(context);
    }

    private async Task<RequestPairResult> CompletePipelinePairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RunPipelineExecution pipelineExecution,
        ExecutionRecord executionRecord,
        CompareSubPhaseCounters? subPhaseCounters,
        CancellationToken cancellationToken)
    {
        RequestItem request = executionRecord.Request;
        EndpointExecutionRecord endpointA = executionRecord.EndpointA;
        EndpointExecutionRecord endpointB = executionRecord.EndpointB;

        // A transport failure or a non-success status says nothing about the
        // comparison type, so those pairs are still diffed as raw responses rather
        // than pushed through mapping that would only fail on unexpected payloads.
        string? errorMessage = BuildErrorMessage(endpointA, endpointB);
        if (!string.IsNullOrWhiteSpace(errorMessage) || !endpointA.IsSuccessStatusCode || !endpointB.IsSuccessStatusCode)
        {
            return await CompareRawPairAsync(
                run,
                comparisonOptions,
                request,
                endpointA.Metadata,
                endpointB.Metadata,
                errorMessage,
                subPhaseCounters,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            IEndpointPipelineContext contextA = RequirePipelineContext(endpointA);
            IEndpointPipelineContext contextB = RequirePipelineContext(endpointB);

            await TimeSubPhaseAsync(
                subPhaseCounters,
                static (c, e) => c.AddNormalize(e),
                async () =>
                {
                    // A replayed slot already holds the comparison model that was
                    // captured; running mapping over it again would try to map an
                    // already-mapped payload.
                    if (!endpointA.IsBaselineReplay)
                    {
                        await pipelineExecution.Pipeline
                            .ExecuteEndpointAsync(contextA, PipelinePhase.Mapping, PipelinePhase.Mapping, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (!endpointB.IsBaselineReplay)
                    {
                        await pipelineExecution.Pipeline
                            .ExecuteEndpointAsync(contextB, PipelinePhase.Mapping, PipelinePhase.Mapping, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    return true;
                }).ConfigureAwait(false);

            if (contextA.IsFailed || contextB.IsFailed)
            {
                return new RequestPairResult(
                    request.RelativePath,
                    RequestPairOutcome.ExecutionFailed,
                    contextA.ResponseArtifact,
                    contextB.ResponseArtifact,
                    contextA.FailureReason ?? contextB.FailureReason);
            }

            PairPipelineContext pairContext = pipelineExecution.CreatePairContext(
                run,
                request,
                contextA,
                contextB,
                comparisonOptions.Comparison);

            await TimeSubPhaseAsync(
                subPhaseCounters,
                static (c, e) => c.AddDiff(e),
                async () =>
                {
                    await pipelineExecution.Pipeline.ExecutePairAsync(pairContext, cancellationToken).ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);

            RequestPairResult pairResult = ToPairResult(request, pairContext);

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
            observabilityRecorder.RecordException(run.Id, "PipelineComparison", ex, request.RelativePath);
            return new RequestPairResult(
                request.RelativePath,
                RequestPairOutcome.ExecutionFailed,
                errorMessage: ex.Message);
        }
    }

    private async Task<RequestPairResult> CompareRawPairAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RequestItem request,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage,
        CompareSubPhaseCounters? subPhaseCounters,
        CancellationToken cancellationToken)
    {
        RequestPairResult pairResult = await TimeSubPhaseAsync(
            subPhaseCounters,
            static (c, e) => c.AddDiff(e),
            () => responseComparer.CompareAsync(request, comparisonOptions, responseA, responseB, errorMessage, cancellationToken)).ConfigureAwait(false);

        return await TimeSubPhaseAsync(
            subPhaseCounters,
            static (c, e) => c.AddFocusedContent(e),
            () => AttachFocusedRawContentAsync(pairResult, run.Id, comparisonOptions, cancellationToken)).ConfigureAwait(false);
    }

    private static IEndpointPipelineContext RequirePipelineContext(EndpointExecutionRecord record) =>
        record.PipelineContext
            ?? throw new InvalidOperationException($"Endpoint {record.Endpoint} did not carry a pipeline context.");

    private static RequestPairResult ToPairResult(RequestItem request, PairPipelineContext context)
    {
        PairComparisonResult result = context.Result;
        if (result.Outcome == RequestPairOutcome.ExecutionFailed)
        {
            return new RequestPairResult(
                request.RelativePath,
                RequestPairOutcome.ExecutionFailed,
                context.ResponseArtifactA,
                context.ResponseArtifactB,
                result.ErrorMessage ?? context.FailureReason);
        }

        return RequestPairResult.FromComparison(
            request,
            context.ResponseArtifactA,
            context.ResponseArtifactB,
            result.Differences,
            result.OutcomeMessage);
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


    private sealed record ArtifactRetentionCounters(
        int RetainedArtifactCount,
        int TrimmedByPolicyArtifactCount,
        int MissingUnexpectedlyArtifactCount);
    // Only instantiated when ObservabilityOptions.EnableDetailedCompareTiming is on -
    // otherwise CompareRecordAsync's TimeSubPhaseAsync helper never touches this and no
    // Stopwatch is created, so the toggle being off has no measurable cost.
}
