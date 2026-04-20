using System.Text.Json;
using ComparisonTool.Core.Serialization.BlazorReport;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Report.Services;

/// <summary>
/// Service that loads the packaged report JSON and deserializes it into the domain objects
/// that the Blazor UI components consume.
/// </summary>
public sealed class ReportDataService
{
    private const string BootstrapDataFileName = "report.data.json";

    private readonly HttpClient httpClient;
    private readonly ILogger<ReportDataService> logger;
    private ReportBootstrapData? cachedData;
    private bool isLoaded;

    public ReportDataService(HttpClient httpClient, ILogger<ReportDataService> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the loaded report data, or null if not yet loaded.
    /// </summary>
    public ReportBootstrapData? Data => cachedData;

    /// <summary>
    /// Whether the report data has been loaded and deserialized.
    /// </summary>
    public bool IsLoaded => isLoaded;

    /// <summary>
    /// Loads and deserializes the report data from the packaged JSON file.
    /// </summary>
    public async Task<ReportBootstrapData> LoadAsync()
    {
        if (isLoaded && cachedData != null)
        {
            return cachedData;
        }

        try
        {
            await using var stream = await this.httpClient.GetStreamAsync(BootstrapDataFileName).ConfigureAwait(false);

            cachedData = await JsonSerializer.DeserializeAsync<ReportBootstrapData>(stream, BlazorReportSerializerOptions.Default).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to deserialize report data.");

            isLoaded = true;
            this.logger.LogInformation(
                "Report data loaded: {PairCount} pairs, AllEqual={AllEqual}",
                cachedData.Result?.TotalPairsCompared ?? 0,
                cachedData.Result?.AllEqual ?? true);

            return cachedData;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to load report data from {BootstrapDataFileName}.", BootstrapDataFileName);
            throw;
        }
    }
}
