using System.Windows;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace ComparisonTool.Desktop;

/// <summary>
/// Main window hosting the BlazorWebView.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        blazorWebView.BlazorWebViewInitialized += OnBlazorWebViewInitialized;
    }

    private static void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs args)
    {
        Log.Information(
            "BlazorWebView initialized. WebView2 runtime version: {WebView2Version}",
            CoreWebView2Environment.GetAvailableBrowserVersionString());

        args.WebView.CoreWebView2.ProcessFailed += OnCoreWebView2ProcessFailed;
    }

    private static void OnCoreWebView2ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args)
    {
        Log.Error(
            "WebView2 process failed. Kind={ProcessFailedKind}, Reason={Reason}, ExitCode={ExitCode}, ProcessDescription={ProcessDescription}",
            args.ProcessFailedKind,
            args.Reason,
            args.ExitCode,
            args.ProcessDescription);
        Log.CloseAndFlush();
    }
}
