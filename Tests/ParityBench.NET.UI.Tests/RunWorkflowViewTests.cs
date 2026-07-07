using AngleSharp.Dom;
using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor;
using MudBlazor.Services;

using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Comparison;
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
        testContext.RenderTree.Add<MudTestRoot>(parameters => { });
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

    [TestMethod]
    public void RunWorkflow_WhenDefaultsLoad_RendersModelProfileAndEndpointOptions()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        component.WaitForAssertion(() =>
        {
            StringAssert.Contains(component.Markup, "Example preset");
            StringAssert.Contains(component.Markup, "Select Domain Model");
            StringAssert.Contains(component.Markup, "Contract Profile");
            StringAssert.Contains(component.Markup, "Endpoint A");
            StringAssert.Contains(component.Markup, "Endpoint B");
        });
    }

    [TestMethod]
    public void RunWorkflow_WhenPresetIsSelected_PopulatesRequestFieldsAndRules()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        SelectPreset(component, "json-json-consumer-report");
        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Start Comparison", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() => Assert.IsNotNull(dataSource.LastRequest));
        Assert.AreEqual("Examples/ParityBench.NET.ManualRuns/json-json", dataSource.LastRequest!.SourceDirectory);
        Assert.AreEqual("https://fixture.example.test/consumer-report/json/a", dataSource.LastRequest.EndpointA.ToString().TrimEnd('/'));
        Assert.AreEqual("https://fixture.example.test/consumer-report/json/b", dataSource.LastRequest.EndpointB.ToString().TrimEnd('/'));
        Assert.AreEqual("ConsumerReportJsonResponse", dataSource.LastRequest.ModelName);
        Assert.IsTrue(dataSource.LastRequest.ComparisonOptions.IgnoreStringCase);
        Assert.AreEqual("Subject.NationalIdentifier", dataSource.LastRequest.ComparisonOptions.MaskRules.Single().PropertyPath);
    }

    [TestMethod]
    public void RunWorkflow_WhenProfileIsSelected_ShowsDefaultComparisonRules()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        SetAutocompleteValue(component, 0, "SampleSoapCustomerLookupResponseEnvelope");
        SetAutocompleteValue(component, 1, "sample-soap-to-json");

        component.WaitForAssertion(() =>
        {
            StringAssert.Contains(component.Markup, "Profile default comparison rules");
            StringAssert.Contains(component.Markup, "Ignore collection order");
            StringAssert.Contains(component.Markup, "Ignore paths: 1");
            StringAssert.Contains(component.Markup, "Smart ignores: 1");
            StringAssert.Contains(component.Markup, "Masks: 1");
            StringAssert.Contains(component.Markup, "Profile default ignore rules");
            StringAssert.Contains(component.Markup, "SourceSystem");
        });
    }

    [TestMethod]
    public void RunWorkflow_WhenCustomEndpointIsTyped_UsesCustomValue()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        ChangeTextField(component, "Request Directory", "requests");
        SetAutocompleteValue(component, 2, "https://custom-a.example.test/api");
        SetAutocompleteValue(component, 3, "https://custom-b.example.test/api");
        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Start Comparison", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() => Assert.IsNotNull(dataSource.LastRequest));
        Assert.AreEqual(new Uri("https://custom-a.example.test/api"), dataSource.LastRequest!.EndpointA);
        Assert.AreEqual(new Uri("https://custom-b.example.test/api"), dataSource.LastRequest.EndpointB);
    }

    [TestMethod]
    public void RunWorkflow_WhenCustomModelIsTyped_UsesCustomValue()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        ChangeTextField(component, "Request Directory", "requests");
        SetAutocompleteValue(component, 0, "CustomModelName");
        SetAutocompleteValue(component, 2, "https://a.example.test/api");
        SetAutocompleteValue(component, 3, "https://b.example.test/api");
        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Start Comparison", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() => Assert.IsNotNull(dataSource.LastRequest));
        Assert.AreEqual("CustomModelName", dataSource.LastRequest!.ModelName);
    }

    private static void SelectPreset(IRenderedComponent<RunWorkflow> component, string presetId)
    {
        IRenderedComponent<MudSelect<string>> select = component.FindComponent<MudSelect<string>>();
        component.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(presetId)).GetAwaiter().GetResult();
    }

    private static void SetAutocompleteValue(IRenderedComponent<RunWorkflow> component, int index, string value)
    {
        IRenderedComponent<MudAutocomplete<string>> autocomplete = component.FindComponents<MudAutocomplete<string>>()[index];
        component.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(value)).GetAwaiter().GetResult();
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

        public Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDefaults());

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

        private static RequestComparisonDefaults CreateDefaults() =>
            new RequestComparisonDefaults(
                new[]
                {
                    new ResponseModelOption("ConsumerReportJsonResponse"),
                    new ResponseModelOption("SampleSoapCustomerLookupResponseEnvelope"),
                },
                new[]
                {
                    new ContractProfileOption(
                        "SampleSoapCustomerLookupResponseEnvelope",
                        "sample-soap-to-json",
                        "1",
                        "sample/customer-lookup/soap",
                        "sample/customer-lookup/json",
                        new ComparisonRuleDefaults(
                            ignoreCollectionOrder: true,
                            ignoreRules: new[] { new IgnoreRuleDefinition("SourceSystem") },
                            smartIgnoreRules: new[] { new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "TraceId") },
                            maskRules: new[] { new MaskRuleDefinition("SensitiveToken") })),
                },
                new[]
                {
                    new EndpointOption("consumer-report/json/a", "Consumer Report JSON A", new Uri("https://fixture.example.test/consumer-report/json/a")),
                    new EndpointOption("consumer-report/json/b", "Consumer Report JSON B", new Uri("https://fixture.example.test/consumer-report/json/b")),
                    new EndpointOption("sample/customer-lookup/soap", "Sample Customer Lookup SOAP", new Uri("https://fixture.example.test/sample/customer-lookup/soap/a")),
                    new EndpointOption("sample/customer-lookup/json", "Sample Customer Lookup JSON", new Uri("https://fixture.example.test/sample/customer-lookup/json/b")),
                },
                new[]
                {
                    new RequestComparisonPresetOption(
                        "json-json-consumer-report",
                        "JSON/JSON consumer report",
                        "Examples/ParityBench.NET.ManualRuns/json-json",
                        new Uri("https://fixture.example.test/consumer-report/json/a"),
                        new Uri("https://fixture.example.test/consumer-report/json/b"),
                        "ConsumerReportJsonResponse",
                        null,
                        new ComparisonOptions(
                            ignoreStringCase: true,
                            maskRules: new[] { new MaskRuleDefinition("Subject.NationalIdentifier", 4) }),
                        new RequestExecutionOptions()),
                });

        private static RunOptions CreateOptions() =>
            new RunOptions(
                new RequestBatchReference("batch-1"),
                new EndpointDefinition(new Uri("https://a.example.test")),
                new EndpointDefinition(new Uri("https://b.example.test")),
                TimeSpan.FromSeconds(30),
                2);
    }
}


