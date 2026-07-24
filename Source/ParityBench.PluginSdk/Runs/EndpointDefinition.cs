using System.Collections.ObjectModel;

namespace ParityBench.NET.Domain.Runs;

public sealed record EndpointDefinition
{
    public EndpointDefinition(
        Uri uri,
        string? label = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(uri);

        Dictionary<string, string> copiedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is not null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                copiedHeaders[header.Key] = header.Value;
            }
        }

        Uri = uri;
        Label = string.IsNullOrWhiteSpace(label) ? null : label;
        Headers = new ReadOnlyDictionary<string, string>(copiedHeaders);
    }

    public Uri Uri { get; }

    public string? Label { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }
}
