using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using MudBlazor.Services;

using ParityBench.NET.Report;
using ParityBench.NET.Report.Results;
using ParityBench.NET.UI.Results;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<ReportRoot>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IRunResultsViewDataSource, StaticReportRunResultsViewDataSource>();

await builder.Build().RunAsync().ConfigureAwait(false);
