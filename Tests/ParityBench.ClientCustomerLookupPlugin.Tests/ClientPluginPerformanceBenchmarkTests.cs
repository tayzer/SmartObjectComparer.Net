using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Engine.Pipeline;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.Plugins;
using ParityBench.NET.Workspaces;

namespace ParityBench.ClientCustomerLookupPlugin.Tests;

/// <summary>
/// Opt-in performance fitness test for the real plugin package and production pipeline.
/// Unlike the generic engine benchmark, this loads the plugin from disk, executes its
/// SOAP/JSON request and mapping middleware, persists raw/canonical/focused artifacts,
/// applies the plugin's comparison defaults, and writes paged details.
/// </summary>
[TestClass]
[TestCategory("Performance")]
public sealed class ClientPluginPerformanceBenchmarkTests
{
    private const string EnableVariable = "PB_RUN_CLIENT_PLUGIN_FITNESS";
    private const string ChildVariable = "PB_CLIENT_PLUGIN_FITNESS_CHILD";
    private const string ChildOutputVariable = "PB_CLIENT_PLUGIN_FITNESS_CHILD_OUTPUT";
    private const string CountVariable = "PB_CLIENT_PLUGIN_FITNESS_COUNT";
    private const string ConcurrencyVariable = "PB_CLIENT_PLUGIN_FITNESS_CONCURRENCY";
    private const string MappingConcurrencyVariable = "PB_CLIENT_PLUGIN_FITNESS_MAPPING_CONCURRENCY";
    private const string FocusedConcurrencyVariable = "PB_CLIENT_PLUGIN_FITNESS_FOCUSED_CONCURRENCY";
    private const string GcModeVariable = "PB_CLIENT_PLUGIN_FITNESS_GC_MODE";
    private const string GcHeapCountVariable = "PB_CLIENT_PLUGIN_FITNESS_GC_HEAP_COUNT";
    private const string IterationsVariable = "PB_CLIENT_PLUGIN_FITNESS_ITERATIONS";
    private const string ConcurrenciesVariable = "PB_CLIENT_PLUGIN_FITNESS_CONCURRENCIES";
    private const string CountsVariable = "PB_CLIENT_PLUGIN_FITNESS_COUNTS";
    private const string OutputVariable = "PB_PERFORMANCE_OUTPUT";
    private const string FingerprintVariable = "PB_CLIENT_STRUCTURAL_FINGERPRINT";
    private const string TraceVariable = "PB_CLIENT_PLUGIN_FITNESS_TRACE";
    private const string ReplayWorkspaceVariable = "PB_CLIENT_REPLAY_WORKSPACE";
    private const string ReplayRunIdVariable = "PB_CLIENT_REPLAY_RUN_ID";
    private const string ReplayCaptureVariable = "PB_CLIENT_REPLAY_CAPTURE";
    private const string CalibrationCaptureManifest = ".paritybench-calibration-sample.json";

    private const string PluginId = "client.customer-lookup";
    private const string ComparisonId = "client.customer-lookup.soap-vs-json";
    private const string RequestStepId = "client.customer-lookup.request";
    private const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";
    private const string PrimarySubscriptionKey = "fitness-primary-key";
    private const string FinalSubscriptionKey = "fitness-final-key";
    private const string EndpointBSubscriptionKey = "fitness-endpoint-b-key";

    // Client evidence: 917,603,311 response bytes / 3,146 pairs.
    private const double TargetResponseBytesPerPair = 291_673d;
    // Client evidence: 1,472,109,157,896 managed bytes / 3,146 pairs.
    private const double TargetAllocatedBytesPerPair = 467_930_438d;
    // Client evidence: 14,823,011 aggregate normalization ms / 3,146 pairs.
    private const double TargetNormalizationMillisecondsPerPair = 4_711.701d;
    private const int EndpointBResponseBytes = 144 * 1024;

    [TestMethod]
    public async Task ExecuteAsync_RealClientPlugin_Fitness()
    {
        RequireEnabled();
        int iterations = ParsePositiveInt(IterationsVariable, 3);
        int matrixCount = ParsePositiveInt(CountVariable, 1000);
        int[] concurrencies = ParsePositiveIntList(ConcurrenciesVariable, [8, 12, 16, 20]);
        int[] scalingCounts = ParsePositiveIntList(CountsVariable, [2500, 8000]);
        Assert.AreEqual(2, scalingCounts.Length, $"{CountsVariable} must contain two counts.");
        Assert.IsTrue(scalingCounts[1] >= scalingCounts[0], $"{CountsVariable} must be ascending.");

        string childRoot = Path.Combine(Path.GetTempPath(), "ParityBenchNET.ClientFitness.Children", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(childRoot);
        try
        {
            Dictionary<string, ClientFitnessChildReport> measured = new(StringComparer.Ordinal);
            async Task<ClientFitnessChildReport> MeasureAsync(FitnessTuple tuple, int count = 0)
            {
                int effectiveCount = count == 0 ? matrixCount : count;
                string key = $"{effectiveCount}:{tuple.Label}";
                if (!measured.TryGetValue(key, out ClientFitnessChildReport? report))
                {
                    report = await LaunchChildAsync(effectiveCount, tuple, iterations, childRoot);
                    measured.Add(key, report);
                }
                return report;
            }

            FitnessGcCandidate[] gcCandidates =
            [
                new(WorkerGcMode.Workstation, null),
                new(WorkerGcMode.ServerAdaptive, null),
                new(WorkerGcMode.ServerFixed, 4),
                new(WorkerGcMode.ServerFixed, 8),
                new(WorkerGcMode.ServerFixed, 12),
            ];
            List<ClientFitnessChildReport> matrix = new();
            foreach (FitnessGcCandidate gc in gcCandidates)
            foreach (int concurrency in concurrencies)
                matrix.Add(await MeasureAsync(new FitnessTuple(8, concurrency, 8, gc.Mode, gc.HeapCount)));

            ClientFitnessCandidate? recommendation = SelectRecommendation(matrix.Select(Summarize).ToList());
            if (recommendation is not null)
            {
                foreach (int mapping in new[] { 4, 8, 12, 16 })
                    matrix.Add(await MeasureAsync(new FitnessTuple(mapping, recommendation.ComparisonConcurrency, 8, recommendation.WorkerGcMode, recommendation.ServerGcHeapCount)));
                recommendation = SelectRecommendation(matrix.Select(Summarize).ToList());
            }
            if (recommendation is not null)
            {
                foreach (int focused in new[] { 4, 8, 12, 16 })
                    matrix.Add(await MeasureAsync(new FitnessTuple(recommendation.MappingConcurrency, recommendation.ComparisonConcurrency, focused, recommendation.WorkerGcMode, recommendation.ServerGcHeapCount)));
                recommendation = SelectRecommendation(matrix.Select(Summarize).ToList());
            }
            matrix = matrix.GroupBy(item => item.Candidate.Label, StringComparer.Ordinal).Select(group => group.First()).ToList();

            string expectedHash = matrix[0].Runs[0].OutputSha256;
            List<string> failures = new();
            if (matrix.SelectMany(candidate => candidate.Runs).Any(run => run.OutputSha256 != expectedHash))
            {
                failures.Add("Ordered differences changed across concurrency candidates.");
            }

            List<ClientFitnessCandidate> candidates = matrix.Select(Summarize).ToList();
            if (recommendation is null)
            {
                failures.Add("No stage/GC candidate passed the dedicated-machine memory and post-run-release gates.");
            }

            List<ClientFitnessChildReport> scaling = new();
            if (recommendation is not null)
            {
                foreach (int count in scalingCounts)
                {
                    scaling.Add(await MeasureAsync(recommendation.ToTuple(), count));
                }

                double smallThroughput = Median(scaling[0].Runs.Select(run => run.PairsPerSecond));
                double largeThroughput = Median(scaling[1].Runs.Select(run => run.PairsPerSecond));
                if (largeThroughput < smallThroughput * .85d)
                {
                    failures.Add($"8k throughput {largeThroughput:F2} pairs/s is below 85% of 2.5k throughput {smallThroughput:F2} pairs/s.");
                }
                double largeWallMilliseconds = Median(scaling[1].Runs.Select(run => run.WallClockMilliseconds));
                if (largeWallMilliseconds > TimeSpan.FromMinutes(10).TotalMilliseconds)
                    failures.Add($"Comparison-only 8k median wall time is {TimeSpan.FromMilliseconds(largeWallMilliseconds)}; limit is 10 minutes.");
                if (largeWallMilliseconds > TimeSpan.FromMinutes(30).TotalMilliseconds)
                    failures.Add($"End-to-end 8k median wall time is {TimeSpan.FromMilliseconds(largeWallMilliseconds)}; limit is 30 minutes.");
                if (scaling.Any(item => item.Runs.Select(run => run.OutputSha256).Distinct(StringComparer.Ordinal).Count() != 1))
                    failures.Add("Ordered differences changed between repeated 2.5k/8k scaling runs.");
                if (scaling.Any(item => item.Runs
                    .Select(run => (run.RetainedArtifactCount, run.TrimmedByPolicyArtifactCount, run.MissingUnexpectedlyArtifactCount))
                    .Distinct().Count() != 1))
                    failures.Add("Retention counts changed between repeated 2.5k/8k scaling runs.");
                long scalingBudget = scaling[1].Runs.Min(run => run.MemoryBudgetBytes);
                long scalingPeak = MedianLong(scaling[1].Runs.Select(run => run.PeakPrivateBytes));
                if (scalingPeak > scalingBudget)
                    failures.Add($"8k median peak private memory {scalingPeak} exceeds budget {scalingBudget}.");
            }

            ClientFingerprintExport? fingerprint = ReadClientFingerprint();
            ClientWorkloadFidelity fidelity = BuildFidelity(matrix, fingerprint);
            ClientStructuralFidelity? structuralFidelity = fingerprint is null ? null : BuildStructuralFidelity(matrix, fingerprint);
            if (fingerprint is null)
            {
                failures.Add($"A client structural fingerprint is required. Set {FingerprintVariable} to its JSON path.");
            }
            if (fidelity.ResponseBytesDeviationPercent > 10d)
            {
                failures.Add($"Generated response bytes/pair differ from the client fingerprint by {fidelity.ResponseBytesDeviationPercent:F1}%.");
            }
            if (fidelity.AllocationDeviationPercent > 20d)
            {
                failures.Add($"Managed allocation per pair differs from offline client evidence by {fidelity.AllocationDeviationPercent:F1}%.");
            }
            if (fidelity.NormalizationDeviationPercent > 20d)
            {
                failures.Add($"Normalization per pair differs from offline client evidence by {fidelity.NormalizationDeviationPercent:F1}%.");
            }
            if (fidelity.ActualAllocatedBytesPerPair > 64d * 1024 * 1024)
                failures.Add($"Managed allocation is {fidelity.ActualAllocatedBytesPerPair / 1024 / 1024:F1} MiB/pair; limit is 64 MiB/pair.");
            if (fidelity.ActualAllocatedBytesPerPair > TargetAllocatedBytesPerPair * .20d)
                failures.Add("Managed allocation did not improve by at least 80% from the pre-hardening client baseline.");
            if (fidelity.ActualNormalizationMillisecondsPerPair > 500d)
                failures.Add($"Normalization is {fidelity.ActualNormalizationMillisecondsPerPair:F1} ms/pair; limit is 500 ms/pair.");
            if (fidelity.ActualNormalizationMillisecondsPerPair > TargetNormalizationMillisecondsPerPair * .15d)
                failures.Add("Normalization did not improve by at least 85% from the pre-hardening client baseline.");
            if (structuralFidelity is not null && structuralFidelity.MaximumDeviationPercent > 10d)
                failures.Add($"Generated structural workload differs from the client fingerprint by up to {structuralFidelity.MaximumDeviationPercent:F1}% (limit 10%).");

            ClientFitnessReport report = new(
                DateTimeOffset.UtcNow,
                Environment.MachineName,
                Environment.ProcessorCount,
                Enum.GetNames<ClientShapeVariant>(),
                matrixCount,
                iterations,
                expectedHash,
                recommendation?.ToTuple(),
                fidelity,
                structuralFidelity,
                failures,
                candidates,
                matrix,
                scaling);
            string outputDirectory = PerformanceOutputDirectory();
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, $"client-plugin-fitness-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, JsonOptions));

            Assert.AreEqual(0, failures.Count, $"Client-plugin fitness failed: {string.Join(" ", failures)} Report: {outputPath}");
        }
        finally
        {
            if (Directory.Exists(childRoot)) Directory.Delete(childRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_ClientPluginFitnessChild()
    {
        RequireEnabled();
        if (!string.Equals(Environment.GetEnvironmentVariable(ChildVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive("This test is launched by ExecuteAsync_RealClientPlugin_Fitness.");
        }

        int count = ParsePositiveInt(CountVariable, 1000);
        int concurrency = ParsePositiveInt(ConcurrencyVariable, 8);
        int mappingConcurrency = ParsePositiveInt(MappingConcurrencyVariable, 8);
        int focusedConcurrency = ParsePositiveInt(FocusedConcurrencyVariable, 8);
        WorkerGcMode gcMode = Enum.Parse<WorkerGcMode>(Environment.GetEnvironmentVariable(GcModeVariable) ?? nameof(WorkerGcMode.Workstation));
        int? gcHeapCount = gcMode == WorkerGcMode.ServerFixed ? ParsePositiveInt(GcHeapCountVariable, 4) : null;
        FitnessTuple candidate = new(mappingConcurrency, concurrency, focusedConcurrency, gcMode, gcHeapCount);
        int iterations = ParsePositiveInt(IterationsVariable, 3);
        string output = Environment.GetEnvironmentVariable(ChildOutputVariable)
            ?? throw new InvalidOperationException($"{ChildOutputVariable} is required in child mode.");

        await RunOnceAsync(Math.Min(10, count), candidate, "warmup");
        List<ClientFitnessMeasurement> runs = new();
        for (int iteration = 1; iteration <= iterations; iteration++)
        {
            runs.Add(await RunOnceAsync(count, candidate, $"{candidate.Label}-{count}-{iteration}"));
        }

        ClientFitnessChildReport report = new(
            count,
            candidate,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            runs);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, JsonOptions));
    }

    [TestMethod]
    public async Task ExecuteAsync_RetainedClientRunReplay()
    {
        RequireEnabled();
        string? sourceWorkspace = Environment.GetEnvironmentVariable(ReplayWorkspaceVariable);
        string? sourceRunId = Environment.GetEnvironmentVariable(ReplayRunIdVariable);
        if (string.IsNullOrWhiteSpace(sourceWorkspace) || string.IsNullOrWhiteSpace(sourceRunId))
            Assert.Inconclusive($"Set {ReplayWorkspaceVariable} and {ReplayRunIdVariable} to run a private local replay.");

        FileSystemRunStore sourceRunStore = new(sourceWorkspace);
        ComparisonRun sourceRun = await sourceRunStore.LoadAsync(new RunId(sourceRunId))
            ?? throw new InvalidOperationException($"Run '{sourceRunId}' was not found in '{sourceWorkspace}'.");
        if (sourceRun.Summary?.DetailIndexReference is null)
            throw new InvalidOperationException("Replay source run has no persisted detail index.");

        FileSystemRunArtifactStore sourceArtifacts = new(sourceWorkspace);
        FileSystemRunDetailStore sourceDetails = new(sourceWorkspace);
        IReadOnlyList<RequestPairResult> allDetails = await sourceDetails.LoadDetailsAsync(sourceRun.Summary.DetailIndexReference);
        FileSystemRequestBatchStore sourceBatches = new(sourceWorkspace);
        RequestBatchManifest sourceManifest = await sourceBatches.LoadManifestAsync(sourceRun.Options.RequestBatch);
        Dictionary<string, RequestItem> requests = sourceManifest.Requests.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        string captureDirectory = ResolveCaptureDirectory(sourceRun.Id);
        bool captureAvailable = IsOwnedCaptureDirectory(captureDirectory, sourceRun.Id);
        List<RequestPairResult> selectedDetails = new();
        foreach (RequestPairResult detail in allDetails.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            if (selectedDetails.Count == 1000) break;
            if (detail.ResponseA is null || detail.ResponseB is null || !requests.ContainsKey(detail.RelativePath)) continue;
            bool retained = await sourceArtifacts.ExistsAsync(detail.ResponseA.Artifact)
                && await sourceArtifacts.ExistsAsync(detail.ResponseB.Artifact);
            bool captured = captureAvailable
                && File.Exists(CapturedArtifactPath(captureDirectory, EndpointSlot.A, detail.RelativePath))
                && File.Exists(CapturedArtifactPath(captureDirectory, EndpointSlot.B, detail.RelativePath));
            if (!retained && !captured) continue;
            selectedDetails.Add(detail);
        }
        if (selectedDetails.Count < 1000)
            Assert.Inconclusive($"Only {selectedDetails.Count} usable raw pairs survive. Set ParityBench:Observability:CaptureNextRunForCalibration=true for one run, then disable it and replay the new run. Private captures default to '{DefaultCaptureBaseDirectory()}'.");

        RequestBatchManifest filteredManifest = new(
            sourceRun.Options.RequestBatch,
            selectedDetails.Select(detail => requests[detail.RelativePath]));
        FilteredRequestBatchStore replayBatchStore = new(sourceBatches, filteredManifest);
        ReplayEndpointSender replaySender = new(sourceArtifacts, selectedDetails, captureAvailable ? captureDirectory : null);
        FitnessTuple candidate = new(
            ParsePositiveInt(MappingConcurrencyVariable, 8),
            ParsePositiveInt(ConcurrencyVariable, 8),
            ParsePositiveInt(FocusedConcurrencyVariable, 8),
            Enum.Parse<WorkerGcMode>(Environment.GetEnvironmentVariable(GcModeVariable) ?? nameof(WorkerGcMode.Workstation)),
            null);
        if (candidate.WorkerGcMode == WorkerGcMode.ServerFixed)
            candidate = candidate with { ServerGcHeapCount = ParsePositiveInt(GcHeapCountVariable, 4) };

        string root = Path.Combine(Path.GetTempPath(), "ParityBenchNET.ClientReplay", Guid.NewGuid().ToString("N"));
        try
        {
            FileSystemRunArtifactStore outputArtifacts = new(root);
            FileSystemRunDetailStore outputDetails = new(root);
            JsonXmlContractPayloadSerializer serializer = new();
            PluginComparisonPlanFactory planFactory = new(
                new PluginCatalog([Path.GetDirectoryName(ResolvePluginPackageDirectory())!]),
                new PluginLoader(),
                services =>
                {
                    services.AddSingleton<IContractPayloadSerializer>(serializer);
                    services.AddSingleton(new HttpClient(new TokenEndpointHandler()));
                });
            ComparisonRunExecutor executor = new(
                replayBatchStore,
                replaySender,
                outputArtifacts,
                outputDetails,
                new HashOnlyResponseComparer(),
                planFactory,
                new FitnessObservabilityRecorder(),
                contractPayloadSerializer: serializer);
            PluginComparisonSelection plugin = new(PluginId, ComparisonId, stepConfiguration: StepConfiguration());
            RunOptions source = sourceRun.Options;
            RunOptions options = new(
                source.RequestBatch,
                source.EndpointA,
                source.EndpointB,
                source.Timeout,
                25,
                source.ResponseModelName,
                source.Comparison,
                source.RequestExecution,
                source.ContractProfile,
                new LargeRunOptions(
                    mappingConcurrency: candidate.MappingConcurrency,
                    comparisonConcurrency: candidate.ComparisonConcurrency,
                    focusedContentConcurrency: candidate.FocusedContentConcurrency,
                    workerGcMode: candidate.WorkerGcMode,
                    serverGcHeapCount: candidate.ServerGcHeapCount),
                source.RunRetentionModeOverride,
                source.ComparisonRulesSnapshotHash,
                plugin);

            ForceFullCollection();
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            Stopwatch stopwatch = Stopwatch.StartNew();
            RunResultSummary summary = await executor.ExecuteAsync(
                ComparisonRun.Create(new RunId($"replay-{Guid.NewGuid():N}"), options).Start(),
                NoOpProgressReporter.Instance);
            stopwatch.Stop();
            IReadOnlyList<RequestPairResult> replayed = await outputDetails.LoadDetailsAsync(summary.DetailIndexReference!);
            string sourceHash = ComputeOutputSha256(selectedDetails);
            string replayHash = ComputeOutputSha256(replayed);
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            ClientReplayReport report = new(
                DateTimeOffset.UtcNow,
                sourceRun.Id.Value,
                selectedDetails.Count,
                candidate,
                stopwatch.Elapsed.TotalMilliseconds,
                selectedDetails.Count / stopwatch.Elapsed.TotalSeconds,
                allocated,
                sourceHash,
                replayHash,
                summary.ExecutionMetrics!);
            string outputDirectory = PerformanceOutputDirectory();
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, $"client-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, JsonOptions));
            Assert.AreEqual(sourceHash, replayHash, $"Replay output changed. Report: {outputPath}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (captureAvailable)
            {
                DeleteOwnedCaptureDirectory(captureDirectory, sourceRun.Id);
            }
        }
    }

    private static string ResolveCaptureDirectory(RunId runId)
    {
        string? configured = Environment.GetEnvironmentVariable(ReplayCaptureVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(DefaultCaptureBaseDirectory(), runId.Value)
            : Path.GetFullPath(configured);
    }

    private static string DefaultCaptureBaseDirectory()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            string.IsNullOrWhiteSpace(localData) ? Path.GetTempPath() : localData,
            "ParityBench.NET",
            "CalibrationSamples");
    }

    private static string CapturedArtifactPath(string captureDirectory, EndpointSlot endpoint, string relativePath)
    {
        string normalized = new RequestItem(relativePath).RelativePath.Replace('/', Path.DirectorySeparatorChar);
        string endpointRoot = Path.GetFullPath(Path.Combine(captureDirectory, endpoint.ToString()));
        string path = Path.GetFullPath(Path.Combine(endpointRoot, normalized));
        if (!path.StartsWith(endpointRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Captured artifact escaped its endpoint directory.");
        return path;
    }

    private static bool IsOwnedCaptureDirectory(string captureDirectory, RunId runId)
    {
        string manifestPath = Path.Combine(captureDirectory, CalibrationCaptureManifest);
        if (!File.Exists(manifestPath) || !string.Equals(new DirectoryInfo(captureDirectory).Name, runId.Value, StringComparison.OrdinalIgnoreCase))
            return false;
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.TryGetProperty("RunId", out JsonElement capturedRun)
            && string.Equals(capturedRun.GetString(), runId.Value, StringComparison.Ordinal);
    }

    private static void DeleteOwnedCaptureDirectory(string captureDirectory, RunId runId)
    {
        if (!IsOwnedCaptureDirectory(captureDirectory, runId))
            throw new InvalidOperationException($"Refusing to delete unowned calibration directory '{captureDirectory}'.");
        Directory.Delete(captureDirectory, recursive: true);
    }

    private static async Task<ClientFitnessMeasurement> RunOnceAsync(int count, FitnessTuple candidate, string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "ParityBenchNET.ClientFitness", Guid.NewGuid().ToString("N"));
        string workspace = Path.Combine(root, "workspace");
        string source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        try
        {
            for (int index = 0; index < count; index++)
            {
                await File.WriteAllTextAsync(Path.Combine(source, $"request-{index:D5}.xml"), SoapRequestBody);
            }

            FileSystemRequestBatchStore batchStore = new(workspace);
            RequestBatchManifest manifest = await batchStore.StageDirectoryAsync(source, new RequestBatchReference($"batch-{name}"));
            FileSystemRunArtifactStore artifactStore = new(workspace);
            FileSystemRunDetailStore detailStore = new(workspace);
            JsonXmlContractPayloadSerializer serializer = new();
            TokenEndpointHandler tokenHandler = new();
            PluginComparisonPlanFactory planFactory = new(
                new PluginCatalog([Path.GetDirectoryName(ResolvePluginPackageDirectory())!]),
                new PluginLoader(),
                services =>
                {
                    services.AddSingleton<IContractPayloadSerializer>(serializer);
                    services.AddSingleton(new HttpClient(tokenHandler));
                });
            ClientShapeEndpointSender sender = new();
            RetentionCleanupStage cleanupStage = new(
                artifactStore,
                detailStore,
                new RetentionPolicyEvaluator(),
                Options.Create(new RetentionConfiguration()));
            ComparisonRunExecutor executor = new(
                batchStore,
                sender,
                artifactStore,
                detailStore,
                new HashOnlyResponseComparer(),
                planFactory,
                new FitnessObservabilityRecorder(),
                cleanupStage,
                contractPayloadSerializer: serializer);
            ComparisonRun run = ComparisonRun.Create(
                new RunId($"fitness-{Guid.NewGuid():N}"),
                new RunOptions(
                    manifest.BatchReference,
                    new EndpointDefinition(new Uri("http://fitness.test/client/customer-lookup/soap")),
                    new EndpointDefinition(new Uri("http://fitness.test/client/customer-lookup/json")),
                    TimeSpan.FromSeconds(120),
                    maxConcurrency: 25,
                    comparisonOptions: new ComparisonOptions(maxDifferences: 100, includeAllDifferences: true),
                    largeRunOptions: new LargeRunOptions(
                        mappingConcurrency: candidate.MappingConcurrency,
                        comparisonConcurrency: candidate.ComparisonConcurrency,
                        focusedContentConcurrency: candidate.FocusedContentConcurrency,
                        workerGcMode: candidate.WorkerGcMode,
                        serverGcHeapCount: candidate.ServerGcHeapCount),
                    pluginComparison: new PluginComparisonSelection(
                        PluginId,
                        ComparisonId,
                        stepConfiguration: StepConfiguration())))
                .Start();

            ForceFullCollection();
            Process process = Process.GetCurrentProcess();
            long privateBefore = process.PrivateMemorySize64;
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            Stopwatch stopwatch = Stopwatch.StartNew();
            RunResultSummary summary = await executor.ExecuteAsync(run, NoOpProgressReporter.Instance);
            stopwatch.Stop();
            process.Refresh();
            RunExecutionMetrics metrics = summary.ExecutionMetrics!;
            DetailAnalysis details = await AnalyzePersistedDetailsAsync(detailStore, summary.DetailIndexReference!);
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            DetailedCompareMetrics? detailed = metrics.DetailedCompareMetrics;
            NormalizationWorkMetrics? normalization = metrics.NormalizationWorkMetrics;
            PipelineStageMetrics? pipeline = metrics.PipelineStageMetrics;
            RunRuntimeMetrics? runtime = metrics.RuntimeMetrics;

            Assert.AreEqual(count, summary.TotalPairs);
            Assert.AreEqual(
                0,
                summary.ErrorPairs,
                string.Join(Environment.NewLine, details.ErrorMessages));
            Assert.AreEqual(count, details.PairCount);
            Assert.AreEqual(count, summary.DifferentPairs, "Every generated pair should retain real surviving differences.");
            Assert.AreEqual(0, details.PairsWithoutDifferences, "No generated pair may lose all surviving differences.");
            Assert.AreEqual(count, sender.EndpointAPairs);
            Assert.AreEqual(count, sender.EndpointBPairs);
            ForceFullCollection();
            process.Refresh();

            return new ClientFitnessMeasurement(
                name,
                count,
                candidate.ComparisonConcurrency,
                stopwatch.Elapsed.TotalMilliseconds,
                count / stopwatch.Elapsed.TotalSeconds,
                metrics.ResponseBytesWritten,
                detailed?.ArtifactBytesRead ?? 0,
                metrics.RequestExecutionDuration.TotalMilliseconds,
                metrics.ComparisonDuration.TotalMilliseconds,
                metrics.FinalizationDuration.TotalMilliseconds,
                detailed?.ComparisonModelNormalizationDuration.TotalMilliseconds ?? 0,
                detailed?.CompareNetObjectsTraversalDuration.TotalMilliseconds ?? 0,
                detailed?.DifferenceMaterializationDuration.TotalMilliseconds ?? 0,
                detailed?.CanonicalMappingDuration.TotalMilliseconds ?? 0,
                detailed?.PluginMappingDuration.TotalMilliseconds ?? 0,
                detailed?.FocusedContentDuration.TotalMilliseconds ?? 0,
                detailed?.OtherCompareWorkerDuration.TotalMilliseconds ?? 0,
                detailed?.CompareQueueWaitDuration.TotalMilliseconds ?? 0,
                detailed?.ExecutionWorkerBackpressureDuration.TotalMilliseconds ?? 0,
                metrics.ProcessResourceMetrics?.ProcessCpuDuration.TotalMilliseconds ?? 0,
                metrics.ProcessResourceMetrics?.AverageProcessCoreUtilizationPercent ?? 0,
                metrics.ProcessResourceMetrics?.AverageMachineCpuUtilizationPercent ?? 0,
                metrics.ProcessResourceMetrics?.PeakWorkingSetBytes ?? process.WorkingSet64,
                metrics.ProcessResourceMetrics?.PeakPrivateBytes ?? process.PrivateMemorySize64,
                privateBefore,
                process.PrivateMemorySize64,
                allocated,
                metrics.ProcessResourceMetrics?.Gen0CollectionCount ?? 0,
                metrics.ProcessResourceMetrics?.Gen1CollectionCount ?? 0,
                metrics.ProcessResourceMetrics?.Gen2CollectionCount ?? 0,
                metrics.RetainedArtifactCount,
                metrics.TrimmedByPolicyArtifactCount,
                metrics.MissingUnexpectedlyArtifactCount,
                details.DifferentPairCount,
                details.DifferenceCount,
                normalization?.GraphTraversalDuration.TotalMilliseconds ?? 0,
                normalization?.SortKeyConstructionDuration.TotalMilliseconds ?? 0,
                normalization?.CollectionSortDuration.TotalMilliseconds ?? 0,
                normalization?.LegacyFallbackDuration.TotalMilliseconds ?? 0,
                normalization?.RestorationDuration.TotalMilliseconds ?? 0,
                normalization?.ObjectNodeCount ?? 0,
                normalization?.PropertyNodeCount ?? 0,
                normalization?.CollectionNodeCount ?? 0,
                normalization?.CollectionItemCount ?? 0,
                normalization?.ScalarNodeCount ?? 0,
                normalization?.ScalarUtf8Bytes ?? 0,
                normalization?.SortKeyBytes ?? 0,
                normalization?.MaximumSortKeyBytes ?? 0,
                normalization?.SortCollisionGroupCount ?? 0,
                pipeline?.MappingWorkerDuration.TotalMilliseconds ?? 0,
                pipeline?.ComparisonWorkerDuration.TotalMilliseconds ?? 0,
                pipeline?.FocusedContentWorkerDuration.TotalMilliseconds ?? 0,
                pipeline?.DetailPersistenceDuration.TotalMilliseconds ?? 0,
                pipeline?.ExecuteToMappingQueueWaitDuration.TotalMilliseconds ?? 0,
                pipeline?.MappingToComparisonQueueWaitDuration.TotalMilliseconds ?? 0,
                pipeline?.ComparisonToFocusedQueueWaitDuration.TotalMilliseconds ?? 0,
                pipeline?.ExecutionBackpressureDuration.TotalMilliseconds ?? 0,
                pipeline?.MappingBackpressureDuration.TotalMilliseconds ?? 0,
                pipeline?.ComparisonBackpressureDuration.TotalMilliseconds ?? 0,
                pipeline?.MaximumExecuteToMappingDepth ?? 0,
                pipeline?.MaximumMappingToComparisonDepth ?? 0,
                pipeline?.MaximumComparisonToFocusedDepth ?? 0,
                runtime?.IsServerGc ?? System.Runtime.GCSettings.IsServerGC,
                runtime?.DynamicAdaptationEnabled,
                runtime?.MemoryBudgetBytes ?? 0,
                details.OutputSha256);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> StepConfiguration() =>
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [RequestStepId] = new Dictionary<string, string>
            {
                ["primaryTokenUrl"] = "http://token.test/primary",
                ["primaryTokenSubscriptionKey"] = PrimarySubscriptionKey,
                ["finalTokenUrl"] = "http://token.test/final",
                ["finalTokenSubscriptionKey"] = FinalSubscriptionKey,
                ["endpointBSubscriptionKey"] = EndpointBSubscriptionKey,
            },
        };

    private static async Task<ClientFitnessChildReport> LaunchChildAsync(
        int count,
        FitnessTuple candidate,
        int iterations,
        string childRoot)
    {
        string outputPath = Path.Combine(childRoot, $"count-{count}-{candidate.Label}.json");
        string project = Path.Combine(FindRepositoryRoot(), "Tests", "ParityBench.ClientCustomerLookupPlugin.Tests", "ParityBench.ClientCustomerLookupPlugin.Tests.csproj");
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = Path.Combine(FindRepositoryRoot(), "Tests"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(CurrentBuildConfiguration());
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add("FullyQualifiedName~ClientPluginPerformanceBenchmarkTests.ExecuteAsync_ClientPluginFitnessChild");
        startInfo.Environment[EnableVariable] = "1";
        startInfo.Environment[ChildVariable] = "1";
        startInfo.Environment[ChildOutputVariable] = outputPath;
        startInfo.Environment[CountVariable] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment[ConcurrencyVariable] = candidate.ComparisonConcurrency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment[MappingConcurrencyVariable] = candidate.MappingConcurrency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment[FocusedConcurrencyVariable] = candidate.FocusedContentConcurrency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment[GcModeVariable] = candidate.WorkerGcMode.ToString();
        if (candidate.ServerGcHeapCount is { } heapCount)
            startInfo.Environment[GcHeapCountVariable] = heapCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ApplyChildGcEnvironment(startInfo, candidate);
        ApplyTraceWrapper(startInfo, candidate, count);
        startInfo.Environment[IterationsVariable] = iterations.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start client fitness child process.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = await stdout;
        string error = await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Client fitness child count={count}, {candidate.Label} failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }

        ClientFitnessChildReport report = JsonSerializer.Deserialize<ClientFitnessChildReport>(await File.ReadAllTextAsync(outputPath), JsonOptions)
            ?? throw new InvalidOperationException($"Client fitness child report '{outputPath}' was empty.");
        // Production Desktop runs use this same one-run process lifetime. Once the
        // child exits, its private bytes are actually released to the host machine.
        return report with { ProcessExitedCleanly = true };
    }

    private static void ApplyChildGcEnvironment(ProcessStartInfo startInfo, FitnessTuple candidate)
    {
        startInfo.Environment.Remove("DOTNET_GCHeapCount");
        startInfo.Environment.Remove("DOTNET_GCDynamicAdaptationMode");
        switch (candidate.WorkerGcMode)
        {
            case WorkerGcMode.Workstation:
                startInfo.Environment["DOTNET_gcServer"] = "0";
                break;
            case WorkerGcMode.ServerAdaptive:
                startInfo.Environment["DOTNET_gcServer"] = "1";
                startInfo.Environment["DOTNET_GCDynamicAdaptationMode"] = "1";
                break;
            case WorkerGcMode.ServerFixed:
                startInfo.Environment["DOTNET_gcServer"] = "1";
                startInfo.Environment["DOTNET_GCDynamicAdaptationMode"] = "0";
                startInfo.Environment["DOTNET_GCHeapCount"] = candidate.ServerGcHeapCount!.Value.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
                break;
            default:
                throw new InvalidOperationException("Fitness candidates must select an explicit GC mode.");
        }
    }

    private static void ApplyTraceWrapper(ProcessStartInfo startInfo, FitnessTuple candidate, int count)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(TraceVariable), "1", StringComparison.Ordinal)) return;

        string[] childArguments = startInfo.ArgumentList.ToArray();
        startInfo.ArgumentList.Clear();
        startInfo.FileName = "dotnet-trace";
        string traceDirectory = Path.Combine(PerformanceOutputDirectory(), "traces");
        Directory.CreateDirectory(traceDirectory);
        startInfo.ArgumentList.Add("collect");
        startInfo.ArgumentList.Add("--providers");
        startInfo.ArgumentList.Add("Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x1C000080018:5");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(Path.Combine(traceDirectory, $"client-fitness-{count}-{candidate.Label}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.nettrace"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("dotnet");
        foreach (string argument in childArguments) startInfo.ArgumentList.Add(argument);
    }

    private static ClientFitnessCandidate Summarize(ClientFitnessChildReport report)
    {
        long peakPrivate = MedianLong(report.Runs.Select(run => Math.Max(run.PeakPrivateBytes, run.PrivateBytesAfter)));
        long postRunPrivate = report.ProcessExitedCleanly
            ? 0
            : MedianLong(report.Runs.Select(run => run.PrivateBytesAfter));
        long memoryBudget = Math.Min(
            (long)(report.TotalAvailableMemoryBytes * .60d),
            Math.Max(0, report.TotalAvailableMemoryBytes - (4L * 1024 * 1024 * 1024)));
        string? rejectionReason = peakPrivate > memoryBudget
            ? "Median peak private memory exceeds the dedicated-machine budget."
            : postRunPrivate > peakPrivate * .80d
                ? "Post-run private memory did not return below 80% of peak before process exit."
                : null;
        return new ClientFitnessCandidate(
            report.Candidate.MappingConcurrency,
            report.Candidate.ComparisonConcurrency,
            report.Candidate.FocusedContentConcurrency,
            report.Candidate.WorkerGcMode,
            report.Candidate.ServerGcHeapCount,
            Median(report.Runs.Select(run => run.PairsPerSecond)),
            peakPrivate,
            postRunPrivate,
            memoryBudget,
            rejectionReason,
            MedianLong(report.Runs.Select(run => run.ManagedAllocatedBytes)),
            Median(report.Runs.Select(run => run.NormalizationMilliseconds)));
    }

    private static ClientFitnessCandidate? SelectRecommendation(IReadOnlyList<ClientFitnessCandidate> candidates)
    {
        ClientFitnessCandidate[] eligible = candidates.Where(candidate => candidate.RejectionReason is null).ToArray();
        if (eligible.Length == 0) return null;
        double best = eligible.Max(candidate => candidate.MedianPairsPerSecond);
        return eligible
            .Where(candidate => candidate.MedianPairsPerSecond >= best * .95d)
            .OrderBy(candidate => candidate.MedianPeakPrivateBytes)
            .ThenBy(candidate => candidate.MappingConcurrency + candidate.ComparisonConcurrency + candidate.FocusedContentConcurrency)
            .ThenBy(candidate => candidate.ComparisonConcurrency)
            .First();
    }

    private static ClientWorkloadFidelity BuildFidelity(
        IReadOnlyList<ClientFitnessChildReport> matrix,
        ClientFingerprintExport? fingerprint)
    {
        ClientFitnessChildReport lowestConcurrency = matrix.OrderBy(candidate => candidate.Candidate.ComparisonConcurrency).First();
        double responseBytesPerPair = Median(lowestConcurrency.Runs.Select(run => run.ResponseBytesWritten / (double)run.RequestCount));
        double allocatedBytesPerPair = Median(lowestConcurrency.Runs.Select(run => run.ManagedAllocatedBytes / (double)run.RequestCount));
        double compare = Median(lowestConcurrency.Runs.Select(run => run.ComparisonMilliseconds));
        double normalization = Median(lowestConcurrency.Runs.Select(run => run.NormalizationMilliseconds));
        double targetAllocation = fingerprint?.PerformanceEvidence.ManagedAllocatedBytesPerPair ?? TargetAllocatedBytesPerPair;
        double targetNormalization = fingerprint?.PerformanceEvidence.NormalizationMillisecondsPerPair ?? TargetNormalizationMillisecondsPerPair;
        return new ClientWorkloadFidelity(
            TargetResponseBytesPerPair,
            responseBytesPerPair,
            PercentDeviation(responseBytesPerPair, TargetResponseBytesPerPair),
            targetAllocation,
            allocatedBytesPerPair,
            PercentDeviation(allocatedBytesPerPair, targetAllocation),
            targetNormalization,
            normalization / lowestConcurrency.RequestCount,
            PercentDeviation(normalization / lowestConcurrency.RequestCount, targetNormalization),
            compare == 0 ? 0 : normalization * 100d / compare);
    }

    private static ClientStructuralFidelity BuildStructuralFidelity(
        IReadOnlyList<ClientFitnessChildReport> matrix,
        ClientFingerprintExport fingerprint)
    {
        ClientFitnessChildReport source = matrix.OrderBy(candidate => candidate.Candidate.ComparisonConcurrency).First();
        double count = source.RequestCount;
        ClientFitnessMeasurement run = source.Runs.OrderBy(item => item.NormalizationMilliseconds).ElementAt(source.Runs.Count / 2);
        double targetCount = Math.Max(1, fingerprint.PerformanceEvidence.RequestCount);
        Dictionary<string, double> deviations = new(StringComparer.Ordinal)
        {
            ["nodeCount"] = PercentDeviation(
                (run.ObjectNodeCount + run.PropertyNodeCount + run.CollectionNodeCount + run.ScalarNodeCount) / count,
                (fingerprint.Structure.ObjectNodeCount + fingerprint.Structure.PropertyNodeCount + fingerprint.Structure.CollectionNodeCount + fingerprint.Structure.ScalarNodeCount) / targetCount),
            ["collectionItems"] = PercentDeviation(run.CollectionItemCount / count, fingerprint.Structure.CollectionItemCount / targetCount),
            ["scalarBytes"] = PercentDeviation(run.ScalarUtf8Bytes / count, fingerprint.Structure.ScalarUtf8Bytes / targetCount),
            ["collisionGroups"] = PercentDeviation(run.SortCollisionGroupCount / count, fingerprint.Structure.SortCollisionGroupCount / targetCount),
            ["sortKeyBytes"] = PercentDeviation(run.SortKeyBytes / count, fingerprint.Structure.SortKeyBytes / targetCount),
        };
        return new ClientStructuralFidelity(deviations, deviations.Values.Max());
    }

    private static ClientFingerprintExport? ReadClientFingerprint()
    {
        string? path = Environment.GetEnvironmentVariable(FingerprintVariable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return JsonSerializer.Deserialize<ClientFingerprintExport>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"Client fingerprint '{path}' was empty.");
    }

    private static string ComputeOutputSha256(IEnumerable<RequestPairResult> details)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (RequestPairResult detail in details) AppendDetail(hash, detail);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<DetailAnalysis> AnalyzePersistedDetailsAsync(
        IRunDetailStore detailStore,
        RunDetailReference detailReference)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int offset = 0;
        int pairCount = 0;
        int differentPairCount = 0;
        int differenceCount = 0;
        int pairsWithoutDifferences = 0;
        HashSet<string> errors = new(StringComparer.Ordinal);
        while (true)
        {
            RunDetailPage page = await detailStore.LoadPageAsync(detailReference, new RunDetailQuery(offset, RunDetailQuery.MaxLimit));
            foreach (RequestPairResult detail in page.Items)
            {
                AppendDetail(hash, detail);
                pairCount++;
                if (detail.Outcome == RequestPairOutcome.Different) differentPairCount++;
                differenceCount += detail.Differences.Count;
                if (detail.Differences.Count == 0) pairsWithoutDifferences++;
                if (!string.IsNullOrWhiteSpace(detail.ErrorMessage)) errors.Add(detail.ErrorMessage);
            }
            if (!page.HasMore) break;
            offset += page.Items.Count;
        }

        return new DetailAnalysis(
            pairCount,
            differentPairCount,
            differenceCount,
            pairsWithoutDifferences,
            errors.ToArray(),
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void AppendDetail(IncrementalHash hash, RequestPairResult detail)
    {
        Append(hash, detail.RelativePath);
        Append(hash, ((int)detail.Outcome).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, detail.ResponseA?.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, detail.ResponseA?.Sha256);
        Append(hash, detail.ResponseB?.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, detail.ResponseB?.Sha256);
        Append(hash, detail.FocusedResponseA?.Sha256);
        Append(hash, detail.FocusedResponseB?.Sha256);
        foreach (string ignorePath in detail.FocusedRawContentIgnorePaths) Append(hash, ignorePath);
        Append(hash, JsonSerializer.Serialize(detail.ArtifactRetentionState));
        foreach (ComparisonDifference difference in detail.Differences)
        {
            Append(hash, difference.PropertyPath);
            Append(hash, difference.ValueA);
            Append(hash, difference.ValueB);
            Append(hash, difference.Message);
        }
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? "<null>");
        hash.AppendData(bytes);
        hash.AppendData([0x1e]);
    }

    private static void ForceFullCollection()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static void RequireEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {EnableVariable}=1 to run the real-plugin performance fitness test.");
        }
    }

    private static int ParsePositiveInt(string variable, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return int.TryParse(value, out int parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"{variable} must be a positive integer.");
    }

    private static int[] ParsePositiveIntList(string variable, int[] fallback)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(item => int.TryParse(item, out int parsed) && parsed > 0
                    ? parsed
                    : throw new InvalidOperationException($"{variable} values must be positive integers."))
                .Distinct()
                .ToArray();
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.OrderBy(value => value).ToArray();
        return ordered[ordered.Length / 2];
    }

    private static long MedianLong(IEnumerable<long> values)
    {
        long[] ordered = values.OrderBy(value => value).ToArray();
        return ordered[ordered.Length / 2];
    }

    private static double PercentDeviation(double actual, double target) =>
        target == 0 ? (actual == 0 ? 0 : 100) : Math.Abs(actual - target) * 100d / target;

    private static string PerformanceOutputDirectory() =>
        Environment.GetEnvironmentVariable(OutputVariable)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParityBench.NET", "Performance");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ComparisonTool.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string ResolvePluginPackageDirectory()
    {
        return Path.Combine(FindRepositoryRoot(), "Source", "ParityBench.ClientCustomerLookupPlugin", "bin", CurrentBuildConfiguration(), "net10.0");
    }

    private static string CurrentBuildConfiguration() =>
        Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;

    private const string SoapRequestBody =
        "<Envelope><Body><LookupRequest><UserName>fitness</UserName><Password>fitness</Password>" +
        "<CustomerId>C-FITNESS</CustomerId><CorrelationId>corr-fitness</CorrelationId>" +
        "</LookupRequest></Body></Envelope>";

    private const string SoapResponseBody =
        "<Envelope><Body><LookupResponse><StatusCode>OK</StatusCode><CustomerName>Fitness Applicant</CustomerName>" +
        "<TraceId>corr-fitness</TraceId></LookupResponse></Body></Envelope>";

    private sealed class FilteredRequestBatchStore(
        IRequestBatchStore source,
        RequestBatchManifest manifest) : IRequestBatchStore
    {
        public Task<RequestBatchManifest> LoadManifestAsync(RequestBatchReference batchReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<Stream> OpenRequestBodyAsync(RequestBatchReference batchReference, RequestItem request, CancellationToken cancellationToken = default) =>
            source.OpenRequestBodyAsync(manifest.BatchReference, request, cancellationToken);

        public Task<RequestBatchManifest> StageDirectoryAsync(string sourceDirectory, RequestBatchReference batchReference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Replay manifests are read-only.");

        public Task<RequestBatchManifest> StageFilesAsync(string sourceDirectory, IReadOnlyList<string> sourceFiles, RequestBatchReference batchReference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Replay manifests are read-only.");
    }

    private sealed class ReplayEndpointSender : IEndpointRequestSender
    {
        private readonly IRunArtifactStore artifacts;
        private readonly IReadOnlyDictionary<string, RequestPairResult> details;
        private readonly string? captureDirectory;

        public ReplayEndpointSender(IRunArtifactStore artifacts, IEnumerable<RequestPairResult> details, string? captureDirectory)
        {
            this.artifacts = artifacts;
            this.details = details.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
            this.captureDirectory = captureDirectory;
        }

        public async Task<EndpointResponse> SendAsync(EndpointRequest request, CancellationToken cancellationToken = default)
        {
            RequestPairResult pair = details[request.Request.RelativePath];
            ResponseArtifactMetadata response = request.Endpoint == EndpointSlot.A ? pair.ResponseA! : pair.ResponseB!;
            Stream body;
            if (await artifacts.ExistsAsync(response.Artifact, cancellationToken))
            {
                body = await artifacts.OpenReadAsync(response.Artifact, cancellationToken);
            }
            else if (captureDirectory is not null)
            {
                body = new FileStream(
                    CapturedArtifactPath(captureDirectory, request.Endpoint, request.Request.RelativePath),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81_920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            else
            {
                throw new FileNotFoundException($"No retained or captured artifact exists for '{request.Request.RelativePath}' endpoint {request.Endpoint}.");
            }
            return new EndpointResponse(response.StatusCode, response.ContentType, body);
        }
    }

    private sealed class ClientShapeEndpointSender : IEndpointRequestSender
    {
        private static readonly byte[] SoapResponse = Encoding.UTF8.GetBytes(SoapResponseBody);
        private readonly byte[][] jsonResponses = Enum.GetValues<ClientShapeVariant>()
            .Select(BuildClientJsonResponse)
            .ToArray();
        private int endpointAPairs;
        private int endpointBPairs;

        public int EndpointAPairs => Volatile.Read(ref endpointAPairs);
        public int EndpointBPairs => Volatile.Read(ref endpointBPairs);

        public Task<EndpointResponse> SendAsync(EndpointRequest request, CancellationToken cancellationToken = default)
        {
            byte[] body;
            string contentType;
            if (request.Endpoint == EndpointSlot.A)
            {
                Interlocked.Increment(ref endpointAPairs);
                body = SoapResponse;
                contentType = "text/xml";
            }
            else
            {
                if (!request.Headers.TryGetValue("Authorization", out string? authorization)
                    || authorization != "Bearer fitness-final-token"
                    || !request.Headers.TryGetValue(SubscriptionKeyHeader, out string? subscriptionKey)
                    || subscriptionKey != EndpointBSubscriptionKey)
                {
                    throw new InvalidOperationException("Client plugin request middleware did not attach endpoint B credentials.");
                }
                Interlocked.Increment(ref endpointBPairs);
                int requestIndex = ParseRequestIndex(request.Request.RelativePath);
                body = jsonResponses[requestIndex % jsonResponses.Length];
                contentType = "application/json";
            }

            return Task.FromResult(new EndpointResponse(200, contentType, new MemoryStream(body, writable: false)));
        }

        private static int ParseRequestIndex(string relativePath)
        {
            string name = Path.GetFileNameWithoutExtension(relativePath);
            int separator = name.LastIndexOf('-');
            return separator >= 0 && int.TryParse(name[(separator + 1)..], out int parsed) ? parsed : 0;
        }

        private static byte[] BuildClientJsonResponse(ClientShapeVariant variant)
        {
            (int applicantCount, int addressCount, int ruleCount, int outcomeCount, int checkCount) = variant switch
            {
                ClientShapeVariant.Wide => (256, 1, 1, 1, 1),
                _ => (64, 4, 4, 3, 4),
            };
            bool collisions = variant == ClientShapeVariant.CollisionHeavy;
            ClientJsonApplicant[] applicants = Enumerable.Range(0, applicantCount)
                .Select(applicant => new ClientJsonApplicant
                {
                    ApplicantId = collisions ? "duplicate-applicant" : applicant == 0 ? "corr-fitness" : $"app-{applicant:D4}",
                    Profile = new ClientJsonProfile
                    {
                        FullName = applicant == 0 ? "Fitness Applicant" : $"Applicant {applicant:D4} {new string((char)('a' + applicant % 26), 48)}",
                        Addresses = Enumerable.Range(0, addressCount).Reverse().Select(address => new ClientJsonAddress
                        {
                            Type = address % 2 == 0 ? "HOME" : "MAILING",
                            City = collisions ? $"Collision-City-{applicant % 4:D2}" : $"City-{applicant:D4}-{address:D2}",
                            Country = address % 3 == 0 ? "GB" : "US",
                        }).ToArray(),
                    },
                    RuleEvaluations = Enumerable.Range(0, ruleCount).Reverse().Select(rule => new ClientJsonRuleEvaluation
                    {
                        RuleSet = collisions ? "duplicate-rule" : $"rules-{rule:D2}",
                        Outcomes = Enumerable.Range(0, outcomeCount).Reverse().Select(outcome => new ClientJsonRuleOutcome
                        {
                            Code = collisions ? "duplicate-outcome" : $"R{rule:D2}-O{outcome:D2}",
                            Result = (applicant + rule + outcome) % 7 == 0 ? "REVIEW" : "PASS",
                            TriggeredChecks = Enumerable.Range(0, checkCount).Reverse()
                                .Select(check => collisions ? "duplicate-check" : $"check-{check:D2}-{applicant % 8:D2}")
                                .ToArray(),
                        }).ToArray(),
                    }).ToArray(),
                    Flags = new[] { $"SEGMENT-{applicant % 8:D2}", $"COHORT-{applicant % 16:D2}", applicant == 0 ? string.Empty : "ACTIVE" },
                })
                .ToArray();

            ClientJsonPayload payload = new()
            {
                Details = new ClientJsonDetails { ResultCode = "OK", TraceId = $"client-{variant}", DecisionEngine = "EndpointB" },
                Apps = applicants,
            };
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, PayloadJsonOptions);
            if (body.Length > EndpointBResponseBytes)
            {
                throw new InvalidOperationException($"Generated {variant} client response is {body.Length} bytes, above {EndpointBResponseBytes} byte target.");
            }

            // Keep target transport/canonical volume deterministic. Padding is a real
            // model value (a flag), so normalization and sort-key generation see it.
            int padding = EndpointBResponseBytes - body.Length;
            applicants[0].Flags[2] = new string('x', padding);
            body = JsonSerializer.SerializeToUtf8Bytes(payload, PayloadJsonOptions);
            if (body.Length != EndpointBResponseBytes)
            {
                throw new InvalidOperationException($"Generated {variant} client response must be {EndpointBResponseBytes} bytes; got {body.Length}.");
            }
            return body;
        }
    }

    private enum ClientShapeVariant
    {
        Deep,
        Wide,
        CollisionHeavy,
    }

    private sealed class ClientJsonPayload
    {
        public ClientJsonDetails Details { get; init; } = new();
        public ClientJsonApplicant[] Apps { get; init; } = [];
    }

    private sealed class ClientJsonDetails
    {
        public string ResultCode { get; init; } = string.Empty;
        public string TraceId { get; init; } = string.Empty;
        public string DecisionEngine { get; init; } = string.Empty;
    }

    private sealed class ClientJsonApplicant
    {
        public string ApplicantId { get; init; } = string.Empty;
        public ClientJsonProfile Profile { get; init; } = new();
        public ClientJsonRuleEvaluation[] RuleEvaluations { get; init; } = [];
        public string[] Flags { get; init; } = [];
    }

    private sealed class ClientJsonProfile
    {
        public string FullName { get; init; } = string.Empty;
        public ClientJsonAddress[] Addresses { get; init; } = [];
    }

    private sealed class ClientJsonAddress
    {
        public string Type { get; init; } = string.Empty;
        public string City { get; init; } = string.Empty;
        public string Country { get; init; } = string.Empty;
    }

    private sealed class ClientJsonRuleEvaluation
    {
        public string RuleSet { get; init; } = string.Empty;
        public ClientJsonRuleOutcome[] Outcomes { get; init; } = [];
    }

    private sealed class ClientJsonRuleOutcome
    {
        public string Code { get; init; } = string.Empty;
        public string Result { get; init; } = string.Empty;
        public string[] TriggeredChecks { get; init; } = [];
    }

    private sealed class TokenEndpointHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool primary = request.RequestUri!.AbsolutePath.EndsWith("primary", StringComparison.Ordinal);
            string expected = primary ? PrimarySubscriptionKey : FinalSubscriptionKey;
            bool authorized = request.Headers.TryGetValues(SubscriptionKeyHeader, out IEnumerable<string>? values)
                && values.Contains(expected, StringComparer.Ordinal);
            return Task.FromResult(authorized
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new Dictionary<string, string>
                    {
                        ["access_token"] = primary ? "fitness-primary-token" : "fitness-final-token",
                    }),
                }
                : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }

    private sealed class FitnessObservabilityRecorder : IObservabilityRecorder
    {
        public bool IsDurationLoggingEnabled => false;
        public bool IsExceptionLoggingEnabled => false;
        public bool IsDiagnosticsPersistenceEnabled => false;
        public bool IsDetailedCompareTimingEnabled => true;
        public TimeSpan SlowPathThreshold => TimeSpan.MaxValue;
        public void RecordRunPhase(RunId runId, string phaseName, TimeSpan duration) { }
        public void RecordRequestPath(RunId runId, string relativePath, TimeSpan duration) { }
        public void RecordException(RunId runId, string stage, Exception exception, string? relativePath = null, EndpointSlot? endpoint = null) { }
        public RunDiagnosticsSnapshot? CreateSnapshot(RunId runId) => null;
    }

    private sealed class NoOpProgressReporter : IRunProgressReporter
    {
        public static readonly NoOpProgressReporter Instance = new();
        public Task ReportAsync(RunStatus status, RunProgress progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions PayloadJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record ClientFitnessMeasurement(
        string Name,
        int RequestCount,
        int ComparisonConcurrency,
        double WallClockMilliseconds,
        double PairsPerSecond,
        long ResponseBytesWritten,
        long ArtifactBytesRead,
        double RequestExecutionMilliseconds,
        double ComparisonMilliseconds,
        double FinalizationMilliseconds,
        double NormalizationMilliseconds,
        double CompareNetObjectsMilliseconds,
        double MaterializationMilliseconds,
        double CanonicalMappingMilliseconds,
        double PluginMappingMilliseconds,
        double FocusedContentMilliseconds,
        double OtherCompareWorkerMilliseconds,
        double CompareQueueWaitMilliseconds,
        double ExecutionBackpressureMilliseconds,
        double ProcessCpuMilliseconds,
        double AverageProcessCoreUtilizationPercent,
        double AverageMachineCpuUtilizationPercent,
        long PeakWorkingSetBytes,
        long PeakPrivateBytes,
        long PrivateBytesBefore,
        long PrivateBytesAfter,
        long ManagedAllocatedBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        int RetainedArtifactCount,
        int TrimmedByPolicyArtifactCount,
        int MissingUnexpectedlyArtifactCount,
        int DifferentPairCount,
        int DifferenceCount,
        double NormalizationTraversalMilliseconds,
        double SortKeyConstructionMilliseconds,
        double CollectionSortMilliseconds,
        double LegacyFallbackMilliseconds,
        double RestorationMilliseconds,
        long ObjectNodeCount,
        long PropertyNodeCount,
        long CollectionNodeCount,
        long CollectionItemCount,
        long ScalarNodeCount,
        long ScalarUtf8Bytes,
        long SortKeyBytes,
        long MaximumSortKeyBytes,
        long SortCollisionGroupCount,
        double MappingWorkerMilliseconds,
        double ComparisonWorkerMilliseconds,
        double FocusedWorkerMilliseconds,
        double DetailPersistenceMilliseconds,
        double ExecuteToMappingQueueWaitMilliseconds,
        double MappingToComparisonQueueWaitMilliseconds,
        double ComparisonToFocusedQueueWaitMilliseconds,
        double ExecuteToMappingBackpressureMilliseconds,
        double MappingToComparisonBackpressureMilliseconds,
        double ComparisonToFocusedBackpressureMilliseconds,
        int MaximumExecuteToMappingDepth,
        int MaximumMappingToComparisonDepth,
        int MaximumComparisonToFocusedDepth,
        bool IsServerGc,
        bool? DynamicAdaptationEnabled,
        long MemoryBudgetBytes,
        string OutputSha256);

    private sealed record ClientFitnessChildReport(
        int RequestCount,
        FitnessTuple Candidate,
        long TotalAvailableMemoryBytes,
        IReadOnlyList<ClientFitnessMeasurement> Runs,
        bool ProcessExitedCleanly = false);

    private sealed record ClientFitnessCandidate(
        int MappingConcurrency,
        int ComparisonConcurrency,
        int FocusedContentConcurrency,
        WorkerGcMode WorkerGcMode,
        int? ServerGcHeapCount,
        double MedianPairsPerSecond,
        long MedianPeakPrivateBytes,
        long MedianPostRunPrivateBytes,
        long MemoryBudgetBytes,
        string? RejectionReason,
        long MedianManagedAllocatedBytes,
        double MedianNormalizationMilliseconds)
    {
        public FitnessTuple ToTuple() => new(
            MappingConcurrency,
            ComparisonConcurrency,
            FocusedContentConcurrency,
            WorkerGcMode,
            ServerGcHeapCount);
    }

    private sealed record FitnessGcCandidate(WorkerGcMode Mode, int? HeapCount);

    private sealed record FitnessTuple(
        int MappingConcurrency,
        int ComparisonConcurrency,
        int FocusedContentConcurrency,
        WorkerGcMode WorkerGcMode,
        int? ServerGcHeapCount)
    {
        public string Label => $"m{MappingConcurrency}-c{ComparisonConcurrency}-f{FocusedContentConcurrency}-{WorkerGcMode}{ServerGcHeapCount?.ToString() ?? string.Empty}";
    }

    private sealed record ClientWorkloadFidelity(
        double TargetResponseBytesPerPair,
        double ActualResponseBytesPerPair,
        double ResponseBytesDeviationPercent,
        double TargetAllocatedBytesPerPair,
        double ActualAllocatedBytesPerPair,
        double AllocationDeviationPercent,
        double TargetNormalizationMillisecondsPerPair,
        double ActualNormalizationMillisecondsPerPair,
        double NormalizationDeviationPercent,
        double NormalizationSharePercent);

    private sealed record ClientStructuralFidelity(
        IReadOnlyDictionary<string, double> DeviationPercentByMetric,
        double MaximumDeviationPercent);

    private sealed record ClientFingerprintExport(
        DateTimeOffset CreatedAt,
        ClientFingerprintStructure Structure,
        ClientFingerprintPerformanceEvidence PerformanceEvidence);

    private sealed record ClientFingerprintStructure(
        int SchemaVersion,
        long ObjectNodeCount,
        long PropertyNodeCount,
        long CollectionNodeCount,
        long CollectionItemCount,
        long ScalarNodeCount,
        long ScalarUtf8Bytes,
        long IgnoredNodeCount,
        long SortKeyBytes,
        long MaximumSortKeyBytes,
        long SortCollisionGroupCount,
        long MutableBranchCount,
        long LegacyFallbackBranchCount);

    private sealed record ClientFingerprintPerformanceEvidence(
        int RequestCount,
        double ManagedAllocatedBytesPerPair,
        double NormalizationMillisecondsPerPair);

    private sealed record ClientFitnessReport(
        DateTimeOffset CreatedAt,
        string Machine,
        int LogicalProcessors,
        IReadOnlyList<string> WorkloadVariants,
        int MatrixRequestCount,
        int Iterations,
        string OrderedOutputSha256,
        FitnessTuple? RecommendedConfiguration,
        ClientWorkloadFidelity WorkloadFidelity,
        ClientStructuralFidelity? StructuralFidelity,
        IReadOnlyList<string> Failures,
        IReadOnlyList<ClientFitnessCandidate> Candidates,
        IReadOnlyList<ClientFitnessChildReport> Matrix,
        IReadOnlyList<ClientFitnessChildReport> Scaling);

    private sealed record ClientReplayReport(
        DateTimeOffset CreatedAt,
        string SourceRunId,
        int PairCount,
        FitnessTuple Candidate,
        double WallClockMilliseconds,
        double PairsPerSecond,
        long ManagedAllocatedBytes,
        string SourceOutputSha256,
        string ReplayOutputSha256,
        RunExecutionMetrics ExecutionMetrics);

    private sealed record DetailAnalysis(
        int PairCount,
        int DifferentPairCount,
        int DifferenceCount,
        int PairsWithoutDifferences,
        IReadOnlyList<string> ErrorMessages,
        string OutputSha256);
}
