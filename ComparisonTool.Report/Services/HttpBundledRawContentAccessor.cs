using System.Text.Json;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Core.Serialization.BlazorReport;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Report.Services;

/// <summary>
/// Loads bundled raw-content sidecars for static Blazor reports.
/// </summary>
public sealed class HttpBundledRawContentAccessor : IBundledRawContentAccessor
{
    private readonly HttpClient httpClient;
    private readonly ILogger<HttpBundledRawContentAccessor> logger;

    public HttpBundledRawContentAccessor(HttpClient httpClient, ILogger<HttpBundledRawContentAccessor> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<RawContentResult?> TryLoadAsync(FilePairComparisonResult pair)
    {
        if (string.IsNullOrWhiteSpace(pair.BundledRawContentPath))
        {
            return null;
        }

        try
        {
            await using var stream = await this.httpClient.GetStreamAsync(pair.BundledRawContentPath).ConfigureAwait(false);
            var data = await JsonSerializer.DeserializeAsync<BundledRawContentData>(stream, BlazorReportSerializerOptions.Default).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to deserialize bundled raw content.");

            if (!string.IsNullOrWhiteSpace(data.ErrorMessage))
            {
                return new RawContentResult
                {
                    ErrorMessage = data.ErrorMessage,
                };
            }

            return new RawContentResult
            {
                ContentA = data.ContentA,
                ContentB = data.ContentB,
                IsTruncatedA = data.IsTruncatedA,
                IsTruncatedB = data.IsTruncatedB,
                IsLoaded = true,
            };
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Failed to load bundled raw content from {BundledRawContentPath}.", pair.BundledRawContentPath);

            return new RawContentResult
            {
                ErrorMessage = $"Failed to load bundled raw content: {ex.Message}",
            };
        }
    }
}