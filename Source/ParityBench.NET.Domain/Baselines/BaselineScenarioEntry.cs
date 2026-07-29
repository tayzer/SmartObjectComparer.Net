using System.Collections.ObjectModel;

namespace ParityBench.NET.Domain.Baselines;

/// <summary>
/// One captured scenario: everything needed to re-issue the request against a live
/// endpoint later, plus the comparison model the captured version produced for it.
/// </summary>
public sealed record BaselineScenarioEntry
{
    public BaselineScenarioEntry(
        string relativePath,
        string requestContentType,
        long requestContentLength,
        int statusCode,
        string? responseContentType,
        string canonicalSha256,
        long canonicalContentLength,
        string? rawSha256 = null,
        long rawContentLength = 0,
        IReadOnlyDictionary<string, string>? requestHeaders = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Scenario relative path must not be empty.", nameof(relativePath));
        }

        if (string.IsNullOrWhiteSpace(canonicalSha256))
        {
            throw new ArgumentException("Canonical hash must not be empty.", nameof(canonicalSha256));
        }

        RelativePath = relativePath.Replace('\\', '/').Trim();
        RequestContentType = string.IsNullOrWhiteSpace(requestContentType) ? "text/plain" : requestContentType.Trim();
        RequestContentLength = requestContentLength;
        StatusCode = statusCode;
        ResponseContentType = string.IsNullOrWhiteSpace(responseContentType) ? null : responseContentType.Trim();
        CanonicalSha256 = canonicalSha256.Trim();
        CanonicalContentLength = canonicalContentLength;
        RawSha256 = string.IsNullOrWhiteSpace(rawSha256) ? null : rawSha256.Trim();
        RawContentLength = rawContentLength;

        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (requestHeaders is not null)
        {
            foreach (KeyValuePair<string, string> header in requestHeaders)
            {
                if (!string.IsNullOrWhiteSpace(header.Key))
                {
                    headers[header.Key] = header.Value;
                }
            }
        }

        RequestHeaders = new ReadOnlyDictionary<string, string>(headers);
    }

    public string RelativePath { get; }

    public string RequestContentType { get; }

    public long RequestContentLength { get; }

    public IReadOnlyDictionary<string, string> RequestHeaders { get; }

    public int StatusCode { get; }

    public string? ResponseContentType { get; }

    /// <summary>Gets the hash of the stored comparison model — the side a replay compares against.</summary>
    public string CanonicalSha256 { get; }

    public long CanonicalContentLength { get; }

    /// <summary>
    /// Gets the hash of the stored raw response, when one was kept. Raw bodies are
    /// provenance and inspection material only; replay never compares them.
    /// </summary>
    public string? RawSha256 { get; }

    public long RawContentLength { get; }

    public bool HasRawResponse => RawSha256 is not null;
}
