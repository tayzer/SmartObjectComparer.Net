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
    private const string IterationsVariable = "PB_CLIENT_PLUGIN_FITNESS_ITERATIONS";
    private const string ConcurrenciesVariable = "PB_CLIENT_PLUGIN_FITNESS_CONCURRENCIES";
    private const string CountsVariable = "PB_CLIENT_PLUGIN_FITNESS_COUNTS";
    private const string OutputVariable = "PB_PERFORMANCE_OUTPUT";

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
            List<ClientFitnessChildReport> matrix = new();
            foreach (int concurrency in concurrencies)
            {
                matrix.Add(await LaunchChildAsync(matrixCount, concurrency, iterations, childRoot));
            }

            string expectedHash = matrix[0].Runs[0].OutputSha256;
            List<string> failures = new();
            if (matrix.SelectMany(candidate => candidate.Runs).Any(run => run.OutputSha256 != expectedHash))
            {
                failures.Add("Ordered differences changed across concurrency candidates.");
            }

            long availableMemory = matrix.Min(candidate => candidate.TotalAvailableMemoryBytes);
            List<ClientFitnessCandidate> candidates = matrix
                .Select(candidate => Summarize(candidate, availableMemory))
                .ToList();
            ClientFitnessCandidate? recommendation = SelectRecommendation(candidates);
            if (recommendation is null)
            {
                failures.Add("No concurrency candidate passed the 50% available-memory gate.");
            }

            List<ClientFitnessChildReport> scaling = new();
            if (recommendation is not null)
            {
                foreach (int count in scalingCounts)
                {
                    scaling.Add(await LaunchChildAsync(count, recommendation.ComparisonConcurrency, iterations, childRoot));
                }

                double smallThroughput = Median(scaling[0].Runs.Select(run => run.PairsPerSecond));
                double largeThroughput = Median(scaling[1].Runs.Select(run => run.PairsPerSecond));
                if (largeThroughput < smallThroughput * .80d)
                {
                    failures.Add($"8k throughput {largeThroughput:F2} pairs/s is below 80% of 2.5k throughput {smallThroughput:F2} pairs/s.");
                }
            }

            ClientWorkloadFidelity fidelity = BuildFidelity(matrix);
            if (fidelity.ResponseBytesDeviationPercent > 10d)
            {
                failures.Add($"Generated response bytes/pair differ from the client fingerprint by {fidelity.ResponseBytesDeviationPercent:F1}%.");
            }
            if (fidelity.AllocatedBytesPercentOfClientBaseline > 30d)
            {
                failures.Add($"Managed allocation is {fidelity.AllocatedBytesPercentOfClientBaseline:F1}% of the client baseline; target is at most 30%.");
            }
            if (fidelity.NormalizationMillisecondsPercentOfClientBaseline > 25d)
            {
                failures.Add($"Normalization is {fidelity.NormalizationMillisecondsPercentOfClientBaseline:F1}% of the client baseline; target is at most 25%.");
            }

            ClientFitnessReport report = new(
                DateTimeOffset.UtcNow,
                Environment.MachineName,
                Environment.ProcessorCount,
                Enum.GetNames<ClientShapeVariant>(),
                matrixCount,
                iterations,
                expectedHash,
                recommendation?.ComparisonConcurrency,
                fidelity,
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
        int iterations = ParsePositiveInt(IterationsVariable, 3);
        string output = Environment.GetEnvironmentVariable(ChildOutputVariable)
            ?? throw new InvalidOperationException($"{ChildOutputVariable} is required in child mode.");

        await RunOnceAsync(Math.Min(10, count), concurrency, "warmup");
        List<ClientFitnessMeasurement> runs = new();
        for (int iteration = 1; iteration <= iterations; iteration++)
        {
            runs.Add(await RunOnceAsync(count, concurrency, $"c{concurrency}-{count}-{iteration}"));
        }

        ClientFitnessChildReport report = new(
            count,
            concurrency,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            runs);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, JsonOptions));
    }

    private static async Task<ClientFitnessMeasurement> RunOnceAsync(int count, int comparisonConcurrency, string name)
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
                    largeRunOptions: new LargeRunOptions(comparisonConcurrency: comparisonConcurrency),
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
            IReadOnlyList<RequestPairResult> details = await detailStore.LoadDetailsAsync(summary.DetailIndexReference!);
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            DetailedCompareMetrics? detailed = metrics.DetailedCompareMetrics;

            Assert.AreEqual(count, summary.TotalPairs);
            Assert.AreEqual(
                0,
                summary.ErrorPairs,
                string.Join(Environment.NewLine, details.Where(detail => detail.Outcome == RequestPairOutcome.ExecutionFailed).Select(detail => detail.ErrorMessage).Distinct()));
            Assert.AreEqual(count, details.Count);
            Assert.AreEqual(count, summary.DifferentPairs, "Every generated pair should retain real surviving differences.");
            Assert.IsTrue(details.All(detail => detail.Differences.Count > 0), "No generated pair may lose all surviving differences.");
            Assert.AreEqual(count, sender.EndpointAPairs);
            Assert.AreEqual(count, sender.EndpointBPairs);

            return new ClientFitnessMeasurement(
                name,
                count,
                comparisonConcurrency,
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
                details.Count(detail => detail.Outcome == RequestPairOutcome.Different),
                details.Sum(detail => detail.Differences.Count),
                ComputeOutputSha256(details));
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
        int concurrency,
        int iterations,
        string childRoot)
    {
        string outputPath = Path.Combine(childRoot, $"count-{count}-c{concurrency}.json");
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
        startInfo.Environment[ConcurrencyVariable] = concurrency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment[IterationsVariable] = iterations.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start client fitness child process.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = await stdout;
        string error = await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Client fitness child count={count}, c={concurrency} failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }

        return JsonSerializer.Deserialize<ClientFitnessChildReport>(await File.ReadAllTextAsync(outputPath), JsonOptions)
            ?? throw new InvalidOperationException($"Client fitness child report '{outputPath}' was empty.");
    }

    private static ClientFitnessCandidate Summarize(ClientFitnessChildReport report, long totalAvailableMemory)
    {
        long peakPrivate = MedianLong(report.Runs.Select(run => run.PeakPrivateBytes));
        return new ClientFitnessCandidate(
            report.ComparisonConcurrency,
            Median(report.Runs.Select(run => run.PairsPerSecond)),
            peakPrivate,
            totalAvailableMemory == 0 ? 0 : peakPrivate * 100d / totalAvailableMemory,
            MedianLong(report.Runs.Select(run => run.ManagedAllocatedBytes)),
            Median(report.Runs.Select(run => run.NormalizationMilliseconds)));
    }

    private static ClientFitnessCandidate? SelectRecommendation(IReadOnlyList<ClientFitnessCandidate> candidates)
    {
        ClientFitnessCandidate[] eligible = candidates.Where(candidate => candidate.AvailableMemoryPercent <= 50d).ToArray();
        if (eligible.Length == 0) return null;
        double best = eligible.Max(candidate => candidate.MedianPairsPerSecond);
        return eligible
            .Where(candidate => candidate.MedianPairsPerSecond >= best * .95d)
            .OrderBy(candidate => candidate.MedianPeakPrivateBytes)
            .ThenBy(candidate => candidate.ComparisonConcurrency)
            .First();
    }

    private static ClientWorkloadFidelity BuildFidelity(IReadOnlyList<ClientFitnessChildReport> matrix)
    {
        ClientFitnessChildReport lowestConcurrency = matrix.OrderBy(candidate => candidate.ComparisonConcurrency).First();
        double responseBytesPerPair = Median(lowestConcurrency.Runs.Select(run => run.ResponseBytesWritten / (double)run.RequestCount));
        double allocatedBytesPerPair = Median(lowestConcurrency.Runs.Select(run => run.ManagedAllocatedBytes / (double)run.RequestCount));
        double compare = Median(lowestConcurrency.Runs.Select(run => run.ComparisonMilliseconds));
        double normalization = Median(lowestConcurrency.Runs.Select(run => run.NormalizationMilliseconds));
        return new ClientWorkloadFidelity(
            TargetResponseBytesPerPair,
            responseBytesPerPair,
            PercentDeviation(responseBytesPerPair, TargetResponseBytesPerPair),
            TargetAllocatedBytesPerPair,
            allocatedBytesPerPair,
            allocatedBytesPerPair * 100d / TargetAllocatedBytesPerPair,
            TargetNormalizationMillisecondsPerPair,
            normalization / lowestConcurrency.RequestCount,
            normalization * 100d / lowestConcurrency.RequestCount / TargetNormalizationMillisecondsPerPair,
            compare == 0 ? 0 : normalization * 100d / compare);
    }

    private static string ComputeOutputSha256(IEnumerable<RequestPairResult> details)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (RequestPairResult detail in details)
        {
            Append(hash, detail.RelativePath);
            Append(hash, ((int)detail.Outcome).ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (ComparisonDifference difference in detail.Differences)
            {
                Append(hash, difference.PropertyPath);
                Append(hash, difference.ValueA);
                Append(hash, difference.ValueB);
                Append(hash, difference.Message);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
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

    private static double PercentDeviation(double actual, double target) => Math.Abs(actual - target) * 100d / target;

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
        string OutputSha256);

    private sealed record ClientFitnessChildReport(
        int RequestCount,
        int ComparisonConcurrency,
        long TotalAvailableMemoryBytes,
        IReadOnlyList<ClientFitnessMeasurement> Runs);

    private sealed record ClientFitnessCandidate(
        int ComparisonConcurrency,
        double MedianPairsPerSecond,
        long MedianPeakPrivateBytes,
        double AvailableMemoryPercent,
        long MedianManagedAllocatedBytes,
        double MedianNormalizationMilliseconds);

    private sealed record ClientWorkloadFidelity(
        double TargetResponseBytesPerPair,
        double ActualResponseBytesPerPair,
        double ResponseBytesDeviationPercent,
        double TargetAllocatedBytesPerPair,
        double ActualAllocatedBytesPerPair,
        double AllocatedBytesPercentOfClientBaseline,
        double TargetNormalizationMillisecondsPerPair,
        double ActualNormalizationMillisecondsPerPair,
        double NormalizationMillisecondsPercentOfClientBaseline,
        double NormalizationSharePercent);

    private sealed record ClientFitnessReport(
        DateTimeOffset CreatedAt,
        string Machine,
        int LogicalProcessors,
        IReadOnlyList<string> WorkloadVariants,
        int MatrixRequestCount,
        int Iterations,
        string OrderedOutputSha256,
        int? RecommendedComparisonConcurrency,
        ClientWorkloadFidelity WorkloadFidelity,
        IReadOnlyList<string> Failures,
        IReadOnlyList<ClientFitnessCandidate> Candidates,
        IReadOnlyList<ClientFitnessChildReport> Matrix,
        IReadOnlyList<ClientFitnessChildReport> Scaling);
}
