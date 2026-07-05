using Microsoft.AspNetCore.DataProtection;

using MudBlazor.Services;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.UI.Results;
using ParityBench.NET.Workspaces;

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

builder.Services.AddSingleton<IRunStore>(_ => new FileSystemRunStore(workspaceRoot));
builder.Services.AddSingleton<IRunDetailStore>(_ => new FileSystemRunDetailStore(workspaceRoot));
builder.Services.AddSingleton<IRunArtifactStore>(_ => new FileSystemRunArtifactStore(workspaceRoot));
builder.Services.AddScoped<IComparisonRunResultUseCases, ComparisonRunResultService>();
builder.Services.AddScoped<IRunResultsViewDataSource, ApplicationRunResultsViewDataSource>();

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