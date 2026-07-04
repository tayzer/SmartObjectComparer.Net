using System.Collections.ObjectModel;

namespace ParityBench.NET.Domain.Requests;

public sealed record RequestItem
{
    public RequestItem(
        string relativePath,
        string contentType = "text/plain",
        long contentLength = 0,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyDictionary<string, string>? headersA = null,
        IReadOnlyDictionary<string, string>? headersB = null)
    {
        if (contentLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contentLength), "Content length must not be negative.");
        }

        RelativePath = NormalizeRelativePath(relativePath);
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "text/plain" : contentType;
        ContentLength = contentLength;
        Headers = CopyHeaders(headers);
        HeadersA = CopyHeaders(headersA);
        HeadersB = CopyHeaders(headersB);
    }

    public string RelativePath { get; }

    public string ContentType { get; }

    public long ContentLength { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public IReadOnlyDictionary<string, string> HeadersA { get; }

    public IReadOnlyDictionary<string, string> HeadersB { get; }

    public IReadOnlyDictionary<string, string> GetHeaders(EndpointSlot endpoint) =>
        endpoint == EndpointSlot.A ? HeadersA : HeadersB;

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Request relative path must not be empty.", nameof(relativePath));
        }

        string normalized = relativePath.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized) || normalized.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Request relative path must not be rooted.", nameof(relativePath));
        }

        string[] parts = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part != ".")
            .ToArray();

        if (parts.Length == 0)
        {
            throw new ArgumentException("Request relative path must include a file name.", nameof(relativePath));
        }

        if (parts.Any(part => part == ".."))
        {
            throw new ArgumentException("Request relative path must not contain parent traversal.", nameof(relativePath));
        }

        return string.Join('/', parts);
    }

    private static IReadOnlyDictionary<string, string> CopyHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        Dictionary<string, string> copiedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is not null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (!string.IsNullOrWhiteSpace(header.Key))
                {
                    copiedHeaders[header.Key] = header.Value;
                }
            }
        }

        return new ReadOnlyDictionary<string, string>(copiedHeaders);
    }
}
