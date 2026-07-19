using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor.Services;

using ParityBench.NET.UI.Shell;
using ParityBench.NET.UI.Theming;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class ParityBenchProductHeaderTests
{
    private BunitContext testContext = null!;

    [TestInitialize]
    public void SetUp()
    {
        testContext = new BunitContext();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;
        testContext.Services.AddMudServices();
        testContext.Services.AddScoped<ParityBenchThemeState>();
        testContext.RenderTree.Add<MudTestRoot>(parameters => { });
    }

    [TestCleanup]
    public async Task TearDown()
    {
        await testContext.DisposeAsync().ConfigureAwait(false);
    }

    [TestMethod]
    public void Header_WhenRendered_ShowsBrandContextAndOneAccessibleThemeControl()
    {
        IRenderedComponent<ParityBenchProductHeader> component = testContext.Render<ParityBenchProductHeader>(parameters =>
            parameters.Add(header => header.ContextLabel, "Static report"));

        Assert.AreEqual(string.Empty, component.Find("img.pb-brand-mark").GetAttribute("alt"));
        StringAssert.Contains(component.Markup, "ParityBench.NET");
        StringAssert.Contains(component.Markup, "Static report");
        Assert.AreEqual(0, component.FindAll(".mud-appbar-dense").Count);
        Assert.AreEqual(1, component.FindAll("button[aria-label='Use light mode']").Count);
        Assert.AreEqual(1, component.FindAll("button[aria-label='Use dark mode']").Count);
        Assert.AreEqual(1, component.FindAll("button[aria-label='Use system theme']").Count);
    }

    [TestMethod]
    public void Header_WhenDarkModeIsSelected_UsesDarkBrandVariant()
    {
        ParityBenchThemeState themeState = testContext.Services.GetRequiredService<ParityBenchThemeState>();
        themeState.SetMode(ThemeMode.Dark);

        IRenderedComponent<ParityBenchProductHeader> component = testContext.Render<ParityBenchProductHeader>();

        StringAssert.Contains(component.Find("img.pb-brand-mark").GetAttribute("src"), "paritybench-mark-dark.svg");
    }
}
