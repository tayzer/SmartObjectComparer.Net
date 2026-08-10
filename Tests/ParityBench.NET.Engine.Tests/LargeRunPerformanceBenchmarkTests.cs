using System.Diagnostics;
using System.Text.Json;
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

    private static async Task<BenchmarkMeasurement> RunOnceAsync(string root, int count, string name)
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
            new PayloadSender(),
            artifactStore,
            detailStore,
            new CompareNetObjectsResponseComparer(artifactStore, new JsonXmlResponseBodyDeserializer(registry)),
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
                comparisonOptions: new ComparisonOptions(ignoreStringCase: true),
                largeRunOptions: new LargeRunOptions(comparisonConcurrency: ParseComparisonConcurrency())))
            .Start();

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        Stopwatch stopwatch = Stopwatch.StartNew();
        RunResultSummary summary = await executor.ExecuteAsync(run, NoOpProgressReporter.Instance);
        stopwatch.Stop();
        RunExecutionMetrics metrics = summary.ExecutionMetrics!;
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
            metrics.ResponseBytesWritten,
            metrics.ComparisonConcurrency,
            GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore,
            Process.GetCurrentProcess().WorkingSet64);
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
        ParsePositiveInt(ComparisonConcurrencyVariable, Math.Min(8, Environment.ProcessorCount));

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

    private sealed class PayloadSender : IEndpointRequestSender
    {
        private static readonly byte[] EqualBody = BuildBody(different: false);
        private static readonly byte[] DifferentBody = BuildBody(different: true);

        public Task<EndpointResponse> SendAsync(EndpointRequest request, CancellationToken cancellationToken = default)
        {
            bool produceDifferences = string.Equals(
                Environment.GetEnvironmentVariable(DifferentResponsesVariable),
                "1",
                StringComparison.Ordinal);
            byte[] body = produceDifferences && request.Endpoint == EndpointSlot.B ? DifferentBody : EqualBody;
            return Task.FromResult(new EndpointResponse(200, "application/json", new MemoryStream(body, writable: false)));
        }

        private static byte[] BuildBody(bool different) => JsonSerializer.SerializeToUtf8Bytes(new PayloadEnvelope
        {
            Id = 1,
            Payload = "stable",
            Items = Enumerable.Range(0, 256)
                .Select(index => new PayloadRecord
                {
                    Index = index,
                    Code = $"record-{index % 32:D2}",
                    Amount = index * 1.25m + (different ? 1 : 0),
                    Description = new string((char)('a' + (different ? (index + 1) : index) % 26), 600),
                    Tags = [$"tag-{index % 8}", $"group-{index % 16}"],
                    Attributes = new Dictionary<string, string>
                    {
                        ["source"] = "performance",
                        ["partition"] = (index % 4).ToString(),
                    },
                })
                .ToArray(),
        });
    }

    public sealed class PayloadEnvelope
    {
        public int Id { get; set; }
        public string? Payload { get; set; }
        public PayloadRecord[]? Items { get; set; }
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
        long ResponseBytesWritten,
        int ComparisonWorkers,
        long TotalAllocatedBytes,
        long WorkingSetBytes);
    private sealed record BenchmarkReport(DateTimeOffset CreatedAt, string Machine, int LogicalProcessors, int PayloadBytes, IReadOnlyList<BenchmarkMeasurement> SmallRuns, IReadOnlyList<BenchmarkMeasurement> LargeRuns, double SmallMedianPairsPerSecond, double LargeMedianPairsPerSecond, double ThroughputRatio);
}
