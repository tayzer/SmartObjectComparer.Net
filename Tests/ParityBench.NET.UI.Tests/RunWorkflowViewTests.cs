using AngleSharp.Dom;
using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor.Services;

using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.UI.Workflow;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class RunWorkflowViewTests
{
    private BunitContext testContext = null!;
    private FakeRunWorkflowViewDataSource dataSource = null!;
    private FakeRequestSourcePicker sourcePicker = null!;

    [TestInitialize]
    public void SetUp()
    {
        testContext = new BunitContext();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;
        testContext.Services.AddMudServices();
        dataSource = new FakeRunWorkflowViewDataSource();
        sourcePicker = new FakeRequestSourcePicker();
        testContext.Services.AddSingleton<IRunWorkflowViewDataSource>(dataSource);
        testContext.Services.AddSingleton<IRequestSourcePicker>(sourcePicker);
    }

    [TestCleanup]
    public async Task TearDown()
    {
        await testContext.DisposeAsync().ConfigureAwait(false);
    }

    [TestMethod]
    public void RunWorkflow_WhenRendered_ShowsV1StyleStepSections()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        StringAssert.Contains(component.Markup, "Step 1: Upload Request Files");
        StringAssert.Contains(component.Markup, "Step 2: Model &amp; Configuration");
        StringAssert.Contains(component.Markup, "Step 3: Configure Endpoints");
        StringAssert.Contains(component.Markup, "Step 4: Run Comparison");
    }

    [TestMethod]
    public void RunWorkflow_WhenRequiredInputsAreMissing_ShowsRecoverableError()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Start Comparison", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Endpoint A must be an absolute URL."));
    }

    [TestMethod]
    public void PickRequestDirectory_WhenPickerReturnsPath_SetsRequestDirectory()
    {
        sourcePicker.DirectoryToReturn = Path.Combine(Path.GetTempPath(), "request-fixtures");
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Add Request Folder", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, sourcePicker.DirectoryToReturn));
        Assert.AreEqual(1, sourcePicker.DirectoryPickCount);
    }

    [TestMethod]
    public void StartWorkflow_WhenFilesWerePicked_PassesSelectedFilesToWorkflowDataSource()
    {
        string sourceDirectory = Path.Combine(Path.GetTempPath(), "request-fixtures");
        sourcePicker.FilesToReturn = new[]
        {
            Path.Combine(sourceDirectory, "one.json"),
            Path.Combine(sourceDirectory, "two.xml"),
        };
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Add Request Files", StringComparison.Ordinal))
            .Click();
        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Selected 2 request files"));
        ChangeTextField(component, "Endpoint A", "https://a.example.test");
        ChangeTextField(component, "Endpoint B", "https://b.example.test");
        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Start Comparison", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() => Assert.IsNotNull(dataSource.LastRequest));
        Assert.AreEqual(sourceDirectory, dataSource.LastRequest!.SourceDirectory);
        CollectionAssert.AreEqual(sourcePicker.FilesToReturn.ToArray(), dataSource.LastRequest.SourceFiles.ToArray());
        Assert.IsTrue(dataSource.StartWasCalled);
    }

    private static void ChangeTextField(IRenderedComponent<RunWorkflow> component, string labelText, string value)
    {
        IElement label = component.FindAll("label")
            .Single(element => string.Equals(element.TextContent.Trim(), labelText, StringComparison.Ordinal));
        string? inputId = label.GetAttribute("for");
        Assert.IsFalse(string.IsNullOrWhiteSpace(inputId));
        IElement input = component.Find($"#{inputId}");
        input.Input(value);
        input.Change(value);
    }

    private sealed class FakeRequestSourcePicker : IRequestSourcePicker
    {
        public bool IsAvailable => true;

        public int DirectoryPickCount { get; private set; }

        public string? DirectoryToReturn { get; set; }

        public IReadOnlyList<string> FilesToReturn { get; set; } = Array.Empty<string>();

        public Task<IReadOnlyList<string>> PickRequestFilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(FilesToReturn);

        public Task<string?> PickRequestDirectoryAsync(CancellationToken cancellationToken = default)
        {
            DirectoryPickCount++;
            return Task.FromResult(DirectoryToReturn);
        }
    }

    private sealed class FakeRunWorkflowViewDataSource : IRunWorkflowViewDataSource
    {
        public RequestComparisonRunRequest? LastRequest { get; private set; }

        public bool StartWasCalled { get; private set; }

        public Task<ComparisonRun> CreateRunFromDirectoryAsync(
            RequestComparisonRunRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(ComparisonRun.Create(new RunId("run-1"), CreateOptions()));
        }

        public Task<bool> StartRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default)
        {
            StartWasCalled = true;
            return Task.FromResult(false);
        }

        public Task<ComparisonRun> CancelRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ComparisonRun> LoadRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool IsRunning(RunId runId) => false;

        public Task<StaticReportBundleWriteResult> GenerateReportAsync(
            RunId runId,
            string outputDirectory,
            string? reportAssetsDirectory = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static RunOptions CreateOptions() =>
            new RunOptions(
                new RequestBatchReference("batch-1"),
                new EndpointDefinition(new Uri("https://a.example.test")),
                new EndpointDefinition(new Uri("https://b.example.test")),
                TimeSpan.FromSeconds(30),
                2);
    }
}