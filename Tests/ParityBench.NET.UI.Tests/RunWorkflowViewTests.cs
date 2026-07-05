using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor.Services;

using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.UI.Workflow;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class RunWorkflowViewTests
{
    private BunitContext testContext = null!;

    [TestInitialize]
    public void SetUp()
    {
        testContext = new BunitContext();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;
        testContext.Services.AddMudServices();
        testContext.Services.AddSingleton<IRunWorkflowViewDataSource>(new FakeRunWorkflowViewDataSource());
    }

    [TestCleanup]
    public async Task TearDown()
    {
        await testContext.DisposeAsync().ConfigureAwait(false);
    }

    [TestMethod]
    public void RunWorkflow_WhenRequiredInputsAreMissing_ShowsRecoverableError()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Start", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Endpoint A must be an absolute URL."));
    }

    private sealed class FakeRunWorkflowViewDataSource : IRunWorkflowViewDataSource
    {
        public Task<ComparisonRun> CreateRunFromDirectoryAsync(
            RequestComparisonRunRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> StartRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
    }
}