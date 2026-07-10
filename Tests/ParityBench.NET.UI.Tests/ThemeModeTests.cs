using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor.Services;

using ParityBench.NET.UI.Theming;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class ThemeModeTests
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
    public void ThemeModeToggle_WhenDarkModeIsClicked_UpdatesThemeState()
    {
        ParityBenchThemeState themeState = testContext.Services.GetRequiredService<ParityBenchThemeState>();
        IRenderedComponent<ThemeModeToggle> component = testContext.Render<ThemeModeToggle>();

        component.Find("button[aria-label='Use dark mode']").Click();

        Assert.AreEqual(ThemeMode.Dark, themeState.Mode);
        Assert.IsTrue(themeState.IsDarkMode);
        Assert.IsTrue(testContext.JSInterop.Invocations.Any(invocation =>
            invocation.Identifier == "parityBenchTheme.setStoredMode"
            && invocation.Arguments.Single()?.ToString() == "Dark"));
    }

    [TestMethod]
    public void ParityBenchThemeProvider_WhenStoredPreferenceExists_UsesStoredPreferenceOverSystem()
    {
        testContext.JSInterop
            .Setup<string?>("parityBenchTheme.getStoredMode")
            .SetResult("Light");
        testContext.JSInterop
            .Setup<bool>("parityBenchTheme.getSystemPrefersDark")
            .SetResult(true);
        ParityBenchThemeState themeState = testContext.Services.GetRequiredService<ParityBenchThemeState>();

        testContext.Render<ParityBenchThemeProvider>(parameters => parameters.AddChildContent("<span>content</span>"));

        Assert.AreEqual(ThemeMode.Light, themeState.Mode);
        Assert.IsFalse(themeState.IsDarkMode);
    }

    [TestMethod]
    public void ParityBenchThemeProvider_WhenNoStoredPreference_UsesSystemPreference()
    {
        testContext.JSInterop
            .Setup<string?>("parityBenchTheme.getStoredMode")
            .SetResult(null);
        testContext.JSInterop
            .Setup<bool>("parityBenchTheme.getSystemPrefersDark")
            .SetResult(true);
        ParityBenchThemeState themeState = testContext.Services.GetRequiredService<ParityBenchThemeState>();

        testContext.Render<ParityBenchThemeProvider>(parameters => parameters.AddChildContent("<span>content</span>"));

        Assert.AreEqual(ThemeMode.System, themeState.Mode);
        Assert.IsTrue(themeState.IsDarkMode);
    }
}
