// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Xml.Serialization;
using ComparisonTool.Core.DI;
using ComparisonTool.Core.Models;
using ComparisonTool.Web;
using ComparisonTool.Web.Components;
using MudBlazor.Services;
using Serilog;

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
    .AddUnifiedComparisonServices(builder.Configuration, options => {
        // Register SoapEnvelope with custom root element name
        options.RegisterDomainModelWithRootElement<SoapEnvelope>("SoapEnvelope", "Envelope");
    })
    .AddRazorComponents()
    .AddInteractiveServerComponents(options => {
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30);
        options.DisconnectedCircuitMaxRetained = 100;
    });

// Add MudBlazor services
builder.Services.AddMudServices();

builder.Services.AddSignalR(options => {
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
if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
else {
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapFileBatchUploadApi();

app.Run();

/// <summary>
/// Cleans up temporary upload files older than 1 day.
/// Run at startup to avoid race conditions during parallel uploads.
/// </summary>
static void CleanupOldTempFiles() {
    var tempPath = Path.Combine(Path.GetTempPath(), "ComparisonToolUploads");
    if (!Directory.Exists(tempPath)) {
        return;
    }

    try {
        // Delete individual batch folders older than 1 day (not the parent folder)
        foreach (var batchDir in Directory.GetDirectories(tempPath)) {
            var dirInfo = new DirectoryInfo(batchDir);
            if (dirInfo.CreationTime < DateTime.Now.AddDays(-1)) {
                try {
                    Directory.Delete(batchDir, true);
                    Log.Information("Cleaned up old temp batch folder: {Folder}", batchDir);
                }
                catch (Exception ex) {
                    Log.Warning(ex, "Failed to clean up temp folder: {Folder}", batchDir);
                }
            }
        }
    }
    catch (Exception ex) {
        Log.Warning(ex, "Error during temp file cleanup");
    }
}
