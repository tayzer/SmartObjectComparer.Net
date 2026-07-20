using Microsoft.AspNetCore.DataProtection;

using MudBlazor.Services;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Composition;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.UI.Theming;
using ParityBench.NET.UI.Workflow;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

string workspaceRoot = builder.Configuration["ParityBench:WorkspaceRoot"]
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ParityBench.NET",
        "Workspace");
Directory.CreateDirectory(workspaceRoot);

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(workspaceRoot, "data-protection-keys")));

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();
builder.Services.AddScoped<ParityBenchThemeState>();
string fixtureBaseUrl = builder.Configuration["ParityBench:RequestDefaults:FixtureBaseUrl"]
    ?? RequestComparisonFixtureDefaults.DefaultFixtureBaseUrl;
RegisterV2Services(builder.Services, workspaceRoot, fixtureBaseUrl, builder.Configuration["ParityBench:AcceptedDifferences:StorePath"], builder.Configuration);

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

static void RegisterV2Services(IServiceCollection services, string workspaceRoot, string fixtureBaseUrl, string? acceptedDifferenceStorePath, IConfiguration configuration)
{
    services.AddParityBenchWorkspaceServices(configuration, workspaceRoot, fixtureBaseUrl);
    services.AddParityBenchUiServices(workspaceRoot, acceptedDifferenceStorePath);
    services.AddScoped<IRequestSourcePicker, NoOpRequestSourcePicker>();
}
