using System.Security.Cryptography;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Engine.Pipeline;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class ComparisonRunExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_WhenResponsesMatch_CompletesWithEqualSummary()
    {
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            FakeEndpointRequestSender.ForBody("same"));

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.TotalPairs);
        Assert.AreEqual(1, summary.EqualPairs);
        Assert.AreEqual(0, summary.DifferentPairs);
    }
    [TestMethod]
    public async Task ExecuteAsync_WhenRunCompletes_IncludesExecutionMetricsInSummary()
    {
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            FakeEndpointRequestSender.ForBody("same"));

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(maxConcurrency: 3), new CapturingProgressReporter());

        Assert.IsNotNull(summary.ExecutionMetrics);
        Assert.AreEqual(1, summary.ExecutionMetrics.RequestCount);
        Assert.AreEqual(3, summary.ExecutionMetrics.MaxConcurrency);
        Assert.AreEqual(8, summary.ExecutionMetrics.ResponseBytesWritten);
        Assert.IsTrue(summary.ExecutionMetrics.RetainedArtifactCount >= 2);
        Assert.AreEqual(0, summary.ExecutionMetrics.TrimmedByPolicyArtifactCount);
        Assert.AreEqual(0, summary.ExecutionMetrics.MissingUnexpectedlyArtifactCount);
        Assert.IsTrue(summary.ExecutionMetrics.TotalDuration >= TimeSpan.Zero);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenDetailedTimingIsEnabled_IncludesDetailedAndProcessMetrics()
    {
        CapturingObservabilityRecorder recorder = new(TimeSpan.Zero) { IsDetailedCompareTimingEnabled = true };
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            FakeEndpointRequestSender.ForBody("same"),
            observabilityRecorder: recorder);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.IsNotNull(summary.ExecutionMetrics?.DetailedCompareMetrics);
        Assert.IsNotNull(summary.ExecutionMetrics?.ProcessResourceMetrics);
        Assert.IsTrue(summary.ExecutionMetrics!.DetailedCompareMetrics!.ExecutionWorkerBackpressureDuration >= TimeSpan.Zero);
        Assert.IsTrue(summary.ExecutionMetrics.ProcessResourceMetrics!.LogicalProcessorCount > 0);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenComparisonConcurrencyIsConfigured_UsesConfiguredWorkerCount()
    {
        RequestItem[] requests = Enumerable.Range(1, 5)
            .Select(index => new RequestItem($"request-{index}.json", "application/json", 2))
            .ToArray();
        ComparisonRunExecutor executor = CreateExecutor(CreateBatch(requests), FakeEndpointRequestSender.ForBody("same"));

        RunResultSummary summary = await executor.ExecuteAsync(
            CreateRun(largeRunOptions: new LargeRunOptions(comparisonConcurrency: 2)),
            new CapturingProgressReporter());

        Assert.AreEqual(2, summary.ExecutionMetrics!.ComparisonConcurrency);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenComparisonConcurrencyExceedsRequestCount_ClampsWorkerCount()
    {
        RequestItem[] requests = Enumerable.Range(1, 3)
            .Select(index => new RequestItem($"request-{index}.json", "application/json", 2))
            .ToArray();
        ComparisonRunExecutor executor = CreateExecutor(CreateBatch(requests), FakeEndpointRequestSender.ForBody("same"));

        RunResultSummary summary = await executor.ExecuteAsync(
            CreateRun(largeRunOptions: new LargeRunOptions(comparisonConcurrency: 20)),
            new CapturingProgressReporter());

        Assert.AreEqual(3, summary.ExecutionMetrics!.ComparisonConcurrency);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenComparisonConcurrencyIsNull_UsesAtMostTwentyWorkers()
    {
        RequestItem[] requests = Enumerable.Range(1, 20)
            .Select(index => new RequestItem($"request-{index}.json", "application/json", 2))
            .ToArray();
        ComparisonRunExecutor executor = CreateExecutor(CreateBatch(requests), FakeEndpointRequestSender.ForBody("same"));

        RunResultSummary summary = await executor.ExecuteAsync(
            CreateRun(largeRunOptions: new LargeRunOptions(comparisonConcurrency: null)),
            new CapturingProgressReporter());

        Assert.AreEqual(Math.Min(20, Environment.ProcessorCount), summary.ExecutionMetrics!.ComparisonConcurrency);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCompareChannelFills_RecordsQueueWaitAndExecutionBackpressureWithoutReordering()
    {
        RequestItem[] requests = Enumerable.Range(1, 20)
            .Select(index => new RequestItem($"request-{index:D2}.json", "application/json", 2))
            .ToArray();
        FakeRunDetailStore detailStore = new();
        CapturingObservabilityRecorder recorder = new(TimeSpan.MaxValue) { IsDetailedCompareTimingEnabled = true };
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(requests),
            FakeEndpointRequestSender.ForBody("same"),
            responseComparer: new DelayedResponseComparer(TimeSpan.FromMilliseconds(10)),
            detailStore: detailStore,
            observabilityRecorder: recorder);

        RunResultSummary summary = await executor.ExecuteAsync(
            CreateRun(
                maxConcurrency: 20,
                largeRunOptions: new LargeRunOptions(chunkSize: 10_000, comparisonConcurrency: 1)),
            new CapturingProgressReporter());

        DetailedCompareMetrics metrics = summary.ExecutionMetrics!.DetailedCompareMetrics!;
        Assert.IsTrue(metrics.CompareQueueWaitDuration > TimeSpan.Zero);
        Assert.IsTrue(metrics.ExecutionWorkerBackpressureDuration > TimeSpan.Zero);
        CollectionAssert.AreEqual(requests.Select(request => request.RelativePath).ToArray(), detailStore.SavedResults.Select(result => result.RelativePath).ToArray());
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenRequestPathExceedsThreshold_RecordsSlowPath()
    {
        RequestItem request = new RequestItem("one.json", "application/json", 2);
        CapturingObservabilityRecorder recorder = new CapturingObservabilityRecorder(TimeSpan.Zero);
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { request }),
            FakeEndpointRequestSender.ForBody("same"),
            observabilityRecorder: recorder);

        await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, recorder.SlowPaths.Count);
        Assert.AreEqual("one.json", recorder.SlowPaths[0].RelativePath);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenEndpointThrows_RecordsExceptionDiagnostic()
    {
        CapturingObservabilityRecorder recorder = new CapturingObservabilityRecorder(TimeSpan.Zero);
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(request =>
        {
            if (request.Endpoint == EndpointSlot.B)
            {
                throw new InvalidOperationException("Endpoint B failed.");
            }

            return new EndpointResponse(200, "application/json", CreateStream("same"));
        });
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            sender,
            observabilityRecorder: recorder);

        await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, recorder.Exceptions.Count);
        Assert.AreEqual("EndpointExecution", recorder.Exceptions[0].Stage);
        Assert.AreEqual("one.json", recorder.Exceptions[0].RelativePath);
        Assert.AreEqual(EndpointSlot.B, recorder.Exceptions[0].Endpoint);
    }
    [TestMethod]
    public async Task ExecuteAsync_WhenLargeResponsesHaveNoMasks_StreamsResponsesToArtifacts()
    {
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(_ =>
            new EndpointResponse(200, "application/octet-stream", new TrackingReadStream(1024 * 1024)));
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("large.bin", "application/octet-stream", 1024 * 1024) }),
            sender,
            artifactStore);

        await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(2, artifactStore.SavedStreamTypes.Count);
        Assert.IsTrue(artifactStore.SavedStreamTypes.All(type => type == typeof(TrackingReadStream)));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenResponsesDiffer_CompletesWithDifferentSummary()
    {
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(request =>
            request.Endpoint == EndpointSlot.A
                ? new EndpointResponse(200, "application/json", CreateStream("a"))
                : new EndpointResponse(200, "application/json", CreateStream("b")));
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            sender);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.DifferentPairs);
        Assert.AreEqual(0, summary.EqualPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenStatusCodesMismatch_CompletesWithStatusMismatchSummary()
    {
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(request =>
            request.Endpoint == EndpointSlot.A
                ? new EndpointResponse(200, "application/json", CreateStream("same"))
                : new EndpointResponse(500, "application/json", CreateStream("same")));
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            sender);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.StatusCodeMismatchPairs);
        Assert.AreEqual(0, summary.ErrorPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenEndpointSenderThrows_CompletesWithErrorSummary()
    {
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(request =>
        {
            if (request.Endpoint == EndpointSlot.B)
            {
                throw new InvalidOperationException("Endpoint B failed.");
            }

            return new EndpointResponse(200, "application/json", CreateStream("same"));
        });
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            sender);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.ErrorPairs);
        Assert.AreEqual(0, summary.EqualPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenMultipleRequestsExist_ReportsProgressAndHonorsConcurrencyLimit()
    {
        RequestItem[] requests = Enumerable
            .Range(1, 5)
            .Select(index => new RequestItem($"request-{index}.json", "application/json", 2))
            .ToArray();
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("same", TimeSpan.FromMilliseconds(25));
        CapturingProgressReporter progressReporter = new CapturingProgressReporter();
        ComparisonRunExecutor executor = CreateExecutor(CreateBatch(requests), sender);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(maxConcurrency: 2), progressReporter);

        Assert.AreEqual(5, summary.TotalPairs);
        Assert.IsTrue(sender.MaxActiveRequestPaths <= 2);
        Assert.IsTrue(progressReporter.Events.Any(progress =>
            progress.Status == RunStatus.Executing
            && progress.Progress.CompletedItems == 5
            && progress.Progress.TotalItems == 5));
    }


    [TestMethod]
    public async Task ExecuteAsync_WhenFiveHundredRequestsExist_CompletesAndHonorsConcurrencyLimit()
    {
        RequestItem[] requests = Enumerable
            .Range(1, 500)
            .Select(index => new RequestItem($"request-{index:000}.json", "application/json", 2))
            .ToArray();
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("same", TimeSpan.FromMilliseconds(1));
        CapturingProgressReporter progressReporter = new CapturingProgressReporter();
        ComparisonRunExecutor executor = CreateExecutor(CreateBatch(requests), sender);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(maxConcurrency: 32), progressReporter);

        Assert.AreEqual(500, summary.TotalPairs);
        Assert.AreEqual(500, summary.EqualPairs);
        Assert.IsTrue(sender.MaxActiveRequestPaths <= 32);
        Assert.IsTrue(progressReporter.Events.Any(progress =>
            progress.Status == RunStatus.Executing
            && progress.Progress.CompletedItems == 500
            && progress.Progress.TotalItems == 500));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenRunUsesHeaders_MergesEndpointAndRequestHeaders()
    {
        RequestItem request = new RequestItem(
            "one.json",
            "application/json",
            2,
            new Dictionary<string, string> { ["X-Common"] = "request", ["X-Override"] = "request" },
            new Dictionary<string, string> { ["X-A"] = "request-a" });
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("same");
        ComparisonRunExecutor executor = CreateExecutor(CreateBatch(new[] { request }), sender);
        ComparisonRun run = CreateRun(
            endpointAHeaders: new Dictionary<string, string> { ["X-Endpoint"] = "a", ["X-Override"] = "endpoint" });

        await executor.ExecuteAsync(run, new CapturingProgressReporter());

        EndpointRequest endpointARequest = sender.SentRequests.Single(sentRequest => sentRequest.Endpoint == EndpointSlot.A);
        Assert.AreEqual("a", endpointARequest.Headers["X-Endpoint"]);
        Assert.AreEqual("request", endpointARequest.Headers["X-Common"]);
        Assert.AreEqual("request", endpointARequest.Headers["X-Override"]);
        Assert.AreEqual("request-a", endpointARequest.Headers["X-A"]);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenContentTypeOverrideIsConfigured_SendsOverride()
    {
        RequestItem request = new RequestItem("one.txt", "text/plain", 2);
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("same");
        ComparisonRunExecutor executor = CreateExecutor(CreateBatch(new[] { request }), sender);
        ComparisonRun run = CreateRun(
            requestExecutionOptions: new RequestExecutionOptions("application/json"));

        await executor.ExecuteAsync(run, new CapturingProgressReporter());

        Assert.IsTrue(sender.SentRequests.All(sentRequest => sentRequest.ContentType == "application/json"));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenMaskRulesExist_PersistsMaskedArtifacts()
    {
        RequestItem request = new RequestItem("one.json", "application/json", 2);
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("{\"token\":\"secret-1234\"}");
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        ComparisonRunExecutor executor = CreateExecutor(CreateBatch(new[] { request }), sender, artifactStore);
        ComparisonRun run = CreateRun(
            comparisonOptions: new ComparisonOptions(
                maskRules: new[] { new MaskRuleDefinition("token", preserveLastCharacters: 4) }));

        await executor.ExecuteAsync(run, new CapturingProgressReporter());

        Assert.AreEqual(2, artifactStore.SavedBodies.Count);
        Assert.IsTrue(artifactStore.SavedBodies.Values.All(body => body.Contains("*******1234", StringComparison.Ordinal)));
        Assert.IsTrue(artifactStore.SavedBodies.Values.All(body => !body.Contains("secret-1234", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenIgnoreCompleteRulePrunesJson_AttachesFocusedRawContent()
    {
        RequestItem request = new RequestItem("one.json", "application/json", 2);
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        FakeRunDetailStore detailStore = new FakeRunDetailStore();
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(endpointRequest =>
            endpointRequest.Endpoint == EndpointSlot.A
                ? new EndpointResponse(200, "application/json", CreateStream(@"{""name"":""Alice"",""token"":""secret""}"))
                : new EndpointResponse(200, "application/json", CreateStream(@"{""name"":""Alicia"",""token"":""other""}")));
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { request }),
            sender,
            artifactStore,
            detailStore: detailStore);
        ComparisonRun run = CreateRun(
            comparisonOptions: new ComparisonOptions(
                ignoreRules: new[] { new IgnoreRuleDefinition("token") }));

        await executor.ExecuteAsync(run, new CapturingProgressReporter());

        RequestPairResult result = detailStore.SavedResults.Single();
        Assert.IsTrue(result.HasFocusedRawContent);
        CollectionAssert.Contains(result.FocusedRawContentIgnorePaths.ToList(), "token");
        string focusedA = artifactStore.SavedBodies[result.FocusedResponseA!.Artifact.ArtifactId];
        string focusedB = artifactStore.SavedBodies[result.FocusedResponseB!.Artifact.ArtifactId];
        Assert.IsTrue(focusedA.Contains("Alice", StringComparison.Ordinal));
        Assert.IsTrue(focusedB.Contains("Alicia", StringComparison.Ordinal));
        Assert.IsFalse(focusedA.Contains("secret", StringComparison.Ordinal));
        Assert.IsFalse(focusedB.Contains("other", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenIgnoreRulePrunesNothing_DoesNotWriteFocusedArtifacts()
    {
        RequestItem request = new("one.json", "application/json", 2);
        FakeRunArtifactStore artifactStore = new();
        FakeRunDetailStore detailStore = new();
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch([request]),
            FakeEndpointRequestSender.ForBody(@"{""name"":""Alice""}"),
            artifactStore,
            detailStore: detailStore);

        await executor.ExecuteAsync(
            CreateRun(comparisonOptions: new ComparisonOptions(ignoreRules: [new IgnoreRuleDefinition("missing")])),
            new CapturingProgressReporter());

        Assert.IsFalse(detailStore.SavedResults.Single().HasFocusedRawContent);
        Assert.AreEqual(2, artifactStore.SavedBodies.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenSmartPropertyIgnorePrunesJson_AttachesFocusedRawContent()
    {
        RequestItem request = new RequestItem("one.json", "application/json", 2);
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        FakeRunDetailStore detailStore = new FakeRunDetailStore();
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(endpointRequest =>
            endpointRequest.Endpoint == EndpointSlot.A
                ? new EndpointResponse(200, "application/json", CreateStream(@"{""name"":""Alice"",""ReportId"":""A-1""}"))
                : new EndpointResponse(200, "application/json", CreateStream(@"{""name"":""Alicia"",""ReportId"":""B-1""}")));
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { request }),
            sender,
            artifactStore,
            detailStore: detailStore);
        ComparisonRun run = CreateRun(
            comparisonOptions: new ComparisonOptions(
                smartIgnoreRules: new[] { new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "ReportId") }));

        await executor.ExecuteAsync(run, new CapturingProgressReporter());

        RequestPairResult result = detailStore.SavedResults.Single();
        Assert.IsTrue(result.HasFocusedRawContent);
        string focusedA = artifactStore.SavedBodies[result.FocusedResponseA!.Artifact.ArtifactId];
        string focusedB = artifactStore.SavedBodies[result.FocusedResponseB!.Artifact.ArtifactId];
        Assert.IsFalse(focusedA.Contains("ReportId", StringComparison.Ordinal));
        Assert.IsFalse(focusedB.Contains("ReportId", StringComparison.Ordinal));
        Assert.IsTrue(result.FocusedRawContentIgnorePaths.Any(path => string.Equals(path, "ReportId", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenSmartNamePatternIgnorePrunesJson_AttachesFocusedRawContent()
    {
        RequestItem request = new RequestItem("one.json", "application/json", 2);
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        FakeRunDetailStore detailStore = new FakeRunDetailStore();
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(endpointRequest =>
            endpointRequest.Endpoint == EndpointSlot.A
                ? new EndpointResponse(200, "application/json", CreateStream(@"{""name"":""Alice"",""meta"":{""ProviderTraceId"":""A-1""}}"))
                : new EndpointResponse(200, "application/json", CreateStream(@"{""name"":""Alicia"",""meta"":{""ProviderTraceId"":""B-1""}}")));
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { request }),
            sender,
            artifactStore,
            detailStore: detailStore);
        ComparisonRun run = CreateRun(
            comparisonOptions: new ComparisonOptions(
                smartIgnoreRules: new[] { new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.NamePattern, @".*ProviderTraceId$") }));

        await executor.ExecuteAsync(run, new CapturingProgressReporter());

        RequestPairResult result = detailStore.SavedResults.Single();
        Assert.IsTrue(result.HasFocusedRawContent);
        string focusedA = artifactStore.SavedBodies[result.FocusedResponseA!.Artifact.ArtifactId];
        string focusedB = artifactStore.SavedBodies[result.FocusedResponseB!.Artifact.ArtifactId];
        Assert.IsFalse(focusedA.Contains("ProviderTraceId", StringComparison.Ordinal));
        Assert.IsFalse(focusedB.Contains("ProviderTraceId", StringComparison.Ordinal));
        CollectionAssert.Contains(result.FocusedRawContentIgnorePaths.ToList(), @".*ProviderTraceId$");
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCancellationTokenIsCancelled_StopsWithoutSavingFinalDetails()
    {
        RequestItem request = new RequestItem("one.json", "application/json", 2);
        FakeRunDetailStore detailStore = new FakeRunDetailStore();
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { request }),
            FakeEndpointRequestSender.ForBody("same"),
            detailStore: detailStore);
        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await AssertThrowsAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter(), cancellationTokenSource.Token));

        Assert.AreEqual(0, detailStore.SaveCount);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenEndpointThrows_ReturnsReadableExecutionFailedPair()
    {
        FakeRunDetailStore detailStore = new FakeRunDetailStore();
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(request =>
        {
            if (request.Endpoint == EndpointSlot.B)
            {
                throw new InvalidOperationException("Endpoint B failed.");
            }

            return new EndpointResponse(200, "application/json", CreateStream("same"));
        });
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            sender,
            detailStore: detailStore);

        await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        RequestPairResult result = detailStore.SavedResults.Single();
        Assert.AreEqual(RequestPairOutcome.ExecutionFailed, result.Outcome);
        Assert.IsTrue(result.ErrorMessage?.Contains("Endpoint B failed", StringComparison.Ordinal) == true);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenRunCompletes_RecordsStagesInOrder()
    {
        CapturingObservabilityRecorder recorder = new CapturingObservabilityRecorder(TimeSpan.Zero);
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            FakeEndpointRequestSender.ForBody("same"),
            observabilityRecorder: recorder);

        await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        CollectionAssert.AreEqual(
            new[] { "Planning", "Execution", "Compare", "Persistence", "Cleanup", "Total" },
            recorder.RunPhases.Select(phase => phase.PhaseName).ToArray());
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCleanupRuns_InvokesCleanupOnlyAfterDurableAppendCompletion()
    {
        FakeRunDetailStore detailStore = new FakeRunDetailStore();
        bool appendWasDurableWhenCleanupStarted = false;
        IRunCleanupStage cleanupStage = new DelegateCleanupStage((_, context, _) =>
        {
            appendWasDurableWhenCleanupStarted = context.DurableAppendCompleted && detailStore.SaveCount == 1;
            return Task.CompletedTask;
        });
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            FakeEndpointRequestSender.ForBody("same"),
            detailStore: detailStore,
            cleanupStage: cleanupStage);

        await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.IsTrue(appendWasDurableWhenCleanupStarted);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenParallelCompletionDiffers_AppendsDetailsByManifestOrdinal()
    {
        RequestItem[] requests =
        {
            new RequestItem("request-b.json", "application/json", 2),
            new RequestItem("request-a.json", "application/json", 2),
            new RequestItem("request-c.json", "application/json", 2),
        };
        FakeRunDetailStore detailStore = new FakeRunDetailStore();
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(
            _ => new EndpointResponse(200, "application/json", CreateStream("same")),
            delaySelector: request => request.Request.RelativePath switch
            {
                "request-b.json" => TimeSpan.FromMilliseconds(40),
                "request-a.json" => TimeSpan.FromMilliseconds(2),
                _ => TimeSpan.FromMilliseconds(8),
            });
        ComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(requests),
            sender,
            detailStore: detailStore);

        await executor.ExecuteAsync(CreateRun(maxConcurrency: 3), new CapturingProgressReporter());

        CollectionAssert.AreEqual(
            new[] { "request-b.json", "request-a.json", "request-c.json" },
            detailStore.SavedResults.Select(result => result.RelativePath).ToArray());
    }

    private static ComparisonRunExecutor CreateExecutor(
        RequestBatchManifest manifest,
        FakeEndpointRequestSender sender,
        FakeRunArtifactStore? artifactStore = null,
        IResponseComparer? responseComparer = null,
        FakeRunDetailStore? detailStore = null,
        IObservabilityRecorder? observabilityRecorder = null,
        IRunCleanupStage? cleanupStage = null)
    {
        FakeRequestBatchStore requestBatchStore = new FakeRequestBatchStore(manifest);
        IRunArtifactStore resolvedArtifactStore = artifactStore ?? new FakeRunArtifactStore();
        return new ComparisonRunExecutor(
            requestBatchStore,
            sender,
            resolvedArtifactStore,
            detailStore ?? new FakeRunDetailStore(),
            responseComparer ?? new RawTextResponseComparer(resolvedArtifactStore, new HashOnlyResponseComparer()),
            null,
                observabilityRecorder,
                cleanupStage);
    }
    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but got {ex.GetType().Name}.");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
    private static RequestBatchManifest CreateBatch(IReadOnlyList<RequestItem> requests) =>
        new RequestBatchManifest(new RequestBatchReference("batch-1"), requests);

    private static ComparisonRun CreateRun(
        int maxConcurrency = 4,
        IReadOnlyDictionary<string, string>? endpointAHeaders = null,
        ComparisonOptions? comparisonOptions = null,
        RequestExecutionOptions? requestExecutionOptions = null,
        LargeRunOptions? largeRunOptions = null) =>
        ComparisonRun
            .Create(
                new RunId("run-1"),
                new RunOptions(
                    new RequestBatchReference("batch-1"),
                    new EndpointDefinition(new Uri("https://service-a.example.test"), headers: endpointAHeaders),
                    new EndpointDefinition(new Uri("https://service-b.example.test")),
                    TimeSpan.FromSeconds(30),
                    maxConcurrency,
                    comparisonOptions: comparisonOptions,
                    requestExecutionOptions: requestExecutionOptions,
                    largeRunOptions: largeRunOptions))
            .Start();

    private static MemoryStream CreateStream(string value) =>
        new MemoryStream(Encoding.UTF8.GetBytes(value));

    private static string ToSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class TrackingReadStream : Stream
    {
        private long remainingBytes;

        public TrackingReadStream(long length)
        {
            remainingBytes = length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = (int)Math.Min(count, remainingBytes);
            if (bytesRead == 0)
            {
                return 0;
            }

            Array.Fill(buffer, (byte)'x', offset, bytesRead);
            remainingBytes -= bytesRead;
            return bytesRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bytesRead = (int)Math.Min(buffer.Length, remainingBytes);
            if (bytesRead == 0)
            {
                return ValueTask.FromResult(0);
            }

            buffer.Span[..bytesRead].Fill((byte)'x');
            remainingBytes -= bytesRead;
            return ValueTask.FromResult(bytesRead);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class DelayedResponseComparer(TimeSpan delay) : IResponseComparer
    {
        public async Task<RequestPairResult> CompareAsync(
            RequestItem request,
            RunOptions options,
            ResponseArtifactMetadata? responseA,
            ResponseArtifactMetadata? responseB,
            string? errorMessage,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return RequestPairResult.Classify(request, responseA, responseB, errorMessage);
        }
    }
    private sealed class CapturingObservabilityRecorder : IObservabilityRecorder
    {
        public CapturingObservabilityRecorder(TimeSpan slowPathThreshold)
        {
            SlowPathThreshold = slowPathThreshold;
        }

        public bool IsDurationLoggingEnabled => true;

        public bool IsExceptionLoggingEnabled => true;

        public bool IsDiagnosticsPersistenceEnabled => true;

        public bool IsDetailedCompareTimingEnabled { get; set; }

        public TimeSpan SlowPathThreshold { get; }

        public List<SlowRequestPathDiagnostic> SlowPaths { get; } = new List<SlowRequestPathDiagnostic>();

        public List<ExceptionDiagnostic> Exceptions { get; } = new List<ExceptionDiagnostic>();

        public List<(string PhaseName, TimeSpan Duration)> RunPhases { get; } = new List<(string PhaseName, TimeSpan Duration)>();

        public void RecordRunPhase(RunId runId, string phaseName, TimeSpan duration)
        {
            RunPhases.Add((phaseName, duration));
        }

        public void RecordRequestPath(RunId runId, string relativePath, TimeSpan duration)
        {
            if (duration >= SlowPathThreshold)
            {
                SlowPaths.Add(new SlowRequestPathDiagnostic(relativePath, duration));
            }
        }

        public void RecordException(
            RunId runId,
            string stage,
            Exception exception,
            string? relativePath = null,
            EndpointSlot? endpoint = null) =>
            Exceptions.Add(new ExceptionDiagnostic(stage, exception.GetType().Name, exception.Message, exception.StackTrace, relativePath, endpoint));

        public RunDiagnosticsSnapshot? CreateSnapshot(RunId runId) =>
            new RunDiagnosticsSnapshot(SlowPaths, Exceptions);
    }
    private sealed class FakeRequestBatchStore : IRequestBatchStore
    {
        private readonly RequestBatchManifest manifest;

        public FakeRequestBatchStore(RequestBatchManifest manifest)
        {
            this.manifest = manifest;
        }

        public Task<RequestBatchManifest> StageDirectoryAsync(
            string sourceDirectory,
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<RequestBatchManifest> StageFilesAsync(
            string sourceDirectory,
            IReadOnlyList<string> sourceFiles,
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<RequestBatchManifest> LoadManifestAsync(
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<Stream> OpenRequestBodyAsync(
            RequestBatchReference batchReference,
            RequestItem request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(CreateStream($"request:{request.RelativePath}"));
    }

    private sealed class FakeEndpointRequestSender : IEndpointRequestSender
    {
        private readonly Func<EndpointRequest, EndpointResponse> send;
        private readonly TimeSpan delay;
        private readonly Func<EndpointRequest, TimeSpan>? delaySelector;
        private readonly object gate = new object();
        private readonly Dictionary<string, int> activeRequestPathCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public FakeEndpointRequestSender(
            Func<EndpointRequest, EndpointResponse> send,
            TimeSpan? delay = null,
            Func<EndpointRequest, TimeSpan>? delaySelector = null)
        {
            this.send = send;
            this.delay = delay ?? TimeSpan.Zero;
            this.delaySelector = delaySelector;
        }

        public List<EndpointRequest> SentRequests { get; } = new List<EndpointRequest>();

        public int MaxActiveRequestPaths { get; private set; }

        public static FakeEndpointRequestSender ForBody(string body, TimeSpan? delay = null) =>
            new FakeEndpointRequestSender(
                _ => new EndpointResponse(200, "application/json", CreateStream(body)),
                delay);

        public async Task<EndpointResponse> SendAsync(
            EndpointRequest request,
            CancellationToken cancellationToken = default)
        {
            EnterRequestPath(request.Request.RelativePath);
            try
            {
                lock (gate)
                {
                    SentRequests.Add(request);
                }

                TimeSpan effectiveDelay = delaySelector?.Invoke(request) ?? delay;
                if (effectiveDelay > TimeSpan.Zero)
                {
                    await Task.Delay(effectiveDelay, cancellationToken).ConfigureAwait(false);
                }

                return send(request);
            }
            finally
            {
                ExitRequestPath(request.Request.RelativePath);
            }
        }

        private void EnterRequestPath(string relativePath)
        {
            lock (gate)
            {
                activeRequestPathCounts.TryGetValue(relativePath, out int count);
                activeRequestPathCounts[relativePath] = count + 1;
                MaxActiveRequestPaths = Math.Max(MaxActiveRequestPaths, activeRequestPathCounts.Count);
            }
        }

        private void ExitRequestPath(string relativePath)
        {
            lock (gate)
            {
                int count = activeRequestPathCounts[relativePath] - 1;
                if (count == 0)
                {
                    activeRequestPathCounts.Remove(relativePath);
                    return;
                }

                activeRequestPathCounts[relativePath] = count;
            }
        }
    }

    private sealed class DelegateCleanupStage : IRunCleanupStage
    {
        private readonly Func<ComparisonRun, CleanupStageContext, CancellationToken, Task> cleanup;

        public DelegateCleanupStage(Func<ComparisonRun, CleanupStageContext, CancellationToken, Task> cleanup)
        {
            this.cleanup = cleanup;
        }

        public async Task<CleanupStageResult> CleanupAsync(
            ComparisonRun run,
            CleanupStageContext context,
            CancellationToken cancellationToken = default)
        {
            await cleanup(run, context, cancellationToken);
            return CleanupStageResult.Empty;
        }
    }

    private sealed class FakeRunArtifactStore : IRunArtifactStore
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, byte[]> savedContent = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> SavedBodies
        {
            get
            {
                lock (gate)
                {
                    return savedContent.ToDictionary(pair => pair.Key, pair => Encoding.UTF8.GetString(pair.Value), StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public List<Type> SavedStreamTypes { get; } = new List<Type>();

        public async Task<ResponseArtifactMetadata> SaveResponseAsync(
            RunId runId,
            EndpointSlot endpoint,
            RequestItem request,
            int statusCode,
            string? contentType,
            Stream body,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                SavedStreamTypes.Add(body.GetType());
            }

            using MemoryStream memoryStream = new MemoryStream();
            await body.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            byte[] content = memoryStream.ToArray();
            string artifactId = $"runs/{runId.Value}/artifacts/{endpoint}/{request.RelativePath}";
            lock (gate)
            {
                savedContent[artifactId] = content;
            }

            return new ResponseArtifactMetadata(
                endpoint,
                new ArtifactReference(artifactId, contentType),
                statusCode,
                contentType,
                content.Length,
                ToSha256(content));
        }

        public Task<Stream> OpenReadAsync(
            ArtifactReference artifact,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                return Task.FromResult<Stream>(new MemoryStream(savedContent[artifact.ArtifactId]));
            }
        }
    }

    private sealed class FakeRunDetailStore : IRunDetailStore
    {
        public IReadOnlyList<RequestPairResult> SavedResults { get; private set; } = Array.Empty<RequestPairResult>();

        public int SaveCount { get; private set; }

        public Task<RunDetailReference> SaveDetailsAsync(
            RunId runId,
            IReadOnlyList<RequestPairResult> results,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            SavedResults = results;
            return Task.FromResult(new RunDetailReference($"runs/{runId.Value}/details/index.json"));
        }

        public Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(
            RunDetailReference detailReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedResults);

        public Task<RunDetailPage> LoadPageAsync(
            RunDetailReference detailReference,
            RunDetailQuery query,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RequestPairResult> pageItems = SavedResults
                .Skip(query.Offset)
                .Take(query.Limit)
                .ToList();
            return Task.FromResult(new RunDetailPage(pageItems, SavedResults.Count, query.Offset, query.Limit));
        }
    }

    private sealed class CapturingProgressReporter : IRunProgressReporter
    {
        public List<(RunStatus Status, RunProgress Progress)> Events { get; } = new List<(RunStatus Status, RunProgress Progress)>();

        public Task ReportAsync(
            RunStatus status,
            RunProgress progress,
            CancellationToken cancellationToken = default)
        {
            Events.Add((status, progress));
            return Task.CompletedTask;
        }
    }
}
