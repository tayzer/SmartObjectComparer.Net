using AngleSharp.Dom;
using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor;
using MudBlazor.Services;

using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.UI.Workflow;
using ParityBench.NET.UI.Theming;

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
        testContext.Services.AddScoped<ParityBenchThemeState>();
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
    public void RunWorkflow_WhenDefaultsLoad_UsesV2RunDefaults()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        component.WaitForAssertion(() => Assert.AreEqual("32", GetTextFieldValue(component, "Max Concurrency")));
        Assert.AreEqual("30", GetTextFieldValue(component, "Request Timeout (seconds)"));
    }

    [TestMethod]
    public void RunWorkflow_WhenDefaultsAreConfigured_UsesConfiguredRunDefaults()
    {
        dataSource.RunDefaults = new RequestComparisonRunDefaults(maxConcurrency: 12, timeoutSeconds: 45);

        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        component.WaitForAssertion(() => Assert.AreEqual("12", GetTextFieldValue(component, "Max Concurrency")));
        Assert.AreEqual("45", GetTextFieldValue(component, "Request Timeout (seconds)"));
    }
    [TestMethod]
    public void RunWorkflow_WhenModelIsSelected_RendersRuleStudiosWithModelPaths()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        SetAutocompleteValue(component, 0, "ConsumerReportJsonResponse");

        component.WaitForAssertion(() =>
        {
            StringAssert.Contains(component.Markup, "Ignore Rules Studio");
            StringAssert.Contains(component.Markup, "Mask Rules Studio");
            StringAssert.Contains(component.Markup, "Subject.NationalIdentifier");
            StringAssert.Contains(component.Markup, "ProviderTraceId");
        });
    }

    [TestMethod]
    public void IgnoreRulesStudio_WhenSearchRowIsClicked_PopulatesEditorPath()
    {
        IRenderedComponent<IgnoreRulesStudio> component = testContext.Render<IgnoreRulesStudio>(parameters => parameters
            .Add(p => p.ModelType, typeof(FakeRunWorkflowViewDataSource.FakeConsumerReportResponse))
            .Add(p => p.ModelName, "ConsumerReportJsonResponse")
            .Add(p => p.Rules, new List<IgnoreRuleDefinition>()));

        FindButtonContaining(component, "ProviderTraceId").Click();

        component.WaitForAssertion(() => Assert.AreEqual("ProviderTraceId", GetTextFieldValue(component, "Property Path")));
        StringAssert.Contains(component.Markup, "Selected");
    }

    [TestMethod]
    public void IgnoreRulesStudio_WhenTreeLeafRowIsClicked_TogglesSelectionAndPopulatesEditorPath()
    {
        IRenderedComponent<IgnoreRulesStudio> component = testContext.Render<IgnoreRulesStudio>(parameters => parameters
            .Add(p => p.ModelType, typeof(FakeRunWorkflowViewDataSource.FakeConsumerReportResponse))
            .Add(p => p.ModelName, "ConsumerReportJsonResponse")
            .Add(p => p.Rules, new List<IgnoreRuleDefinition>()));

        ClickTab(component, "Tree");
        FindLastButtonContaining(component, "ProviderTraceId").Click();

        component.WaitForAssertion(() =>
        {
            Assert.AreEqual("ProviderTraceId", GetTextFieldValue(component, "Property Path"));
            StringAssert.Contains(component.Markup, "1 selected");
        });
    }

    [TestMethod]
    public void IgnoreRulesStudio_WhenCollectionPathIsClicked_EnablesCollectionOrderRule()
    {
        IRenderedComponent<IgnoreRulesStudio> component = testContext.Render<IgnoreRulesStudio>(parameters => parameters
            .Add(p => p.ModelType, typeof(FakeRunWorkflowViewDataSource.FakeConsumerReportResponse))
            .Add(p => p.ModelName, "ConsumerReportJsonResponse")
            .Add(p => p.Rules, new List<IgnoreRuleDefinition>()));

        FindButtonContaining(component, "Aliases").Click();

        component.WaitForAssertion(() =>
        {
            Assert.AreEqual("Aliases", GetTextFieldValue(component, "Property Path"));
            IElement collectionOrderInput = FindCheckboxInput(component, "Ignore collection order");
            Assert.IsNull(collectionOrderInput.GetAttribute("disabled"));
            Assert.IsNotNull(collectionOrderInput.GetAttribute("checked"));
        });

        FindButtonContaining(component, "Add Or Update Rule").Click();

        component.WaitForAssertion(() =>
        {
            IgnoreRuleDefinition rule = component.Instance.Rules.Single();
            Assert.AreEqual("Aliases", rule.PropertyPath);
            Assert.IsFalse(rule.IgnoreCompletely);
            Assert.IsTrue(rule.IgnoreCollectionOrder);
        });
    }

    [TestMethod]
    public void IgnoreRulesStudio_WhenManyCurrentRulesAreConfigured_CapsAndFiltersList()
    {
        List<IgnoreRuleDefinition> rules = Enumerable
            .Range(0, 30)
            .Select(index => new IgnoreRuleDefinition($"Rule{index:00}"))
            .Append(new IgnoreRuleDefinition("Target.VisibleRule"))
            .ToList();
        IRenderedComponent<IgnoreRulesStudio> component = testContext.Render<IgnoreRulesStudio>(parameters => parameters
            .Add(p => p.ModelType, typeof(FakeRunWorkflowViewDataSource.FakeConsumerReportResponse))
            .Add(p => p.ModelName, "ConsumerReportJsonResponse")
            .Add(p => p.Rules, rules));

        component.WaitForAssertion(() =>
        {
            StringAssert.Contains(component.Markup, "31 custom");
            StringAssert.Contains(component.Markup, "Rule00");
            Assert.IsFalse(component.Markup.Contains("Rule29", StringComparison.Ordinal));
            StringAssert.Contains(component.Markup, "Show all 31 rules");
        });

        ChangeTextField(component, "Filter Current Ignore Rules", "Target");

        component.WaitForAssertion(() =>
        {
            StringAssert.Contains(component.Markup, "Target.VisibleRule");
            Assert.IsFalse(component.Markup.Contains("Rule00", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void MaskRulesStudio_WhenSearchRowIsClicked_PopulatesEditorPath()
    {
        IRenderedComponent<MaskRulesStudio> component = testContext.Render<MaskRulesStudio>(parameters => parameters
            .Add(p => p.ModelType, typeof(FakeRunWorkflowViewDataSource.FakeConsumerReportResponse))
            .Add(p => p.ModelName, "ConsumerReportJsonResponse")
            .Add(p => p.Rules, new List<MaskRuleDefinition>()));

        FindButtonContaining(component, "Subject.NationalIdentifier").Click();

        component.WaitForAssertion(() => Assert.AreEqual("Subject.NationalIdentifier", GetTextFieldValue(component, "Property Path")));
        StringAssert.Contains(component.Markup, "Selected");
    }

    [TestMethod]
    public void MaskRulesStudio_WhenTreeLeafRowIsClicked_PopulatesEditorPath()
    {
        IRenderedComponent<MaskRulesStudio> component = testContext.Render<MaskRulesStudio>(parameters => parameters
            .Add(p => p.ModelType, typeof(FakeRunWorkflowViewDataSource.FakeConsumerReportResponse))
            .Add(p => p.ModelName, "ConsumerReportJsonResponse")
            .Add(p => p.Rules, new List<MaskRuleDefinition>()));

        ClickTab(component, "Tree");
        FindLastButtonContaining(component, "Subject").Click();
        FindLastButtonContaining(component, "NationalIdentifier").Click();

        component.WaitForAssertion(() => Assert.AreEqual("Subject.NationalIdentifier", GetTextFieldValue(component, "Property Path")));
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
        Assert.AreEqual("application/json", dataSource.LastRequest.EndpointAHeaders["Content-Type"]);
        Assert.AreEqual("fixture-key", dataSource.LastRequest.EndpointBHeaders["X-Fixture-Key"]);
        Assert.IsTrue(dataSource.LastRequest.ComparisonOptions.IgnoreStringCase);
        Assert.AreEqual("Subject.NationalIdentifier", dataSource.LastRequest.ComparisonOptions.MaskRules.Single().PropertyPath);
    }

    [TestMethod]
    public void RunWorkflow_WhenRunProfileIsSelected_PopulatesEndpointsAndRunCarriesPluginComparison()
    {
        dataSource.RunProfilesToReturn = new[]
        {
            new RunProfileSummary("client-customer-lookup-local", "Client Customer Lookup — Local"),
        };
        dataSource.ResolvedProfile = new ResolvedRunProfileView(
            new Uri("https://qa.example.test/soap"),
            new Uri("https://qa.example.test/json"),
            new ComparisonOptions(ignoreXmlNamespaces: true),
            "C:/runs/client",
            new PluginComparisonSelection("client.customer-lookup", "client.customer-lookup.soap-vs-json"),
            typeof(PluginComparisonFixture),
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            LargeRun: new LargeRunOptions(comparisonConcurrency: 8));

        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        // The run-profile picker is the first select when profiles are present.
        SelectFirstDropdown(component, "client-customer-lookup-local");
        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Start Comparison", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() => Assert.IsNotNull(dataSource.LastRequest));
        // The plugin selection from the resolved profile flows into the run.
        Assert.IsNotNull(dataSource.LastRequest!.PluginComparison);
        Assert.AreEqual("client.customer-lookup", dataSource.LastRequest.PluginComparison!.PluginId);
        Assert.AreEqual("client.customer-lookup.soap-vs-json", dataSource.LastRequest.PluginComparison.ComparisonId);
        Assert.AreEqual("https://qa.example.test/soap", dataSource.LastRequest.EndpointA.ToString().TrimEnd('/'));
        Assert.AreEqual("C:/runs/client", dataSource.LastRequest.SourceDirectory);
        Assert.AreEqual(8, dataSource.LastRequest.LargeRunOptions.ComparisonConcurrency);
        // A plugin run must never carry its canonical type name as the wire-level
        // ResponseModelName: a raw-comparison fallback (transport error, non-2xx
        // status) resolves that name against the legacy response-model registry,
        // where plugin comparisons are never registered.
        Assert.AreEqual("Auto", dataSource.LastRequest.ModelName);
    }

    [TestMethod]
    public void RunWorkflow_WhenTheProfileSetsRetention_TheRunCarriesIt()
    {
        dataSource.RunProfilesToReturn = new[]
        {
            new RunProfileSummary("client-customer-lookup-local", "Client Customer Lookup — Local"),
        };
        dataSource.ResolvedProfile = CreateResolvedProfile(RetentionMode.None);

        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();
        SelectFirstDropdown(component, "client-customer-lookup-local");
        StartComparison(component);

        component.WaitForAssertion(() => Assert.IsNotNull(dataSource.LastRequest));
        // Without this the profile's retention mode is inert and every run falls back
        // to the app-wide setting.
        Assert.AreEqual(RetentionMode.None, dataSource.LastRequest!.RunRetentionModeOverride);
    }

    [TestMethod]
    public void RunWorkflow_WhenThePickerChangesRetention_ItBeatsTheProfile()
    {
        dataSource.RunProfilesToReturn = new[]
        {
            new RunProfileSummary("client-customer-lookup-local", "Client Customer Lookup — Local"),
        };
        dataSource.ResolvedProfile = CreateResolvedProfile(RetentionMode.None);

        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();
        SelectFirstDropdown(component, "client-customer-lookup-local");
        SelectByLabel(component, "Response retention", nameof(RetentionMode.TrimmedEquals));
        StartComparison(component);

        component.WaitForAssertion(() => Assert.IsNotNull(dataSource.LastRequest));
        Assert.AreEqual(RetentionMode.TrimmedEquals, dataSource.LastRequest!.RunRetentionModeOverride);
    }

    [TestMethod]
    public void RunWorkflow_WhenRetentionIsLeftAtTheDefault_TheRunNamesNoOverride()
    {
        dataSource.RunProfilesToReturn = new[]
        {
            new RunProfileSummary("client-customer-lookup-local", "Client Customer Lookup — Local"),
        };
        dataSource.ResolvedProfile = CreateResolvedProfile(retentionModeOverride: null);

        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();
        SelectFirstDropdown(component, "client-customer-lookup-local");
        StartComparison(component);

        component.WaitForAssertion(() => Assert.IsNotNull(dataSource.LastRequest));
        // Null, not a mode: the run must defer to ParityBench:Retention:Mode rather
        // than freezing whatever the default happened to be when it was created.
        Assert.IsNull(dataSource.LastRequest!.RunRetentionModeOverride);
    }

    private static ResolvedRunProfileView CreateResolvedProfile(RetentionMode? retentionModeOverride) =>
        new ResolvedRunProfileView(
            new Uri("https://qa.example.test/soap"),
            new Uri("https://qa.example.test/json"),
            new ComparisonOptions(ignoreXmlNamespaces: true),
            "C:/runs/client",
            new PluginComparisonSelection("client.customer-lookup", "client.customer-lookup.soap-vs-json"),
            typeof(PluginComparisonFixture),
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            retentionModeOverride);

    private static void StartComparison(IRenderedComponent<RunWorkflow> component) =>
        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Start Comparison", StringComparison.Ordinal))
            .Click();

    [TestMethod]
    public void RunWorkflow_WhenRunProfileIsSelected_IgnoreRulesStudioBrowsesThePluginComparisonType()
    {
        dataSource.RunProfilesToReturn = new[]
        {
            new RunProfileSummary("client-customer-lookup-local", "Client Customer Lookup — Local"),
        };
        dataSource.ResolvedProfile = new ResolvedRunProfileView(
            new Uri("https://qa.example.test/soap"),
            new Uri("https://qa.example.test/json"),
            new ComparisonOptions(ignoreXmlNamespaces: true),
            "C:/runs/client",
            new PluginComparisonSelection("client.customer-lookup", "client.customer-lookup.soap-vs-json"),
            typeof(PluginComparisonFixture),
            new ComparisonRuleDefaults(
                ignoreXmlNamespaces: true,
                ignoreRules: new[] { new IgnoreRuleDefinition("details.traceId") }),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        SelectFirstDropdown(component, "client-customer-lookup-local");

        component.WaitForAssertion(() =>
        {
            // Selecting a plugin run profile must not fall back to the "no
            // browsable model" placeholder — the plugin's comparison type is
            // reflectable even though it isn't in the legacy response-model registry.
            Assert.IsFalse(component.Markup.Contains("Select a registered domain model", StringComparison.Ordinal));
            StringAssert.Contains(component.Markup, nameof(PluginComparisonFixture.CustomerId));
            // The plugin comparison's baseline ignore rules must surface the same
            // way a legacy contract profile's defaults do.
            StringAssert.Contains(component.Markup, "Profile default comparison rules");
            StringAssert.Contains(component.Markup, "profile default ignore rule");
        });
    }

    private sealed class PluginComparisonFixture
    {
        public string CustomerId { get; set; } = string.Empty;
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
            StringAssert.Contains(component.Markup, "Profile Default Ignore Rules");
            StringAssert.Contains(component.Markup, "1 profile default ignore rule will be applied automatically.");
            StringAssert.Contains(component.Markup, "View Rules");
            StringAssert.Contains(component.Markup, "1 profile default mask rule will be applied automatically.");
            Assert.IsFalse(component.Markup.Contains("Profile default mask rules", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void RunWorkflow_WhenProfileHasManyDefaultIgnoreRules_CapsAndFiltersVisibleRules()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        SetAutocompleteValue(component, 0, "SampleSoapCustomerLookupResponseEnvelope");
        SetAutocompleteValue(component, 1, "sample-soap-many-rules");

        component.WaitForAssertion(() =>
        {
            StringAssert.Contains(component.Markup, "31 profile default ignore rules will be applied automatically.");
            StringAssert.Contains(component.Markup, "View Rules");
            Assert.IsFalse(component.Markup.Contains("Rule29", StringComparison.Ordinal));
        });

        FindButtonContaining(component, "View Rules").Click();

        component.WaitForAssertion(() =>
        {
            StringAssert.Contains(component.Markup, "Filter Profile Default Ignore Rules");
            StringAssert.Contains(component.Markup, "Rule00");
            Assert.IsFalse(component.Markup.Contains("Rule29", StringComparison.Ordinal));
            StringAssert.Contains(component.Markup, "Show all 31 rules");
        });

        ChangeTextField(component, "Filter Profile Default Ignore Rules", "Target");

        component.WaitForAssertion(() =>
        {
            StringAssert.Contains(component.Markup, "Target.VisibleRule");
            StringAssert.Contains(component.Markup, "Ignore collection order");
            Assert.IsFalse(component.Markup.Contains("Rule00", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void RunWorkflow_WhenProfileForcesCollectionOrderIgnore_TogglesTheSwitchOnAndDisablesIt()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        SetAutocompleteValue(component, 0, "SampleSoapCustomerLookupResponseEnvelope");
        SetAutocompleteValue(component, 1, "sample-soap-to-json");

        component.WaitForAssertion(() =>
        {
            IElement collectionOrderSwitch = component.FindAll("label.mud-switch")
                .Single(label => label.TextContent.Contains("Ignore Collection Order", StringComparison.Ordinal));
            IElement input = collectionOrderSwitch.QuerySelector("input")!;

            Assert.IsTrue(input.HasAttribute("checked"), "Badge says the profile forces collection-order ignoring, so the switch should show on, not off.");
            Assert.IsTrue(input.HasAttribute("disabled"), "A profile-forced setting can't actually be turned off (the executor always ORs it back in), so the switch should be disabled rather than implying the user has control.");
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


    private static IElement FindButtonContaining<TComponent>(IRenderedComponent<TComponent> component, string text)
        where TComponent : IComponent =>
        component.FindAll("button")
            .First(button => button.TextContent.Contains(text, StringComparison.Ordinal));


    private static IElement FindLastButtonContaining<TComponent>(IRenderedComponent<TComponent> component, string text)
        where TComponent : IComponent =>
        component.FindAll("button")
            .Last(button => button.TextContent.Contains(text, StringComparison.Ordinal));
    private static void ClickTab<TComponent>(IRenderedComponent<TComponent> component, string text)
        where TComponent : IComponent =>
        component.FindAll("[role='tab']")
            .Single(tab => tab.TextContent.Contains(text, StringComparison.Ordinal))
            .Click();

    private static IElement FindCheckboxInput<TComponent>(IRenderedComponent<TComponent> component, string labelText)
        where TComponent : IComponent =>
        component.FindAll("label")
            .Single(label => label.TextContent.Contains(labelText, StringComparison.Ordinal))
            .QuerySelector("input")!;
    private static string? GetTextFieldValue<TComponent>(IRenderedComponent<TComponent> component, string labelText)
        where TComponent : IComponent
    {
        IElement label = component.FindAll("label")
            .Single(element => string.Equals(element.TextContent.Trim(), labelText, StringComparison.Ordinal));
        string? inputId = label.GetAttribute("for");
        Assert.IsFalse(string.IsNullOrWhiteSpace(inputId));
        return component.Find($"#{inputId}").GetAttribute("value");
    }
    private static void SelectPreset(IRenderedComponent<RunWorkflow> component, string presetId)
    {
        IRenderedComponent<MudSelect<string>> select = component.FindComponent<MudSelect<string>>();
        component.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(presetId)).GetAwaiter().GetResult();
    }

    private static void SelectFirstDropdown(IRenderedComponent<RunWorkflow> component, string value)
    {
        IRenderedComponent<MudSelect<string>> select = component.FindComponent<MudSelect<string>>();
        component.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(value)).GetAwaiter().GetResult();
    }

    private static void SelectByLabel(IRenderedComponent<RunWorkflow> component, string label, string value)
    {
        IRenderedComponent<MudSelect<string>> select = component.FindComponents<MudSelect<string>>()
            .Single(candidate => string.Equals(candidate.Instance.Label, label, StringComparison.Ordinal));
        component.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(value)).GetAwaiter().GetResult();
    }

    private static void SetAutocompleteValue(IRenderedComponent<RunWorkflow> component, int index, string value)
    {
        IRenderedComponent<MudAutocomplete<string>> autocomplete = component.FindComponents<MudAutocomplete<string>>()[index];
        component.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(value)).GetAwaiter().GetResult();
    }

    private static void ChangeTextField<TComponent>(IRenderedComponent<TComponent> component, string labelText, string value)
        where TComponent : IComponent
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

        public RequestComparisonRunDefaults RunDefaults { get; set; } = new RequestComparisonRunDefaults();

        public IReadOnlyList<RunProfileSummary> RunProfilesToReturn { get; set; } = Array.Empty<RunProfileSummary>();

        public ResolvedRunProfileView? ResolvedProfile { get; set; }

        public Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDefaults());

        public Task<IReadOnlyList<RunProfileSummary>> ListRunProfilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(RunProfilesToReturn);

        public Task<ResolvedRunProfileView> ResolveRunProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ResolvedProfile ?? throw new InvalidOperationException("No resolved profile configured."));

        public bool StartWasCalled { get; private set; }

        public Type? ResolveResponseModelType(string modelName) => modelName switch
        {
            "ConsumerReportJsonResponse" => typeof(FakeConsumerReportResponse),
            "SampleSoapCustomerLookupResponseEnvelope" => typeof(FakeSoapCustomerLookupResponseEnvelope),
            _ => null,
        };

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

        private RequestComparisonDefaults CreateDefaults() =>
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
                    new ContractProfileOption(
                        "SampleSoapCustomerLookupResponseEnvelope",
                        "sample-soap-many-rules",
                        "1",
                        "sample/customer-lookup/soap",
                        "sample/customer-lookup/json",
                        new ComparisonRuleDefaults(ignoreRules: CreateManyDefaultIgnoreRules())),
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
                        new RequestExecutionOptions(),
                        new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                        new Dictionary<string, string> { ["X-Fixture-Key"] = "fixture-key" }),
                },
                RunDefaults);

        public sealed class FakeConsumerReportResponse
        {
            public FakeSubject Subject { get; set; } = new();
            public List<string> Aliases { get; set; } = new();
            public string ProviderTraceId { get; set; } = string.Empty;
        }

        public sealed class FakeSubject
        {
            public string NationalIdentifier { get; set; } = string.Empty;
        }

        private sealed class FakeSoapCustomerLookupResponseEnvelope
        {
            public string SourceSystem { get; set; } = string.Empty;
            public string SensitiveToken { get; set; } = string.Empty;
        }

        private static IReadOnlyList<IgnoreRuleDefinition> CreateManyDefaultIgnoreRules() =>
            Enumerable
                .Range(0, 30)
                .Select(index => new IgnoreRuleDefinition($"Rule{index:00}"))
                .Append(new IgnoreRuleDefinition("Target.VisibleRule", ignoreCompletely: false, ignoreCollectionOrder: true))
                .ToList();

        private static RunOptions CreateOptions() =>
            new RunOptions(
                new RequestBatchReference("batch-1"),
                new EndpointDefinition(new Uri("https://a.example.test")),
                new EndpointDefinition(new Uri("https://b.example.test")),
                TimeSpan.FromSeconds(30),
                2);
    }
}
