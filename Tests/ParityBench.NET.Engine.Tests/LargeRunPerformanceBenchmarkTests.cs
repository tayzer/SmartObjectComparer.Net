using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
            await RunOnceAsync(root, 250, "warmup");
            List<BenchmarkMeasurement> small = new();
            List<BenchmarkMeasurement> large = new();
            for (int iteration = 1; iteration <= 3; iteration++)
            {
                small.Add(await RunOnceAsync(root, 2500, $"small-{iteration}"));
                large.Add(await RunOnceAsync(root, 8000, $"large-{iteration}"));
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
        ComparisonRunExecutor executor = new(
            batchStore,
            new PayloadSender(),
            artifactStore,
            detailStore,
            new CompareNetObjectsResponseComparer(artifactStore, new JsonXmlResponseBodyDeserializer(registry)));
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
                largeRunOptions: new LargeRunOptions(comparisonConcurrency: Math.Min(8, Environment.ProcessorCount))))
            .Start();

        Stopwatch stopwatch = Stopwatch.StartNew();
        RunResultSummary summary = await executor.ExecuteAsync(run, NoOpProgressReporter.Instance);
        stopwatch.Stop();
        return new BenchmarkMeasurement(name, count, stopwatch.Elapsed.TotalMilliseconds,
            count / stopwatch.Elapsed.TotalSeconds, summary.ExecutionMetrics!.ResponseBytesWritten,
            summary.ExecutionMetrics.ComparisonConcurrency);
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.OrderBy(value => value).ToArray();
        return ordered[ordered.Length / 2];
    }

    private sealed class PayloadSender : IEndpointRequestSender
    {
        private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"id\":1,\"payload\":\"" + new string('x', PayloadBytes - 32) + "\"}");
        public Task<EndpointResponse> SendAsync(EndpointRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EndpointResponse(200, "application/json", new MemoryStream(Body, writable: false)));
    }

    public sealed class PayloadEnvelope { public int Id { get; set; } public string? Payload { get; set; } }
    private sealed class NoOpProgressReporter : ParityBench.NET.Application.Runs.IRunProgressReporter
    {
        public static readonly NoOpProgressReporter Instance = new();
        public Task ReportAsync(RunStatus status, RunProgress progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record BenchmarkMeasurement(string Name, int RequestCount, double WallClockMilliseconds, double PairsPerSecond, long ResponseBytesWritten, int ComparisonWorkers);
    private sealed record BenchmarkReport(DateTimeOffset CreatedAt, string Machine, int LogicalProcessors, int PayloadBytes, IReadOnlyList<BenchmarkMeasurement> SmallRuns, IReadOnlyList<BenchmarkMeasurement> LargeRuns, double SmallMedianPairsPerSecond, double LargeMedianPairsPerSecond, double ThroughputRatio);
}
