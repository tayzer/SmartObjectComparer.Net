using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor;
using MudBlazor.Services;

using ParityBench.NET.UI.Results;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class AlignedSideBySideDiffTests
{
    private BunitContext testContext = null!;

    [TestInitialize]
    public void SetUp()
    {
        testContext = new BunitContext();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;
        testContext.Services.AddMudServices();
        testContext.RenderTree.Add<MudTestRoot>(parameters => { });
    }

    [TestCleanup]
    public async Task TearDown()
    {
        if (testContext is not null)
        {
            await testContext.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void LeadingInsertion_RendersPairedRowsWithImaginaryPlaceholder()
    {
        IRenderedComponent<AlignedSideBySideDiff> component = Render(
            "alpha\nbravo\ncharlie",
            "inserted\nalpha\nbravo\ncharlie");

        var panels = component.FindAll(".aligned-diff-panel");
        var leftRows = panels[0].QuerySelectorAll("[data-diff-row]");
        var rightRows = panels[1].QuerySelectorAll("[data-diff-row]");

        Assert.AreEqual(rightRows.Length, leftRows.Length);
        CollectionAssert.AreEqual(
            leftRows.Select(row => row.GetAttribute("data-diff-row")).ToList(),
            rightRows.Select(row => row.GetAttribute("data-diff-row")).ToList());
        Assert.IsTrue(leftRows.Any(row => row.ClassList.Contains("aligned-diff-line-imaginary")));
        Assert.IsTrue(rightRows.Any(row => row.ClassList.Contains("aligned-diff-line-inserted")));
        Assert.AreEqual(string.Empty, leftRows[0].QuerySelector(".aligned-diff-line-number")?.TextContent);
        Assert.AreEqual("1", rightRows[0].QuerySelector(".aligned-diff-line-number")?.TextContent);
    }

    [TestMethod]
    public void MiddleInsertion_PreservesLaterLineAlignmentAndLineNumbers()
    {
        IRenderedComponent<AlignedSideBySideDiff> component = Render(
            "alpha\nbravo\ncharlie",
            "alpha\ninserted\nbravo\ncharlie");

        var panels = component.FindAll(".aligned-diff-panel");
        var leftRows = panels[0].QuerySelectorAll("[data-diff-row]");
        var rightRows = panels[1].QuerySelectorAll("[data-diff-row]");

        Assert.AreEqual(4, leftRows.Length);
        Assert.IsTrue(leftRows[1].ClassList.Contains("aligned-diff-line-imaginary"));
        Assert.AreEqual("2", rightRows[1].QuerySelector(".aligned-diff-line-number")?.TextContent);
        Assert.AreEqual("2", leftRows[2].QuerySelector(".aligned-diff-line-number")?.TextContent);
        Assert.AreEqual("3", rightRows[2].QuerySelector(".aligned-diff-line-number")?.TextContent);
        Assert.AreEqual("bravo", leftRows[2].QuerySelector(".aligned-diff-line-text")?.TextContent);
        Assert.AreEqual("bravo", rightRows[2].QuerySelector(".aligned-diff-line-text")?.TextContent);
    }

    [TestMethod]
    public void ModifiedLine_RendersCharacterLevelHighlights()
    {
        IRenderedComponent<AlignedSideBySideDiff> component = Render("customer: Alice", "customer: Alicia");

        Assert.AreEqual(1, component.FindAll(".aligned-diff-char-deleted").Count);
        Assert.AreEqual(1, component.FindAll(".aligned-diff-char-inserted").Count);
        Assert.AreEqual("customer: Alice", component.FindAll(".aligned-diff-line-text")[0].TextContent);
        Assert.AreEqual("customer: Alicia", component.FindAll(".aligned-diff-line-text")[1].TextContent);
    }

    [TestMethod]
    public void EmptyLeftSide_StillRendersAlignedRows()
    {
        IRenderedComponent<AlignedSideBySideDiff> component = Render(string.Empty, "only on endpoint B");

        Assert.AreEqual(1, component.FindAll(".aligned-diff-line-imaginary").Count);
        StringAssert.Contains(component.Markup, "only on endpoint B");
    }

    [TestMethod]
    public void WrapChange_RebindsDefaultEnabledScrollSync()
    {
        IRenderedComponent<AlignedSideBySideDiff> component = Render("short", "a much longer changed value");

        component.WaitForAssertion(() => Assert.IsTrue(SyncInvocationCount() >= 1));
        IRenderedComponent<MudSwitch<bool>> wrapSwitch = component.FindComponents<MudSwitch<bool>>().Last();
        component.InvokeAsync(() => wrapSwitch.Instance.ValueChanged.InvokeAsync(true)).GetAwaiter().GetResult();

        component.WaitForAssertion(() => Assert.IsTrue(SyncInvocationCount() >= 2));
        StringAssert.Contains(component.Markup, "aligned-diff-wrap");
    }

    [TestMethod]
    public void ContentChange_RebuildsRowsAndRebindsScrollSync()
    {
        IRenderedComponent<AlignedSideBySideDiff> component = Render("alpha", "alpha");
        component.WaitForAssertion(() => Assert.IsTrue(SyncInvocationCount() >= 1));
        int initialInvocationCount = SyncInvocationCount();

        component.Render(parameters => parameters
            .Add(diff => diff.ContentA, "alpha\nbravo")
            .Add(diff => diff.ContentB, "alpha\nchanged"));

        component.WaitForAssertion(() => Assert.IsTrue(SyncInvocationCount() > initialInvocationCount));
        Assert.AreEqual(2, component.FindAll(".aligned-diff-panel")[0].QuerySelectorAll("[data-diff-row]").Length);
    }

    private IRenderedComponent<AlignedSideBySideDiff> Render(string contentA, string contentB) =>
        testContext.Render<AlignedSideBySideDiff>(parameters => parameters
            .Add(component => component.ContentA, contentA)
            .Add(component => component.ContentB, contentB)
            .Add(component => component.LabelA, "Expected")
            .Add(component => component.LabelB, "Actual")
            .Add(component => component.FileNameA, "expected.json")
            .Add(component => component.FileNameB, "actual.json"));

    private int SyncInvocationCount() => testContext.JSInterop.Invocations.Count(invocation =>
        invocation.Identifier == "parityBenchSetSyncedScroll" &&
        invocation.Arguments.Count == 3 &&
        invocation.Arguments[2] is true);
}
