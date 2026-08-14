using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
[TestCategory("Performance")]
public sealed class LargeRunPerformanceBenchmarkTests
{
    private const string EnableVariable = "PB_RUN_PERFORMANCE_BENCHMARKS";
    private const string CountsVariable = "PB_PERFORMANCE_COUNTS";
    private const string IterationsVariable = "PB_PERFORMANCE_ITERATIONS";
    private const string ComparisonConcurrencyVariable = "PB_PERFORMANCE_COMPARISON_CONCURRENCY";
    private const string DifferentResponsesVariable = "PB_PERFORMANCE_DIFFERENT_RESPONSES";
    private const string MatrixConcurrenciesVariable = "PB_PERFORMANCE_CONCURRENCIES";
    private const string CalibrationChildVariable = "PB_PERFORMANCE_CALIBRATION_CHILD";
    private const string CalibrationConcurrencyVariable = "PB_PERFORMANCE_CALIBRATION_CONCURRENCY";
    private const string CalibrationOutputFileVariable = "PB_PERFORMANCE_CALIBRATION_OUTPUT_FILE";
    private const string BaselineReportVariable = "PB_PERFORMANCE_BASELINE_REPORT";
    private const int PayloadBytes = 192 * 1024;

    [TestMethod]
    public async Task ExecuteAsync_2k5And8k_ReportsStableThroughput()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {EnableVariable}=1 to run this multi-gigabyte performance benchmark.");
        }

        string root = Path.Combine(Path.GetTempPath(), "ParityBenchNET.Performance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            int[] counts = ParseCounts();
            int iterations = ParsePositiveInt(IterationsVariable, 3);
            await RunOnceAsync(root, Math.Min(250, counts[0]), "warmup");
            List<BenchmarkMeasurement> small = new();
            List<BenchmarkMeasurement> large = new();
            for (int iteration = 1; iteration <= iterations; iteration++)
            {
                small.Add(await RunOnceAsync(root, counts[0], $"small-{iteration}"));
                large.Add(await RunOnceAsync(root, counts[1], $"large-{iteration}"));
            }

            double smallMedian = Median(small.Select(item => item.PairsPerSecond));
            double largeMedian = Median(large.Select(item => item.PairsPerSecond));
            BenchmarkReport report = new(
                DateTimeOffset.UtcNow,
                Environment.MachineName,
                Environment.ProcessorCount,
                PayloadBytes,
                small,
                large,
                smallMedian,
                largeMedian,
                largeMedian / smallMedian);
            string outputDirectory = Environment.GetEnvironmentVariable("PB_PERFORMANCE_OUTPUT")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParityBench.NET", "Performance");
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, $"large-run-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

            Assert.IsTrue(largeMedian >= smallMedian * .80d,
                $"8k throughput {largeMedian:F2} pairs/s is below 80% of 2.5k throughput {smallMedian:F2} pairs/s. Report: {outputPath}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_1kSyntheticShape_ReportsLegacyAndOptimizedConcurrencyMatrix()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {EnableVariable}=1 to run this performance benchmark.");
        }

        string root = Path.Combine(Path.GetTempPath(), "ParityBenchNET.Performance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            int count = ParsePositiveInt("PB_PERFORMANCE_MATRIX_COUNT", 1000);
            int iterations = ParsePositiveInt(IterationsVariable, 3);
            int[] concurrencies = ParseConcurrencies();
            await RunOnceAsync(root, Math.Min(50, count), "matrix-warmup-legacy", concurrencies[0], useLegacyNormalizer: true);
            await RunOnceAsync(root, Math.Min(50, count), "matrix-warmup-optimized", concurrencies[0], useLegacyNormalizer: false);

            List<ComparisonBenchmarkMeasurement> matrix = new();
            foreach (int concurrency in concurrencies)
            {
                List<BenchmarkMeasurement> legacy = new();
                List<BenchmarkMeasurement> optimized = new();
                for (int iteration = 1; iteration <= iterations; iteration++)
                {
                    legacy.Add(await RunOnceAsync(root, count, $"matrix-c{concurrency}-legacy-{iteration}", concurrency, useLegacyNormalizer: true));
                    optimized.Add(await RunOnceAsync(root, count, $"matrix-c{concurrency}-optimized-{iteration}", concurrency, useLegacyNormalizer: false));
                }

                matrix.Add(new ComparisonBenchmarkMeasurement(concurrency, legacy, optimized));
            }

            List<BenchmarkMeasurement> noRulesLegacy = new();
            List<BenchmarkMeasurement> noRulesOptimized = new();
            await RunOnceAsync(root, Math.Min(50, count), "no-rules-warmup-legacy", concurrencies[0], useLegacyNormalizer: true, useComparisonRules: false, produceDifferences: false);
            await RunOnceAsync(root, Math.Min(50, count), "no-rules-warmup-optimized", concurrencies[0], useLegacyNormalizer: false, useComparisonRules: false, produceDifferences: false);
            for (int iteration = 1; iteration <= iterations; iteration++)
            {
                noRulesLegacy.Add(await RunOnceAsync(root, count, $"no-rules-legacy-{iteration}", concurrencies[0], useLegacyNormalizer: true, useComparisonRules: false, produceDifferences: false));
                noRulesOptimized.Add(await RunOnceAsync(root, count, $"no-rules-optimized-{iteration}", concurrencies[0], useLegacyNormalizer: false, useComparisonRules: false, produceDifferences: false));
            }

            string outputDirectory = Environment.GetEnvironmentVariable("PB_PERFORMANCE_OUTPUT")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParityBench.NET", "Performance");
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, $"compare-hot-path-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
            HotPathBenchmarkReport report = new(matrix, noRulesLegacy, noRulesOptimized);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

            foreach (ComparisonBenchmarkMeasurement measurement in matrix)
            {
                CollectionAssert.AreEqual(
                    measurement.Legacy.Select(run => run.OutputSha256).ToArray(),
                    measurement.Optimized.Select(run => run.OutputSha256).ToArray(),
                    $"Ordered comparison output changed at concurrency {measurement.ComparisonConcurrency}. Report: {outputPath}");
            }

            ComparisonBenchmarkMeasurement best = matrix.OrderByDescending(item => Median(item.Optimized.Select(run => run.PairsPerSecond))).First();
            double legacyThroughput = Median(best.Legacy.Select(run => run.PairsPerSecond));
            double optimizedThroughput = Median(best.Optimized.Select(run => run.PairsPerSecond));
            double legacyNormalization = Median(best.Legacy.Select(run => run.ComparisonModelNormalizationMilliseconds));
            double optimizedNormalization = Median(best.Optimized.Select(run => run.ComparisonModelNormalizationMilliseconds));
            double legacyAllocated = Median(best.Legacy.Select(run => (double)run.ManagedAllocatedBytes));
            double optimizedAllocated = Median(best.Optimized.Select(run => (double)run.ManagedAllocatedBytes));

            Assert.IsTrue(optimizedThroughput >= legacyThroughput * 2d, $"Optimized throughput must be at least 2x legacy. Report: {outputPath}");
            Assert.IsTrue(optimizedNormalization <= legacyNormalization * .25d, $"Normalization must fall by at least 75%. Report: {outputPath}");
            Assert.IsTrue(optimizedAllocated <= legacyAllocated * .30d, $"Managed allocations must fall by at least 70%. Report: {outputPath}");
            double noRulesLegacyThroughput = Median(noRulesLegacy.Select(run => run.PairsPerSecond));
            double noRulesOptimizedThroughput = Median(noRulesOptimized.Select(run => run.PairsPerSecond));
            Assert.IsTrue(noRulesOptimizedThroughput >= noRulesLegacyThroughput * .95d,
                $"No-rules hash fast path regressed by more than 5%. Report: {outputPath}");
            CollectionAssert.AreEqual(
                noRulesLegacy.Select(run => run.OutputSha256).ToArray(),
                noRulesOptimized.Select(run => run.OutputSha256).ToArray(),
                $"No-rules output changed. Report: {outputPath}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_1kSyntheticShape_CalibratesComparisonConcurrencyInIsolatedProcesses()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {EnableVariable}=1 to run this performance benchmark.");
        }

        if (string.Equals(Environment.GetEnvironmentVariable(CalibrationChildVariable), "1", StringComparison.Ordinal))
        {
            await RunCalibrationChildAsync();
            return;
        }

        int count = ParsePositiveInt("PB_PERFORMANCE_MATRIX_COUNT", 1000);
        int iterations = ParsePositiveInt(IterationsVariable, 3);
        int[] concurrencies = ParseConcurrencies(defaultValues: [8, 12, 16, 20]);
        string outputDirectory = PerformanceOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        string childDirectory = Path.Combine(Path.GetTempPath(), "ParityBenchNET.Performance.Calibration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(childDirectory);
        try
        {
            List<CalibrationChildReport> children = new();
            foreach (int concurrency in concurrencies)
            {
                string childOutput = Path.Combine(childDirectory, $"c{concurrency}.json");
                await LaunchCalibrationChildAsync(concurrency, count, iterations, childOutput);
                CalibrationChildReport? child = JsonSerializer.Deserialize<CalibrationChildReport>(await File.ReadAllTextAsync(childOutput));
                Assert.IsNotNull(child, $"Calibration child {concurrency} did not produce a readable report.");
                children.Add(child);
            }

            string outputHash = children.SelectMany(child => child.Runs).First().OutputSha256;
            List<string> validationFailures = new();
            if (!children.SelectMany(child => child.Runs).All(run => run.OutputSha256 == outputHash))
            {
                validationFailures.Add("Ordered comparison output changed across concurrency candidates.");
            }

            long totalAvailableMemoryBytes = children.Min(child => child.TotalAvailableMemoryBytes);
            List<CalibrationCandidate> candidates = children
                .Select(child => SummarizeCandidate(child, totalAvailableMemoryBytes))
                .ToList();
            CalibrationCandidate? recommendation = SelectRecommendation(candidates);
            List<CalibrationCandidate> annotated = candidates
                .Select(candidate => candidate with { Decision = DescribeDecision(candidate, candidates, recommendation) })
                .ToList();
            BenchmarkBaseline? baseline = ReadBaseline(Environment.GetEnvironmentVariable(BaselineReportVariable));

            if (baseline is not null)
            {
                CalibrationCandidate eightWorkers = annotated.Single(candidate => candidate.ComparisonConcurrency == 8);
                if (!string.Equals(baseline.OutputSha256, outputHash, StringComparison.Ordinal))
                {
                    validationFailures.Add("Post-fix output differs from pre-fix baseline.");
                }
                if (eightWorkers.MedianPairsPerSecond < baseline.PairsPerSecond * .95d)
                {
                    validationFailures.Add($"Eight-worker throughput regressed by more than 5%: {eightWorkers.MedianPairsPerSecond:F2} vs {baseline.PairsPerSecond:F2} pairs/s.");
                }
                if (eightWorkers.MedianPeakPrivateBytes > baseline.PeakPrivateBytes)
                {
                    validationFailures.Add($"Eight-worker peak private bytes increased: {eightWorkers.MedianPeakPrivateBytes} vs {baseline.PeakPrivateBytes}.");
                }
                if (eightWorkers.MedianPostRunWorkingSetBytes > baseline.PostRunWorkingSetBytes)
                {
                    validationFailures.Add($"Eight-worker post-run working set increased: {eightWorkers.MedianPostRunWorkingSetBytes} vs {baseline.PostRunWorkingSetBytes}.");
                }
            }

            if (recommendation is null)
            {
                validationFailures.Add("No concurrency candidate passed the 50% available-memory gate.");
            }

            CalibrationReport report = new(
                DateTimeOffset.UtcNow,
                Environment.MachineName,
                Environment.ProcessorCount,
                PayloadBytes,
                count,
                iterations,
                totalAvailableMemoryBytes,
                .50d,
                .95d,
                outputHash,
                recommendation?.ComparisonConcurrency,
                baseline,
                validationFailures,
                annotated,
                children);
            string outputPath = Path.Combine(outputDirectory, $"comparison-calibration-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

            Assert.AreEqual(0, validationFailures.Count, $"Calibration acceptance failed: {string.Join(" ", validationFailures)} Report: {outputPath}");
        }
        finally
        {
            if (Directory.Exists(childDirectory)) Directory.Delete(childDirectory, recursive: true);
        }
    }

    private static async Task RunCalibrationChildAsync()
    {
        int concurrency = ParsePositiveInt(CalibrationConcurrencyVariable, 8);
        int count = ParsePositiveInt("PB_PERFORMANCE_MATRIX_COUNT", 1000);
        int iterations = ParsePositiveInt(IterationsVariable, 3);
        string outputFile = Environment.GetEnvironmentVariable(CalibrationOutputFileVariable)
            ?? throw new InvalidOperationException($"{CalibrationOutputFileVariable} is required in calibration child mode.");
        string root = Path.Combine(Path.GetTempPath(), "ParityBenchNET.Performance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await RunOnceAsync(root, Math.Min(50, count), $"calibration-c{concurrency}-warmup", concurrency, useLegacyNormalizer: false, produceDifferences: true);
            List<BenchmarkMeasurement> runs = new();
            for (int iteration = 1; iteration <= iterations; iteration++)
            {
                runs.Add(await RunOnceAsync(root, count, $"calibration-c{concurrency}-{iteration}", concurrency, useLegacyNormalizer: false, produceDifferences: true));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            CalibrationChildReport report = new(
                concurrency,
                GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
                runs);
            await File.WriteAllTextAsync(outputFile, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task LaunchCalibrationChildAsync(int concurrency, int count, int iterations, string outputFile)
    {
        string repositoryRoot = FindRepositoryRoot();
        string project = Path.Combine(repositoryRoot, "Tests", "ParityBench.NET.Engine.Tests", "ParityBench.NET.Engine.Tests.csproj");
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = Path.Combine(repositoryRoot, "Tests"),
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
        startInfo.ArgumentList.Add("FullyQualifiedName~ExecuteAsync_1kSyntheticShape_CalibratesComparisonConcurrencyInIsolatedProcesses");
        startInfo.Environment[EnableVariable] = "1";
        startInfo.Environment[CalibrationChildVariable] = "1";
        startInfo.Environment[CalibrationConcurrencyVariable] = concurrency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment[CalibrationOutputFileVariable] = outputFile;
        startInfo.Environment["PB_PERFORMANCE_MATRIX_COUNT"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment[IterationsVariable] = iterations.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment[DifferentResponsesVariable] = "1";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start isolated calibration process.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = await standardOutput;
        string error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Calibration child c{concurrency} failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tests", "ParityBench.NET.Engine.Tests", "ParityBench.NET.Engine.Tests.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate ComparisonTool repository root.");
    }

    private static async Task<BenchmarkMeasurement> RunOnceAsync(
        string root,
        int count,
        string name,
        int? comparisonConcurrency = null,
        bool useLegacyNormalizer = false,
        bool useComparisonRules = true,
        bool? produceDifferences = null)
    {
        string workspace = Path.Combine(root, name, "workspace");
        string source = Path.Combine(root, name, "source");
        Directory.CreateDirectory(source);
        for (int index = 0; index < count; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(source, $"request-{index:D5}.json"), "{}");
        }

        FileSystemRequestBatchStore batchStore = new(workspace);
        RequestBatchManifest manifest = await batchStore.StageDirectoryAsync(source, new RequestBatchReference($"batch-{name}"));
        FileSystemRunArtifactStore artifactStore = new(workspace);
        FileSystemRunDetailStore detailStore = new(workspace);
        ResponseModelRegistry registry = new();
        registry.Register<PayloadEnvelope>("PayloadEnvelope");
        BenchmarkObservabilityRecorder observabilityRecorder = new();
        ComparisonRunExecutor executor = new(
            batchStore,
            new PayloadSender(produceDifferences),
            artifactStore,
            detailStore,
            new CompareNetObjectsResponseComparer(artifactStore, new JsonXmlResponseBodyDeserializer(registry), useLegacyNormalizer),
            null,
            observabilityRecorder);
        ComparisonRun run = ComparisonRun.Create(
            new RunId($"run-{name}"),
            new RunOptions(
                manifest.BatchReference,
                new EndpointDefinition(new Uri("https://a.performance.test")),
                new EndpointDefinition(new Uri("https://b.performance.test")),
                TimeSpan.FromSeconds(30),
                maxConcurrency: 16,
                responseModelName: "PayloadEnvelope",
                comparisonOptions: useComparisonRules
                    ? new ComparisonOptions(
                        ignoreCollectionOrder: true,
                        ignoreStringCase: true,
                        maxDifferences: 25,
                        ignoreRules: [new IgnoreRuleDefinition("Items[*].Amount")],
                        smartIgnoreRules: [new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "Description")])
                    : new ComparisonOptions(),
                largeRunOptions: new LargeRunOptions(comparisonConcurrency: comparisonConcurrency ?? ParseComparisonConcurrency())))
            .Start();

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        Stopwatch stopwatch = Stopwatch.StartNew();
        RunResultSummary summary = await executor.ExecuteAsync(run, NoOpProgressReporter.Instance);
        stopwatch.Stop();
        RunExecutionMetrics metrics = summary.ExecutionMetrics!;
        IReadOnlyList<RequestPairResult> details = await detailStore.LoadDetailsAsync(summary.DetailIndexReference!);
        string outputSha256 = ComputeOutputSha256(details);
        return new BenchmarkMeasurement(
            name,
            count,
            stopwatch.Elapsed.TotalMilliseconds,
            count / stopwatch.Elapsed.TotalSeconds,
            metrics.RequestExecutionDuration.TotalMilliseconds,
            metrics.ComparisonDuration.TotalMilliseconds,
            metrics.FinalizationDuration.TotalMilliseconds,
            metrics.CompareSubPhases?.NormalizeDuration.TotalMilliseconds ?? 0,
            metrics.CompareSubPhases?.PersistCanonicalDuration.TotalMilliseconds ?? 0,
            metrics.CompareSubPhases?.DiffDuration.TotalMilliseconds ?? 0,
            metrics.CompareSubPhases?.FocusedContentDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.ArtifactBytesRead ?? 0,
            metrics.DetailedCompareMetrics?.ArtifactOpenDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.ResponseDeserializationDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.ComparisonModelNormalizationDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.CompareNetObjectsTraversalDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.DifferenceMaterializationDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.CanonicalMappingDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.PluginMappingDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.PluginPairProcessingDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.OtherCompareWorkerDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.CompareQueueWaitDuration.TotalMilliseconds ?? 0,
            metrics.DetailedCompareMetrics?.ExecutionWorkerBackpressureDuration.TotalMilliseconds ?? 0,
            metrics.ProcessResourceMetrics?.ProcessCpuDuration.TotalMilliseconds ?? 0,
            metrics.ProcessResourceMetrics?.AverageMachineCpuUtilizationPercent ?? 0,
            metrics.ProcessResourceMetrics?.PeakWorkingSetBytes ?? 0,
            metrics.ProcessResourceMetrics?.PeakPrivateBytes ?? 0,
            metrics.ProcessResourceMetrics?.ManagedAllocatedBytes ?? 0,
            metrics.ProcessResourceMetrics?.Gen0CollectionCount ?? 0,
            metrics.ProcessResourceMetrics?.Gen1CollectionCount ?? 0,
            metrics.ProcessResourceMetrics?.Gen2CollectionCount ?? 0,
            metrics.ResponseBytesWritten,
            metrics.ComparisonConcurrency,
            GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore,
            Process.GetCurrentProcess().WorkingSet64,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            details.Count(detail => detail.Outcome == RequestPairOutcome.Different),
            details.Sum(detail => detail.Differences.Count),
            outputSha256);
    }

    private static string ComputeOutputSha256(IEnumerable<RequestPairResult> details)
    {
        StringBuilder canonical = new();
        foreach (RequestPairResult detail in details)
        {
            canonical.Append(detail.RelativePath).Append('\u001f').Append((int)detail.Outcome).Append('\u001e');
            foreach (ComparisonDifference difference in detail.Differences)
            {
                canonical.Append(difference.PropertyPath).Append('\u001f')
                    .Append(difference.ValueA).Append('\u001f')
                    .Append(difference.ValueB).Append('\u001f')
                    .Append(difference.Message).Append('\u001e');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.OrderBy(value => value).ToArray();
        return ordered[ordered.Length / 2];
    }

    private static int[] ParseCounts()
    {
        string? value = Environment.GetEnvironmentVariable(CountsVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [2500, 8000];
        }

        int[] counts = value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select((item, index) =>
            {
                if (!int.TryParse(item, out int count) || count <= 0)
                {
                    throw new InvalidOperationException($"{CountsVariable} item {index + 1} must be a positive integer.");
                }

                return count;
            })
            .ToArray();
        if (counts.Length != 2 || counts[1] < counts[0])
        {
            throw new InvalidOperationException($"{CountsVariable} must contain two ascending counts, for example '2500,8000'.");
        }

        return counts;
    }

    private static int ParseComparisonConcurrency() =>
        ParsePositiveInt(ComparisonConcurrencyVariable, Math.Min(20, Environment.ProcessorCount));

    private static int[] ParseConcurrencies(int[]? defaultValues = null)
    {
        string? configured = Environment.GetEnvironmentVariable(MatrixConcurrenciesVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? defaultValues ?? [4, 8, 12, 20]
            : configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, out int parsed) && parsed > 0 ? parsed : throw new InvalidOperationException($"{MatrixConcurrenciesVariable} values must be positive integers."))
                .Distinct()
                .ToArray();
    }

    private static int ParsePositiveInt(string variable, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, out int parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"{variable} must be a positive integer.");
        }

        return parsed;
    }

    private static string PerformanceOutputDirectory() =>
        Environment.GetEnvironmentVariable("PB_PERFORMANCE_OUTPUT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParityBench.NET", "Performance");

    private static string CurrentBuildConfiguration() =>
        Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;

    private static CalibrationCandidate SummarizeCandidate(CalibrationChildReport child, long totalAvailableMemoryBytes)
    {
        long medianPeakPrivateBytes = MedianLong(child.Runs.Select(run => run.PeakPrivateBytes));
        return new CalibrationCandidate(
            child.ComparisonConcurrency,
            Median(child.Runs.Select(run => run.PairsPerSecond)),
            Median(child.Runs.Select(run => run.WallClockMilliseconds)),
            Median(child.Runs.Select(run => run.ComparisonModelNormalizationMilliseconds)),
            Median(child.Runs.Select(run => run.CompareNetObjectsTraversalMilliseconds)),
            Median(child.Runs.Select(run => run.DifferenceMaterializationMilliseconds)),
            Median(child.Runs.Select(run => run.CompareFocusedContentMilliseconds)),
            Median(child.Runs.Select(run => run.OtherCompareWorkerMilliseconds)),
            Median(child.Runs.Select(run => run.CompareQueueWaitMilliseconds)),
            Median(child.Runs.Select(run => run.ExecutionBackpressureMilliseconds)),
            MedianLong(child.Runs.Select(run => run.ManagedAllocatedBytes)),
            MedianLong(child.Runs.Select(run => run.PeakWorkingSetBytes)),
            medianPeakPrivateBytes,
            MedianLong(child.Runs.Select(run => run.WorkingSetBytes)),
            totalAvailableMemoryBytes == 0 ? 0 : medianPeakPrivateBytes * 100d / totalAvailableMemoryBytes,
            string.Empty);
    }

    private static CalibrationCandidate? SelectRecommendation(IReadOnlyList<CalibrationCandidate> candidates)
    {
        CalibrationCandidate[] eligible = candidates.Where(candidate => candidate.AvailableMemoryPercent <= 50d).ToArray();
        if (eligible.Length == 0)
        {
            return null;
        }

        double bestThroughput = eligible.Max(candidate => candidate.MedianPairsPerSecond);
        return eligible
            .Where(candidate => candidate.MedianPairsPerSecond >= bestThroughput * .95d)
            .OrderBy(candidate => candidate.MedianPeakPrivateBytes)
            .ThenBy(candidate => candidate.ComparisonConcurrency)
            .First();
    }

    private static string DescribeDecision(
        CalibrationCandidate candidate,
        IReadOnlyList<CalibrationCandidate> candidates,
        CalibrationCandidate? recommendation)
    {
        if (recommendation is not null && candidate.ComparisonConcurrency == recommendation.ComparisonConcurrency)
        {
            return "Selected: lowest median peak-private memory within 5% of best eligible throughput.";
        }

        if (candidate.AvailableMemoryPercent > 50d)
        {
            return $"Rejected: median peak private memory is {candidate.AvailableMemoryPercent:F1}% of available memory, above 50%.";
        }

        double bestEligibleThroughput = candidates
            .Where(item => item.AvailableMemoryPercent <= 50d)
            .Select(item => item.MedianPairsPerSecond)
            .DefaultIfEmpty(0)
            .Max();
        if (candidate.MedianPairsPerSecond < bestEligibleThroughput * .95d)
        {
            return "Rejected: median throughput is more than 5% below best eligible throughput.";
        }

        return "Not selected: within throughput band but uses more peak-private memory than recommendation.";
    }

    private static BenchmarkBaseline? ReadBaseline(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement optimized = document.RootElement
            .GetProperty("Matrix")
            .EnumerateArray()
            .Single(item => item.GetProperty("ComparisonConcurrency").GetInt32() == 8)
            .GetProperty("Optimized")
            .EnumerateArray()
            .First();
        return new BenchmarkBaseline(
            path,
            optimized.GetProperty("PairsPerSecond").GetDouble(),
            optimized.GetProperty("PeakPrivateBytes").GetInt64(),
            optimized.GetProperty("WorkingSetBytes").GetInt64(),
            optimized.GetProperty("OutputSha256").GetString()!);
    }

    private static long MedianLong(IEnumerable<long> values)
    {
        long[] ordered = values.OrderBy(value => value).ToArray();
        return ordered[ordered.Length / 2];
    }

    private sealed class PayloadSender(bool? produceDifferencesOverride = null) : IEndpointRequestSender
    {
        private static readonly byte[] EqualBody = BuildBody(different: false);
        private static readonly byte[] DifferentBody = BuildBody(different: true);

        public Task<EndpointResponse> SendAsync(EndpointRequest request, CancellationToken cancellationToken = default)
        {
            bool produceDifferences = produceDifferencesOverride ?? string.Equals(
                Environment.GetEnvironmentVariable(DifferentResponsesVariable), "1", StringComparison.Ordinal);
            byte[] body = produceDifferences && request.Endpoint == EndpointSlot.B ? DifferentBody : EqualBody;
            return Task.FromResult(new EndpointResponse(200, "application/json", new MemoryStream(body, writable: false)));
        }

        private static byte[] BuildBody(bool different)
        {
            PayloadEnvelope payload = new()
            {
                Id = 1,
                Payload = different ? "changed" : "stable",
                Items = (different ? Enumerable.Range(0, 1024).Reverse() : Enumerable.Range(0, 1024))
                .Select(index => new PayloadRecord
                {
                    Index = index,
                    Code = different && index < 512
                        ? $"changed-{index:D4}"
                        : $"record-{index % 32:D2}",
                    Amount = index * 1.25m + (different ? 1 : 0),
                    Description = new string((char)('a' + (different ? (index + 1) : index) % 26), 32),
                    Tags = [$"tag-{index % 8}", $"group-{index % 16}"],
                    Attributes = new Dictionary<string, string>
                    {
                        ["source"] = "performance",
                        ["partition"] = (index % 4).ToString(),
                    },
                })
                .ToArray(),
            };
            byte[] unpadded = JsonSerializer.SerializeToUtf8Bytes(payload);
            int paddingLength = PayloadBytes - unpadded.Length;
            if (paddingLength < 0)
            {
                throw new InvalidOperationException($"Synthetic payload is {unpadded.Length} bytes, exceeding {PayloadBytes} byte target.");
            }

            payload.Padding = new string('x', paddingLength);
            byte[] padded = JsonSerializer.SerializeToUtf8Bytes(payload);
            if (padded.Length != PayloadBytes)
            {
                throw new InvalidOperationException($"Synthetic payload must be exactly {PayloadBytes} bytes; produced {padded.Length}.");
            }

            return padded;
        }
    }

    public sealed class PayloadEnvelope
    {
        public int Id { get; set; }
        public string? Payload { get; set; }
        public PayloadRecord[]? Items { get; set; }
        public string? Padding { get; set; } = string.Empty;

        [JsonIgnore]
        public int ItemCount => Items?.Length ?? 0;
    }

    public sealed class PayloadRecord
    {
        public int Index { get; set; }
        public string? Code { get; set; }
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public string[]? Tags { get; set; }
        public Dictionary<string, string>? Attributes { get; set; }
    }
    private sealed class NoOpProgressReporter : ParityBench.NET.Application.Runs.IRunProgressReporter
    {
        public static readonly NoOpProgressReporter Instance = new();
        public Task ReportAsync(RunStatus status, RunProgress progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BenchmarkObservabilityRecorder : IObservabilityRecorder
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

    private sealed record BenchmarkMeasurement(
        string Name,
        int RequestCount,
        double WallClockMilliseconds,
        double PairsPerSecond,
        double RequestExecutionMilliseconds,
        double ComparisonMilliseconds,
        double FinalizationMilliseconds,
        double CompareNormalizeMilliseconds,
        double ComparePersistCanonicalMilliseconds,
        double CompareDiffMilliseconds,
        double CompareFocusedContentMilliseconds,
        long ArtifactBytesRead,
        double ArtifactOpenMilliseconds,
        double ResponseDeserializationMilliseconds,
        double ComparisonModelNormalizationMilliseconds,
        double CompareNetObjectsTraversalMilliseconds,
        double DifferenceMaterializationMilliseconds,
        double CanonicalMappingMilliseconds,
        double PluginMappingMilliseconds,
        double PluginPairProcessingMilliseconds,
        double OtherCompareWorkerMilliseconds,
        double CompareQueueWaitMilliseconds,
        double ExecutionBackpressureMilliseconds,
        double ProcessCpuMilliseconds,
        double ProcessMachineCpuPercent,
        long PeakWorkingSetBytes,
        long PeakPrivateBytes,
        long ManagedAllocatedBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        long ResponseBytesWritten,
        int ComparisonWorkers,
        long TotalAllocatedBytes,
        long WorkingSetBytes,
        long TotalAvailableMemoryBytes,
        int DifferentPairCount,
        int DifferenceCount,
        string OutputSha256);
    private sealed record BenchmarkReport(DateTimeOffset CreatedAt, string Machine, int LogicalProcessors, int PayloadBytes, IReadOnlyList<BenchmarkMeasurement> SmallRuns, IReadOnlyList<BenchmarkMeasurement> LargeRuns, double SmallMedianPairsPerSecond, double LargeMedianPairsPerSecond, double ThroughputRatio);
    private sealed record ComparisonBenchmarkMeasurement(int ComparisonConcurrency, IReadOnlyList<BenchmarkMeasurement> Legacy, IReadOnlyList<BenchmarkMeasurement> Optimized);
    private sealed record HotPathBenchmarkReport(
        IReadOnlyList<ComparisonBenchmarkMeasurement> Matrix,
        IReadOnlyList<BenchmarkMeasurement> NoRulesLegacy,
        IReadOnlyList<BenchmarkMeasurement> NoRulesOptimized);
    private sealed record CalibrationChildReport(
        int ComparisonConcurrency,
        long TotalAvailableMemoryBytes,
        IReadOnlyList<BenchmarkMeasurement> Runs);
    private sealed record CalibrationCandidate(
        int ComparisonConcurrency,
        double MedianPairsPerSecond,
        double MedianWallClockMilliseconds,
        double MedianNormalizationMilliseconds,
        double MedianCompareNetObjectsMilliseconds,
        double MedianMaterializationMilliseconds,
        double MedianFocusedContentMilliseconds,
        double MedianOtherCompareWorkerMilliseconds,
        double MedianQueueWaitMilliseconds,
        double MedianBackpressureMilliseconds,
        long MedianManagedAllocatedBytes,
        long MedianPeakWorkingSetBytes,
        long MedianPeakPrivateBytes,
        long MedianPostRunWorkingSetBytes,
        double AvailableMemoryPercent,
        string Decision);
    private sealed record BenchmarkBaseline(
        string ReportPath,
        double PairsPerSecond,
        long PeakPrivateBytes,
        long PostRunWorkingSetBytes,
        string OutputSha256);
    private sealed record CalibrationReport(
        DateTimeOffset CreatedAt,
        string Machine,
        int LogicalProcessors,
        int PayloadBytes,
        int RequestCount,
        int Iterations,
        long TotalAvailableMemoryBytes,
        double MemoryLimitRatio,
        double ThroughputBandRatio,
        string OutputSha256,
        int? RecommendedComparisonConcurrency,
        BenchmarkBaseline? Baseline,
        IReadOnlyList<string> ValidationFailures,
        IReadOnlyList<CalibrationCandidate> Candidates,
        IReadOnlyList<CalibrationChildReport> RawResults);
}
