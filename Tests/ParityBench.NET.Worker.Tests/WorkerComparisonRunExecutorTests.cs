using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Reflection;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Composition;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Infrastructure.Worker;

namespace ParityBench.NET.Worker.Tests;

/// <summary>
/// End-to-end tests that launch the real worker executable, proving the host and
/// worker agree on the pipe protocol and that a worker-side failure is contained
/// as a failed run rather than taking the host process down.
/// </summary>
[TestClass]
public sealed class WorkerComparisonRunExecutorTests
{
    [TestMethod]
    public void WorkerGcConfiguration_MapsEveryExplicitModeToProcessStartupVariables()
    {
        AssertGcEnvironment(new LargeRunOptions(workerGcMode: WorkerGcMode.Workstation), "0", null, null);
        AssertGcEnvironment(new LargeRunOptions(workerGcMode: WorkerGcMode.ServerAdaptive), "1", "1", null);
        AssertGcEnvironment(new LargeRunOptions(workerGcMode: WorkerGcMode.ServerFixed, serverGcHeapCount: 12), "1", "0", "c");
    }

    [TestMethod]
    public void WorkerObservabilityConfiguration_ForwardsHostSettingsToWorkerEnvironment()
    {
        ProcessStartInfo startInfo = new();
        ObservabilityOptions options = new()
        {
            LogDurations = true,
            LogExceptions = false,
            PersistDiagnostics = true,
            SlowPathThresholdMs = 123,
            MaxSlowPathEntries = 17,
            MaxExceptionEntries = 19,
            EnableDetailedCompareTiming = true,
            EnableStructuralFingerprintExport = true,
            StructuralFingerprintOutputDirectory = @"C:\calibration",
            CaptureNextRunForCalibration = true,
            CalibrationCaptureOutputDirectory = @"C:\private-sample",
        };
        MethodInfo method = typeof(WorkerComparisonRunExecutor).GetMethod(
            "ApplyObservabilityConfiguration",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        method.Invoke(null, [startInfo, options]);

        Assert.AreEqual("True", startInfo.Environment["ParityBench__Observability__LogDurations"]);
        Assert.AreEqual("False", startInfo.Environment["ParityBench__Observability__LogExceptions"]);
        Assert.AreEqual("True", startInfo.Environment["ParityBench__Observability__EnableDetailedCompareTiming"]);
        Assert.AreEqual("True", startInfo.Environment["ParityBench__Observability__EnableStructuralFingerprintExport"]);
        Assert.AreEqual(@"C:\calibration", startInfo.Environment["ParityBench__Observability__StructuralFingerprintOutputDirectory"]);
        Assert.AreEqual("True", startInfo.Environment["ParityBench__Observability__CaptureNextRunForCalibration"]);
        Assert.AreEqual(@"C:\private-sample", startInfo.Environment["ParityBench__Observability__CalibrationCaptureOutputDirectory"]);
    }

    private static void AssertGcEnvironment(
        LargeRunOptions options,
        string expectedServer,
        string? expectedDatas,
        string? expectedHeapCount)
    {
        ProcessStartInfo startInfo = new();
        MethodInfo method = typeof(WorkerComparisonRunExecutor).GetMethod(
            "ApplyGcConfiguration",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        method.Invoke(null, [startInfo, options]);

        Assert.AreEqual(expectedServer, startInfo.Environment["DOTNET_gcServer"]);
        startInfo.Environment.TryGetValue("DOTNET_GCDynamicAdaptationMode", out string? actualDatas);
        startInfo.Environment.TryGetValue("DOTNET_GCHeapCount", out string? actualHeapCount);
        Assert.AreEqual(expectedDatas, actualDatas);
        Assert.AreEqual(expectedHeapCount, actualHeapCount);
    }
    private static readonly string WorkerExecutablePath = ResolveWorkerExecutablePath();

    [TestMethod]
    public async Task ExecuteAsync_WhenRunHasNoRequests_ReturnsSummaryAndReportsProgress()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        await using ServiceProvider provider = BuildProvider(workspace.Path);
        ComparisonRun run = await CreateEmptyRunAsync(provider, workspace, pluginComparison: null);

        RecordingProgressReporter reporter = new RecordingProgressReporter();
        WorkerComparisonRunExecutor executor = CreateExecutor(workspace.Path);

        RunResultSummary summary = await executor.ExecuteAsync(run, reporter);

        Assert.AreEqual(0, summary.TotalPairs);
        // The host process is obviously still alive to make these assertions, which
        // is the point: work happened in another process.
        Assert.IsTrue(reporter.Reports.Count > 0, "Expected the worker to stream progress frames.");
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenRunSelectsAMissingPlugin_FailsTheRunWithoutCrashingTheHost()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        await using ServiceProvider provider = BuildProvider(workspace.Path);
        ComparisonRun run = await CreateEmptyRunAsync(
            provider,
            workspace,
            new PluginComparisonSelection("missing.plugin", "missing.comparison"));

        WorkerComparisonRunExecutor executor = CreateExecutor(workspace.Path);

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(run, new RecordingProgressReporter()));

        StringAssert.Contains(exception.Message, "is not installed");
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenWorkerExecutableIsMissing_SurfacesAStartupFailure()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        await using ServiceProvider provider = BuildProvider(workspace.Path);
        ComparisonRun run = await CreateEmptyRunAsync(provider, workspace, pluginComparison: null);

        WorkerComparisonRunExecutor executor = new WorkerComparisonRunExecutor(Options.Create(new WorkerExecutionOptions
        {
            WorkspaceRoot = workspace.Path,
            FixtureBaseUrl = "http://localhost",
            WorkerExecutablePath = Path.Combine(workspace.Path, "does-not-exist.dll"),
            CancellationGracePeriod = TimeSpan.FromSeconds(2),
        }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(run, new RecordingProgressReporter()));
    }

    private static WorkerComparisonRunExecutor CreateExecutor(string workspaceRoot) =>
        new WorkerComparisonRunExecutor(Options.Create(new WorkerExecutionOptions
        {
            WorkspaceRoot = workspaceRoot,
            FixtureBaseUrl = "http://localhost",
            WorkerExecutablePath = WorkerExecutablePath,
            CancellationGracePeriod = TimeSpan.FromSeconds(5),
        }));

    private static ServiceProvider BuildProvider(string workspaceRoot)
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddParityBenchWorkspaceServices(configuration, workspaceRoot, "http://localhost");
        return services.BuildServiceProvider();
    }

    private static async Task<ComparisonRun> CreateEmptyRunAsync(
        IServiceProvider provider,
        TempWorkspace workspace,
        PluginComparisonSelection? pluginComparison)
    {
        IRequestBatchStore batchStore = provider.GetRequiredService<IRequestBatchStore>();
        RequestBatchReference batchReference = new RequestBatchReference(Guid.NewGuid().ToString("n"));
        string emptyRequestDirectory = Path.Combine(workspace.Path, "requests");
        Directory.CreateDirectory(emptyRequestDirectory);
        await batchStore.StageDirectoryAsync(emptyRequestDirectory, batchReference);

        RunOptions options = new RunOptions(
            batchReference,
            new EndpointDefinition(new Uri("https://a.example.test")),
            new EndpointDefinition(new Uri("https://b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            pluginComparison: pluginComparison);

        return await provider.GetRequiredService<IComparisonRunUseCases>().CreateRunAsync(options);
    }

    private static string ResolveWorkerExecutablePath()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ComparisonTool.sln")))
        {
            directory = directory.Parent;
        }

        string repositoryRoot = directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");

        string configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;
        return Path.Combine(repositoryRoot, "Source", "ParityBench.NET.Worker", "bin", configuration, "net10.0", "ParityBench.NET.Worker.dll");
    }

    private sealed class RecordingProgressReporter : IRunProgressReporter
    {
        public List<(RunStatus Status, RunProgress Progress)> Reports { get; } = new List<(RunStatus, RunProgress)>();

        public Task ReportAsync(RunStatus status, RunProgress progress, CancellationToken cancellationToken = default)
        {
            lock (Reports)
            {
                Reports.Add((status, progress));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string path) => Path = path;

        public string Path { get; }

        public static TempWorkspace Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "paritybench-worker", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(path);
            return new TempWorkspace(path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
