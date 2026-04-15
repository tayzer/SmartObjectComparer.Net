using Blazored.LocalStorage;
using ComparisonTool.Core.Abstractions;
using ComparisonTool.Core.DI;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Report.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<ComparisonTool.Report.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register core comparison services (for model deserialization and analysis types)
builder.Services.AddUnifiedComparisonServices(builder.Configuration, options =>
{
    options.RegisterDomainModelWithRootElement<SoapEnvelope>("SoapEnvelope", "Envelope");
});

// MudBlazor + local storage
builder.Services.AddMudServices();
builder.Services.AddBlazoredLocalStorage();

// Report data service - reads and deserializes the embedded report JSON
builder.Services.AddSingleton<ReportDataService>();

// Platform service stubs for read-only report context
builder.Services.AddSingleton<IFileExportService, WasmFileExportService>();
builder.Services.AddSingleton<IFolderPickerService, WasmFolderPickerService>();
builder.Services.AddSingleton<INotificationService, WasmNotificationService>();
builder.Services.AddScoped<IScrollService, WasmScrollService>();
builder.Services.AddScoped<IProgressSubscriber, WasmProgressSubscriber>();
builder.Services.AddSingleton<IRequestComparisonGateway, WasmRequestComparisonGateway>();
builder.Services.AddScoped<RawContentService>();

await builder.Build().RunAsync();
