// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using ComparisonTool.Core.DI;
using ComparisonTool.Web;
using ComparisonTool.Web.Components;
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
    .AddUnifiedComparisonServices(builder.Configuration)
    .AddRazorComponents()
    .AddInteractiveServerComponents(options => {
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30);
        options.DisconnectedCircuitMaxRetained = 100;
    });

builder.Services.AddBlazorBootstrap();

builder.Services.AddSignalR(options => {
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

ThreadPoolConfig.Configure();

var app = builder.Build();

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
