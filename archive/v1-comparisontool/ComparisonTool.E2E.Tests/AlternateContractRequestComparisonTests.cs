using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparisonTool.E2E.Tests;

[TestClass]
public sealed class AlternateContractRequestComparisonTests
{
    private const string ExpectedModelName = "ExpectedJsonCustomerLookupResponse";
    private const string ExpectedProfileId = "expected-json-customer-lookup";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static E2ETestHost? host;

    private IPlaywright? playwright;
    private IBrowser? browser;
    private IBrowserContext? browserContext;
    private IPage? page;

    private E2ETestHost Host =>
        host ?? throw new InvalidOperationException("E2E host has not been initialized.");

    private IPage Page => page ?? throw new InvalidOperationException("Playwright page has not been initialized.");

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        host = await E2ETestHost.StartAsync(context).ConfigureAwait(false);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (host != null)
        {
            await host.DisposeAsync().ConfigureAwait(false);
            host = null;
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        }).ConfigureAwait(false);
        browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = 1366,
                Height = 900,
            },
        }).ConfigureAwait(false);
        page = await browserContext.NewPageAsync().ConfigureAwait(false);
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        if (browserContext != null)
        {
            await browserContext.CloseAsync().ConfigureAwait(false);
            browserContext = null;
        }

        if (browser != null)
        {
            await browser.CloseAsync().ConfigureAwait(false);
            browser = null;
        }

        playwright?.Dispose();
        playwright = null;
        page = null;
    }

    [TestMethod]
    [Timeout(180000)]
    public async Task AlternativeContract_MixedFixture_RunsExpectedOutcomes()
    {
        var batch = await StageFixtureAsync("manual-mixed").ConfigureAwait(false);

        await RunAlternateContractComparisonAsync(batch).ConfigureAwait(false);

        await AssertVisibleAsync(ResultFileCell("success-request.xml")).ConfigureAwait(false);
        await AssertVisibleAsync(ResultFileCell("difference-request.xml")).ConfigureAwait(false);
        await AssertVisibleAsync(ResultFileCell("error-request.xml")).ConfigureAwait(false);
        await AssertVisibleAsync(Page.GetByText("Both Non-Success").First).ConfigureAwait(false);
        await AssertVisibleAsync(Page.GetByText("A: 200").First).ConfigureAwait(false);
        await AssertVisibleAsync(Page.GetByText("B: 200").First).ConfigureAwait(false);

        await AssertCustomerNamePropertyGroupAsync(
            expectedAffectedFilesText: "1 files affected",
            expectedPagingText: "Showing 1-1 of 1 files | Page 1 of 1",
            expectedFileCounts: new Dictionary<string, int>
            {
                ["difference-request.xml"] = 1,
            }).ConfigureAwait(false);

        await ResultFileCell("error-request.xml").ClickAsync().ConfigureAwait(false);

        await AssertVisibleAsync(Page.GetByText("Raw Response Comparison")).ConfigureAwait(false);
        await AssertVisibleAsync(Page.GetByText("Both endpoints returned non-success responses.")).ConfigureAwait(false);
    }

    [TestMethod]
    [Timeout(180000)]
    public async Task AlternativeContract_DuplicateNames_ShowDistinctRequestRelativePaths()
    {
        var batch = await StageFixtureAsync("duplicate-names").ConfigureAwait(false);

        await RunAlternateContractComparisonAsync(batch).ConfigureAwait(false);

        await AssertVisibleCountAsync(ResultFileCells("alpha/lookup.xml"), 1, "alpha result row").ConfigureAwait(false);
        await AssertVisibleCountAsync(ResultFileCells("beta/lookup.xml"), 1, "beta result row").ConfigureAwait(false);
        await AssertVisibleTextCountAsync("lookup.json vs lookup.json", 0).ConfigureAwait(false);

        await AssertCustomerNamePropertyGroupAsync(
            expectedAffectedFilesText: "2 files affected",
            expectedPagingText: "Showing 1-2 of 2 files | Page 1 of 1",
            expectedFileCounts: new Dictionary<string, int>
            {
                ["alpha/lookup.xml"] = 1,
                ["beta/lookup.xml"] = 1,
            }).ConfigureAwait(false);

        await ResultFileCell("alpha/lookup.xml").ClickAsync().ConfigureAwait(false);
        await AssertSelectedCustomerNameDifferenceAsync("alpha/lookup.xml").ConfigureAwait(false);
        await AssertVisibleTextCountAsync("lookup.json vs lookup.json", 0).ConfigureAwait(false);

        await ResultFileCell("beta/lookup.xml").ClickAsync().ConfigureAwait(false);
        await AssertSelectedCustomerNameDifferenceAsync("beta/lookup.xml").ConfigureAwait(false);
        await AssertVisibleTextCountAsync("lookup.json vs lookup.json", 0).ConfigureAwait(false);
    }

    private async Task<StageFixtureResponse> StageFixtureAsync(string fixtureSet)
    {
        using var http = new HttpClient();
        var response = await http.PostAsync(
            new Uri(Host.WebBaseUri, $"api/test-fixtures/request-comparison/{fixtureSet}/stage"),
            null).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Fixture staging failed: {(int)response.StatusCode} {content}");
        }

        return JsonSerializer.Deserialize<StageFixtureResponse>(content, JsonOptions)
            ?? throw new InvalidOperationException("Fixture staging returned an empty response.");
    }

    private async Task RunAlternateContractComparisonAsync(StageFixtureResponse batch)
    {
        var targetUri = new Uri(
            Host.WebBaseUri,
            $"?requestBatchId={Uri.EscapeDataString(batch.BatchId)}&requestBatchCount={batch.Uploaded}&requestModelName={ExpectedModelName}&requestUseAlternateContract=true&requestAlternateContractProfileId={ExpectedProfileId}");

        await Page.GotoAsync(targetUri.ToString()).ConfigureAwait(false);
        await AssertVisibleAsync(Page.GetByText("File Comparison Tool (XML & JSON)"), 30000).ConfigureAwait(false);
        await AssertVisibleAsync(Page.GetByText($"Uploaded {batch.Uploaded} files. Batch ID: {batch.BatchId}"), 30000).ConfigureAwait(false);
        await AssertVisibleAsync(Page.GetByText($"Using alternate contract profile: {ExpectedProfileId}")).ConfigureAwait(false);

        await Page.WaitForTimeoutAsync(1000).ConfigureAwait(false);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Start Comparison" }).ClickAsync().ConfigureAwait(false);
        await AssertVisibleAsync(Page.GetByText("Request Comparison Results"), 90000).ConfigureAwait(false);
    }

    private ILocator ResultFileCells(string requestRelativePath)
    {
        return Page.GetByRole(AriaRole.Cell, new PageGetByRoleOptions { Name = requestRelativePath, Exact = true });
    }

    private ILocator ResultFileCell(string requestRelativePath)
    {
        return ResultFileCells(requestRelativePath).First;
    }

    private async Task AssertDetailHeaderPathAsync(string expectedPath)
    {
        var detailHeader = Page.Locator("#detailed-differences-section");
        await AssertVisibleAsync(detailHeader.GetByText(expectedPath, new LocatorGetByTextOptions { Exact = true })).ConfigureAwait(false);
    }

    private async Task AssertSelectedCustomerNameDifferenceAsync(string expectedPath)
    {
        await AssertDetailHeaderPathAsync(expectedPath).ConfigureAwait(false);

        var selectedDetailPanel = Page.Locator("#detailed-differences-section + .mud-paper").First;
        await AssertVisibleAsync(selectedDetailPanel.GetByText("All Differences", new LocatorGetByTextOptions { Exact = true })).ConfigureAwait(false);
        await AssertVisibleCountAsync(selectedDetailPanel.GetByText("CustomerName", new LocatorGetByTextOptions { Exact = true }), 1, "selected CustomerName difference").ConfigureAwait(false);
        await AssertVisibleCountAsync(selectedDetailPanel.GetByText("1 diffs", new LocatorGetByTextOptions { Exact = true }), 1, "selected CustomerName difference count").ConfigureAwait(false);
    }

    private async Task AssertCustomerNamePropertyGroupAsync(
        string expectedAffectedFilesText,
        string expectedPagingText,
        IReadOnlyDictionary<string, int> expectedFileCounts)
    {
        var propertyTree = Page.Locator(".property-tree-container").First;
        await propertyTree.GetByText("CustomerName", new LocatorGetByTextOptions { Exact = true }).ClickAsync().ConfigureAwait(false);

        var valueDetailsPanel = Page.Locator("#value-inspector-details-panel");
        await AssertVisibleAsync(valueDetailsPanel.GetByText("CustomerName", new LocatorGetByTextOptions { Exact = true })).ConfigureAwait(false);
        await AssertVisibleAsync(valueDetailsPanel.GetByText(expectedAffectedFilesText, new LocatorGetByTextOptions { Exact = true })).ConfigureAwait(false);
        await AssertVisibleAsync(valueDetailsPanel.GetByText(expectedPagingText, new LocatorGetByTextOptions { Exact = true })).ConfigureAwait(false);

        var expectedCardCount = expectedFileCounts.Values.Sum();
        await AssertVisibleCountAsync(
            valueDetailsPanel.Locator(".file-diff-card"),
            expectedCardCount,
            "CustomerName affected-file cards").ConfigureAwait(false);

        foreach (var expectedFileCount in expectedFileCounts)
        {
            await AssertVisibleCountAsync(
                valueDetailsPanel.GetByText(expectedFileCount.Key, new LocatorGetByTextOptions { Exact = true }),
                expectedFileCount.Value,
                $"CustomerName affected-file card for {expectedFileCount.Key}").ConfigureAwait(false);
        }

        await AssertVisibleCountAsync(
            valueDetailsPanel.GetByText("lookup.json vs lookup.json", new LocatorGetByTextOptions { Exact = true }),
            0,
            "artifact-name fallback in CustomerName inspector").ConfigureAwait(false);
    }

    private async Task AssertVisibleAsync(ILocator locator, int timeoutMs = 10000)
    {
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMs,
            }).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            var bodyText = await ReadBodyTextForDiagnosticsAsync().ConfigureAwait(false);
            throw new AssertFailedException(
                $"Timed out waiting for {locator}. Url: {Page.Url}. Body text: {bodyText}{Environment.NewLine}Service logs:{Environment.NewLine}{Host.GetRecentLogs()}",
                ex);
        }
    }

    private async Task<string> ReadBodyTextForDiagnosticsAsync()
    {
        try
        {
            var text = await Page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions
            {
                Timeout = 1000,
            }).ConfigureAwait(false);
            return text.Length <= 4000 ? text : text[..4000];
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            return $"<unable to read body text: {ex.Message}>";
        }
    }

    private async Task AssertVisibleCountAsync(ILocator locator, int expectedCount, string description)
    {
        var actualCount = await CountVisibleAsync(locator).ConfigureAwait(false);
        if (actualCount != expectedCount)
        {
            var bodyText = await ReadBodyTextForDiagnosticsAsync().ConfigureAwait(false);
            Assert.AreEqual(
                expectedCount,
                actualCount,
                $"Expected {expectedCount} visible occurrences of {description}, but found {actualCount}. Body text: {bodyText}{Environment.NewLine}Service logs:{Environment.NewLine}{Host.GetRecentLogs()}");
        }
    }

    private async Task AssertVisibleTextCountAsync(string text, int expectedCount)
    {
        var actualCount = await CountVisibleAsync(Page.GetByText(text, new PageGetByTextOptions { Exact = true })).ConfigureAwait(false);
        Assert.AreEqual(expectedCount, actualCount, $"Expected {expectedCount} visible occurrences of '{text}', but found {actualCount}.");
    }

    private async Task<int> CountVisibleAsync(ILocator locator)
    {
        var count = await locator.CountAsync().ConfigureAwait(false);
        var visibleCount = 0;
        for (var index = 0; index < count; index++)
        {
            if (await locator.Nth(index).IsVisibleAsync().ConfigureAwait(false))
            {
                visibleCount++;
            }
        }

        return visibleCount;
    }

    private sealed class StageFixtureResponse
    {
        public int Uploaded { get; init; }

        public string BatchId { get; init; } = string.Empty;
    }

    private sealed class E2ETestHost : IAsyncDisposable
    {
        private readonly Process mockApiProcess;
        private readonly Process webProcess;
        private readonly ConcurrentQueue<string> logs;

        private E2ETestHost(
            Uri mockBaseUri,
            Uri webBaseUri,
            Process mockApiProcess,
            Process webProcess,
            ConcurrentQueue<string> logs)
        {
            MockBaseUri = mockBaseUri;
            WebBaseUri = webBaseUri;
            this.mockApiProcess = mockApiProcess;
            this.webProcess = webProcess;
            this.logs = logs;
        }

        public Uri MockBaseUri { get; }

        public Uri WebBaseUri { get; }

        public static async Task<E2ETestHost> StartAsync(TestContext context)
        {
            var solutionRoot = FindSolutionRoot();
            var mockPort = GetFreeTcpPort();
            var webPort = GetFreeTcpPort();
            var mockBaseUri = new Uri($"http://127.0.0.1:{mockPort}/");
            var webBaseUri = new Uri($"http://127.0.0.1:{webPort}/");

            var logs = new ConcurrentQueue<string>();
            var mockProcess = StartDotNetProject(
                "MockApi",
                Path.Combine(solutionRoot, "ComparisonTool.MockApi", "ComparisonTool.MockApi.csproj"),
                mockBaseUri,
                logs,
                new Dictionary<string, string>
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                });

            try
            {
                await WaitForHttpSuccessAsync(new Uri(mockBaseUri, "health"), mockProcess, logs).ConfigureAwait(false);

                var webProcess = StartDotNetProject(
                    "Web",
                    Path.Combine(solutionRoot, "ComparisonTool.Web", "ComparisonTool.Web.csproj"),
                    webBaseUri,
                    logs,
                    new Dictionary<string, string>
                    {
                        ["ASPNETCORE_ENVIRONMENT"] = "Development",
                        ["RequestComparison__TestFixtures__Enabled"] = "true",
                        ["RequestComparison__AlternateContracts__ExpectedJsonCustomerLookup__AuthorizationTokenUrl"] =
                            new Uri(mockBaseUri, "api/mock/authorisation-token").ToString(),
                        ["RequestComparison__EndpointOptions__AllowCustom"] = "false",
                        ["RequestComparison__EndpointOptions__Endpoints__0__Name"] = "Local Mock Customer Lookup SOAP",
                        ["RequestComparison__EndpointOptions__Endpoints__0__Url"] =
                            new Uri(mockBaseUri, "api/mock/customer-lookup/soap").ToString(),
                        ["RequestComparison__EndpointOptions__Endpoints__1__Name"] = "Local Mock Customer Lookup JSON",
                        ["RequestComparison__EndpointOptions__Endpoints__1__Url"] =
                            new Uri(mockBaseUri, "api/mock/customer-lookup/json").ToString(),
                    });

                try
                {
                    await WaitForHttpSuccessAsync(webBaseUri, webProcess, logs).ConfigureAwait(false);
                    return new E2ETestHost(mockBaseUri, webBaseUri, mockProcess, webProcess, logs);
                }
                catch
                {
                    await StopProcessAsync(webProcess).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                context.WriteLine("Failed to start E2E services.");
                context.WriteLine(ex.ToString());
                foreach (var line in logs.TakeLast(80))
                {
                    context.WriteLine(line);
                }

                await StopProcessAsync(mockProcess).ConfigureAwait(false);
                throw;
            }
        }

        public string GetRecentLogs()
        {
            return FormatLogs(logs);
        }

        public async ValueTask DisposeAsync()
        {
            await StopProcessAsync(webProcess).ConfigureAwait(false);
            await StopProcessAsync(mockApiProcess).ConfigureAwait(false);
        }

        private static Process StartDotNetProject(
            string name,
            string projectPath,
            Uri baseUri,
            ConcurrentQueue<string> logs,
            IReadOnlyDictionary<string, string> environment)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--no-launch-profile");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(projectPath);

            startInfo.Environment["ASPNETCORE_URLS"] = baseUri.ToString().TrimEnd('/');
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    logs.Enqueue($"{name}: {args.Data}");
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    logs.Enqueue($"{name}: {args.Data}");
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start {name}.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        private static async Task WaitForHttpSuccessAsync(
            Uri uri,
            Process process,
            ConcurrentQueue<string> logs)
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3),
            };

            var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Process exited before {uri} became ready. Exit code {process.ExitCode}.{Environment.NewLine}{FormatLogs(logs)}");
                }

                try
                {
                    using var response = await http.GetAsync(uri).ConfigureAwait(false);
                    if (response.StatusCode != HttpStatusCode.ServiceUnavailable
                        && (int)response.StatusCode < 500)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Keep polling until the app binds its port.
                }
                catch (TaskCanceledException)
                {
                    // Keep polling while the process is still alive.
                }

                await Task.Delay(500).ConfigureAwait(false);
            }

            throw new TimeoutException($"Timed out waiting for {uri}.{Environment.NewLine}{FormatLogs(logs)}");
        }

        private static string FormatLogs(ConcurrentQueue<string> logs)
        {
            return string.Join(Environment.NewLine, logs.TakeLast(80));
        }

        private static async Task StopProcessAsync(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Process already exited.
            }
            finally
            {
                process.Dispose();
            }
        }

        private static string FindSolutionRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "ComparisonTool.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate ComparisonTool.sln from the test output directory.");
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
