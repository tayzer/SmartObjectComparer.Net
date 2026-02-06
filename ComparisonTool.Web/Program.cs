using System.Xml.Serialization;
using ComparisonTool.Core.DI;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Web;
using ComparisonTool.Web.Hubs;
using ComparisonTool.Web.Models;
using ComparisonTool.Web.Components;
using ComparisonTool.Web.Services;
using MudBlazor.Services;
using Serilog;

try
{
var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

// Use Serilog for logging
builder.Host.UseSerilog();

// Add services to the container with proper configuration
builder.Services
    .AddUnifiedComparisonServices(builder.Configuration, options =>
    {
        // Register SoapEnvelope with custom root element name
        options.RegisterDomainModelWithRootElement<SoapEnvelope>("SoapEnvelope", "Envelope");
    })
    .AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30);
        options.DisconnectedCircuitMaxRetained = 100;
    });

// Add MudBlazor services
builder.Services.AddMudServices();

// Add HttpClient for request comparison
builder.Services.AddHttpClient("RequestComparison")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
    });

// Add Request Comparison services
builder.Services.AddSingleton<RequestFileParserService>();
builder.Services.AddSingleton<RequestExecutionService>();
builder.Services.AddSingleton<IComparisonProgressPublisher, SignalRProgressPublisher>();
builder.Services.AddSingleton<RequestComparisonJobService>();
builder.Services.AddScoped<ComparisonProgressService>();

builder.Services.Configure<RequestComparisonEndpointOptions>(
    builder.Configuration.GetSection("RequestComparison:EndpointOptions"));

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

ThreadPoolConfig.Configure();

var app = builder.Build();

// Clean up old temp files at startup (moved from upload API to avoid race conditions)
CleanupOldTempFiles();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hub for comparison progress
app.MapHub<ComparisonProgressHub>("/hubs/comparison-progress");

app.MapFileBatchUploadApi();
app.MapRequestComparisonApi();

app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"[FATAL] Application failed to start: {ex}");
    Console.Out.Flush();
    throw;
}

/// <summary>
/// Cleans up temporary upload files older than 1 day.
/// Run at startup to avoid race conditions during parallel uploads.
/// </summary>
static void CleanupOldTempFiles()
{
    var tempPaths = new[]
    {
        Path.Combine(Path.GetTempPath(), "ComparisonToolUploads"),
        Path.Combine(Path.GetTempPath(), "ComparisonToolRequests"),
        Path.Combine(Path.GetTempPath(), "ComparisonToolJobs")
    };

    foreach (var tempPath in tempPaths)
    {
        if (!Directory.Exists(tempPath))
        {
            continue;
        }

        try
        {
            // Delete individual batch folders older than 1 day (not the parent folder)
            foreach (var batchDir in Directory.GetDirectories(tempPath))
            {
                var dirInfo = new DirectoryInfo(batchDir);
                if (dirInfo.CreationTime < DateTime.Now.AddDays(-1))
                {
                    try
                    {
                        Directory.Delete(batchDir, true);
                        Log.Information("Cleaned up old temp batch folder: {Folder}", batchDir);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to clean up temp folder: {Folder}", batchDir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during temp file cleanup for {Path}", tempPath);
        }
    }
}
