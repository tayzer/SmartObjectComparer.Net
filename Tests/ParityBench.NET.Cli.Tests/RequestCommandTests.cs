using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Cli;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Workspaces;

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
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A loaded plugin's assemblies stay locked by their load context, so
                // a leftover temp directory is not worth failing the test over.
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
    public void Parse_WhenPresetIsProvidedWithoutDirectoryOrEndpoints_ReturnsOptions()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "--preset",
            "client-soap-json-token",
        });

        Assert.IsTrue(result.IsSuccess, string.Join(",", result.Errors));
        Assert.IsNotNull(result.Options);
        Assert.AreEqual("client-soap-json-token", result.Options.PresetId);
        Assert.IsNull(result.Options.RequestDirectory);
        Assert.IsNull(result.Options.EndpointA);
        Assert.IsNull(result.Options.EndpointB);
    }

    [TestMethod]
    public void Parse_WhenRunProfileIsProvidedWithoutDirectoryOrEndpoints_ReturnsOptions()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "--run-profile",
            "client-customer-lookup-local",
        });

        Assert.IsTrue(result.IsSuccess, string.Join(",", result.Errors));
        Assert.AreEqual("client-customer-lookup-local", result.Options!.RunProfileId);
        Assert.IsNull(result.Options.RequestDirectory);
    }

    [TestMethod]
    public void Parse_WhenBothPresetAndRunProfileAreProvided_ReturnsValidationError()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "--preset",
            "a",
            "--run-profile",
            "b",
        });

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Errors.Any(error => error.Contains("only one of --preset or --run-profile", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Parse_WhenNeitherPresetNorEndpointsAreProvided_ReturnsValidationError()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
        });

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Errors.Any(error => error.Contains("Request directory", StringComparison.Ordinal)));
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
    public async Task RunAsync_WhenPresetSuppliesDirectoryAndEndpoints_ResolvesFromPreset()
    {
        string workspaceRoot = CreateTempDirectory();
        string requestDirectory = CreateRequestDirectory("one.json", "{\"id\":1}");
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("{\"ok\":true}");
        InMemoryRequestComparisonPresetRegistry stubPresetRegistry = new InMemoryRequestComparisonPresetRegistry();
        stubPresetRegistry.Register(new RequestComparisonPresetOption(
            "test-preset",
            "Test preset",
            requestDirectory,
            new Uri("https://a.example.test"),
            new Uri("https://b.example.test"),
            "Auto",
            null,
            new ComparisonOptions(),
            new RequestExecutionOptions()));
        using StringWriter output = new StringWriter();
        using StringWriter error = new StringWriter();

        int exitCode = await CliApplication.RunAsync(
            new[]
            {
                "request",
                "--preset",
                "test-preset",
            },
            output,
            error,
            workspaceRoot,
            services =>
            {
                services.AddSingleton<IEndpointRequestSender>(sender);
                services.AddSingleton<IRequestComparisonPresetRegistry>(stubPresetRegistry);
            }).ConfigureAwait(false);

        Assert.AreEqual(0, exitCode, error.ToString());
        StringAssert.Contains(output.ToString(), "Status: Completed");
        StringAssert.Contains(output.ToString(), "Total: 1");
        Assert.AreEqual(2, sender.RequestCount);
    }

    [TestMethod]
    public async Task RunAsync_WhenPresetIdIsUnknown_ReturnsValidationFailure()
    {
        string workspaceRoot = CreateTempDirectory();
        using StringWriter output = new StringWriter();
        using StringWriter error = new StringWriter();

        int exitCode = await CliApplication.RunAsync(
            new[]
            {
                "request",
                "--preset",
                "does-not-exist",
            },
            output,
            error,
            workspaceRoot).ConfigureAwait(false);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "Preset 'does-not-exist' was not found");
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

    [TestMethod]
    public async Task RunAsync_WhenRunProfileSelectsTheReferencePlugin_LoadsItAndCompletesTheRun()
    {
        string workspaceRoot = CreateTempDirectory();
        InstallReferencePlugin(workspaceRoot);
        string requestDirectory = CreateRequestDirectory("one.xml", SoapRequestBody);
        await SaveReferenceProfileAsync(workspaceRoot, requestDirectory);

        SlotAwareEndpointSender sender = new SlotAwareEndpointSender();
        HttpClient tokenClient = new HttpClient(new TokenHandler());
        using StringWriter output = new StringWriter();
        using StringWriter error = new StringWriter();

        int exitCode = await CliApplication.RunAsync(
            new[] { "request", "--run-profile", "client-customer-lookup-local" },
            output,
            error,
            workspaceRoot,
            services =>
            {
                services.AddSingleton<IEndpointRequestSender>(sender);
                // Last registration wins, so the plugin's token exchange uses this
                // controlled client instead of reaching the network.
                services.AddSingleton(tokenClient);
            }).ConfigureAwait(false);

        Assert.AreEqual(0, exitCode, error.ToString());
        StringAssert.Contains(output.ToString(), "Status: Completed");
        StringAssert.Contains(output.ToString(), "Total: 1");
        // Endpoint B got the exchanged bearer token, proving the plugin's request
        // middleware ran inside the run the profile drove.
        Assert.AreEqual("Bearer mock-final-token", sender.EndpointBAuthorization);
    }

    private static async Task SaveReferenceProfileAsync(string workspaceRoot, string requestDirectory)
    {
        FileSystemRunProfileStore store = new FileSystemRunProfileStore(workspaceRoot);
        await store.SaveAsync(new RunProfile(
            "client-customer-lookup-local",
            "Client Customer Lookup — Local",
            "client.customer-lookup",
            "client.customer-lookup.soap-vs-json",
            new Uri("http://a.endpoint.test/soap"),
            new Uri("http://b.endpoint.test/json"),
            requestDirectory: requestDirectory,
            stepConfiguration: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["client.customer-lookup.request"] = new Dictionary<string, string>
                {
                    ["primaryTokenUrl"] = "http://token.test/primary",
                    ["primaryTokenSubscriptionKey"] = "mock-primary-token-subscription-key",
                    ["finalTokenUrl"] = "http://token.test/final",
                    ["finalTokenSubscriptionKey"] = "mock-final-token-subscription-key",
                    ["endpointBSubscriptionKey"] = "mock-endpoint-b-subscription-key",
                },
            })).ConfigureAwait(false);
    }

    private static void InstallReferencePlugin(string workspaceRoot)
    {
        string packageDirectory = Path.Combine(workspaceRoot, "plugins", "client.customer-lookup");
        Directory.CreateDirectory(packageDirectory);
        foreach (string file in Directory.EnumerateFiles(ResolveReferencePluginBuildDirectory()))
        {
            File.Copy(file, Path.Combine(packageDirectory, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string ResolveReferencePluginBuildDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ComparisonTool.sln")))
        {
            directory = directory.Parent;
        }

        string repositoryRoot = directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
        string configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;
        return Path.Combine(repositoryRoot, "Source", "ParityBench.ClientCustomerLookupPlugin", "bin", configuration, "net10.0");
    }

    private const string SoapRequestBody =
        "<Envelope><Body><LookupRequest>" +
        "<UserName>svc-user</UserName><Password>svc-pass</Password>" +
        "<CustomerId>C-1</CustomerId><CorrelationId>corr-1</CorrelationId>" +
        "</LookupRequest></Body></Envelope>";

    private const string SoapResponseBody =
        "<Envelope><Body><LookupResponse>" +
        "<StatusCode>OK</StatusCode><CustomerName>Riley Morgan</CustomerName><TraceId>corr-1</TraceId>" +
        "</LookupResponse></Body></Envelope>";

    private const string EndpointBJsonResponse =
        "{\"details\":{\"resultCode\":\"OK\",\"traceId\":\"corr-1\",\"decisionEngine\":\"EndpointB\"}," +
        "\"apps\":[{\"applicantId\":\"corr-1\",\"profile\":{\"fullName\":\"Riley Morgan\",\"addresses\":[]}," +
        "\"ruleEvaluations\":[],\"flags\":[]}]}";

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

    private sealed class SlotAwareEndpointSender : IEndpointRequestSender
    {
        public string? EndpointBAuthorization { get; private set; }

        public Task<EndpointResponse> SendAsync(EndpointRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Endpoint == EndpointSlot.B)
            {
                request.Headers.TryGetValue("Authorization", out string? authorization);
                EndpointBAuthorization = authorization;
                return Task.FromResult(new EndpointResponse(200, "application/json", new MemoryStream(Encoding.UTF8.GetBytes(EndpointBJsonResponse))));
            }

            return Task.FromResult(new EndpointResponse(200, "text/xml", new MemoryStream(Encoding.UTF8.GetBytes(SoapResponseBody))));
        }
    }

    private sealed class TokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string token = request.RequestUri!.AbsolutePath.EndsWith("/primary", StringComparison.Ordinal)
                ? "mock-primary-token"
                : "mock-final-token";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"access_token\":\"{token}\"}}", Encoding.UTF8, "application/json"),
            });
        }
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