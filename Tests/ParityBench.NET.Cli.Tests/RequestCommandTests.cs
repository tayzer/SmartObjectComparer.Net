using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Cli;

namespace ParityBench.NET.Cli.Tests;

[TestClass]
public sealed class RequestCommandTests
{
    private readonly List<string> tempDirectories = new List<string>();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string tempDirectory in tempDirectories)
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Parse_WhenRequiredRequestOptionsAreProvided_ReturnsOptions()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "requests",
            "--endpoint-a",
            "https://a.example.test",
            "--endpoint-b",
            "https://b.example.test",
            "--concurrency",
            "3",
            "--timeout",
            "45",
            "--header",
            "X-Test: yes",
        });

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Options);
        Assert.AreEqual("requests", result.Options.RequestDirectory);
        Assert.AreEqual(new Uri("https://a.example.test"), result.Options.EndpointA);
        Assert.AreEqual(new Uri("https://b.example.test"), result.Options.EndpointB);
        Assert.AreEqual(3, result.Options.MaxConcurrency);
        Assert.AreEqual(TimeSpan.FromSeconds(45), result.Options.Timeout);
        CollectionAssert.Contains(result.Options.CommonHeaders.ToList(), "X-Test: yes");
    }

    [TestMethod]
    public void Parse_WhenObservabilityOptionsAreProvided_ReturnsOverrides()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "requests",
            "--endpoint-a",
            "https://a.example.test",
            "--endpoint-b",
            "https://b.example.test",
            "--log-level",
            "Debug",
            "--log-durations",
            "--log-exceptions",
            "--persist-diagnostics",
            "--slow-path-threshold-ms",
            "0",
        });

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Options);
        Assert.AreEqual(Microsoft.Extensions.Logging.LogLevel.Debug, result.Options.Observability.LogLevel);
        Assert.IsTrue(result.Options.Observability.LogDurations);
        Assert.IsTrue(result.Options.Observability.LogExceptions);
        Assert.IsTrue(result.Options.Observability.PersistDiagnostics);
        Assert.AreEqual(0, result.Options.Observability.SlowPathThresholdMs);
    }
    [TestMethod]
    public void Parse_WhenEndpointUrlIsInvalid_ReturnsValidationError()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "requests",
            "--endpoint-a",
            "not-a-url",
            "--endpoint-b",
            "https://b.example.test",
        });

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Errors.Any(error => error.Contains("--endpoint-a", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RunAsync_WhenRequestDirectoryIsMissing_ReturnsValidationFailure()
    {
        string workspaceRoot = CreateTempDirectory();
        using StringWriter output = new StringWriter();
        using StringWriter error = new StringWriter();

        int exitCode = await CliApplication.RunAsync(
            new[]
            {
                "request",
                Path.Combine(workspaceRoot, "missing"),
                "--endpoint-a",
                "https://a.example.test",
                "--endpoint-b",
                "https://b.example.test",
            },
            output,
            error,
            workspaceRoot).ConfigureAwait(false);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "Request directory was not found");
    }

    [TestMethod]
    public async Task RunAsync_WhenFakeEndpointResponsesMatch_PrintsSummaryCounts()
    {
        string workspaceRoot = CreateTempDirectory();
        string requestDirectory = CreateRequestDirectory("one.json", "{\"id\":1}");
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("{\"ok\":true}");
        using StringWriter output = new StringWriter();
        using StringWriter error = new StringWriter();

        int exitCode = await CliApplication.RunAsync(
            new[]
            {
                "request",
                requestDirectory,
                "--endpoint-a",
                "https://a.example.test",
                "--endpoint-b",
                "https://b.example.test",
            },
            output,
            error,
            workspaceRoot,
            services => services.AddSingleton<IEndpointRequestSender>(sender)).ConfigureAwait(false);

        Assert.AreEqual(0, exitCode, error.ToString());
        StringAssert.Contains(output.ToString(), "Status: Completed");
        StringAssert.Contains(output.ToString(), "Total: 1");
        StringAssert.Contains(output.ToString(), "Equal: 1");
        Assert.AreEqual(2, sender.RequestCount);
    }

    [TestMethod]
    public async Task RunAsync_WhenReportOutputIsProvided_WritesStaticReport()
    {
        string workspaceRoot = CreateTempDirectory();
        string requestDirectory = CreateRequestDirectory("one.json", "{\"id\":1}");
        string assetsDirectory = CreateReportAssetsDirectory();
        string reportOutput = Path.Combine(CreateTempDirectory(), "report");
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("{\"ok\":true}");
        using StringWriter output = new StringWriter();
        using StringWriter error = new StringWriter();

        int exitCode = await CliApplication.RunAsync(
            new[]
            {
                "request",
                requestDirectory,
                "--endpoint-a",
                "https://a.example.test",
                "--endpoint-b",
                "https://b.example.test",
                "--report-output",
                reportOutput,
                "--report-assets",
                assetsDirectory,
            },
            output,
            error,
            workspaceRoot,
            services => services.AddSingleton<IEndpointRequestSender>(sender)).ConfigureAwait(false);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.IsTrue(File.Exists(Path.Combine(reportOutput, "report.data.json")));
        Assert.IsTrue(Directory.Exists(Path.Combine(reportOutput, "details")));
        Assert.IsTrue(Directory.Exists(Path.Combine(reportOutput, "raw")));
        StringAssert.Contains(output.ToString(), "Report:");
    }

    private string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "ParityBenchCliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        tempDirectories.Add(tempDirectory);
        return tempDirectory;
    }

    private string CreateRequestDirectory(string fileName, string body)
    {
        string requestDirectory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(requestDirectory, fileName), body);
        return requestDirectory;
    }

    private string CreateReportAssetsDirectory()
    {
        string assetsDirectory = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(assetsDirectory, "_framework"));
        File.WriteAllText(Path.Combine(assetsDirectory, "index.html"), "<!doctype html><html><body>report</body></html>");
        File.WriteAllText(Path.Combine(assetsDirectory, "_framework", "placeholder.txt"), "asset");
        return assetsDirectory;
    }

    private sealed class FakeEndpointRequestSender : IEndpointRequestSender
    {
        private readonly string body;

        private FakeEndpointRequestSender(string body)
        {
            this.body = body;
        }

        public int RequestCount { get; private set; }

        public static FakeEndpointRequestSender ForBody(string body) => new FakeEndpointRequestSender(body);

        public Task<EndpointResponse> SendAsync(
            EndpointRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            return Task.FromResult(new EndpointResponse(
                200,
                "application/json",
                new MemoryStream(bytes)));
        }
    }
}