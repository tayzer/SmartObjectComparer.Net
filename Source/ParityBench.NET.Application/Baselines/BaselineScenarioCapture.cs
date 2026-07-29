namespace ParityBench.NET.Application.Baselines;

/// <summary>
/// One scenario handed to the store during capture. The three payloads are opened
/// lazily so a capture run never holds more than one scenario's bodies in memory.
/// </summary>
public sealed record BaselineScenarioCapture
{
    public BaselineScenarioCapture(
        string relativePath,
        string requestContentType,
        IReadOnlyDictionary<string, string> requestHeaders,
        int statusCode,
        string? responseContentType,
        Func<CancellationToken, Task<Stream>> openRequestBodyAsync,
        Func<CancellationToken, Task<Stream>> openCanonicalBodyAsync,
        Func<CancellationToken, Task<Stream>>? openRawBodyAsync = null)
    {
        ArgumentNullException.ThrowIfNull(openRequestBodyAsync);
        ArgumentNullException.ThrowIfNull(openCanonicalBodyAsync);

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Scenario relative path must not be empty.", nameof(relativePath));
        }

        RelativePath = relativePath;
        RequestContentType = requestContentType;
        RequestHeaders = requestHeaders;
        StatusCode = statusCode;
        ResponseContentType = responseContentType;
        OpenRequestBodyAsync = openRequestBodyAsync;
        OpenCanonicalBodyAsync = openCanonicalBodyAsync;
        OpenRawBodyAsync = openRawBodyAsync;
    }

    public string RelativePath { get; }

    public string RequestContentType { get; }

    public IReadOnlyDictionary<string, string> RequestHeaders { get; }

    public int StatusCode { get; }

    public string? ResponseContentType { get; }

    public Func<CancellationToken, Task<Stream>> OpenRequestBodyAsync { get; }

    public Func<CancellationToken, Task<Stream>> OpenCanonicalBodyAsync { get; }

    public Func<CancellationToken, Task<Stream>>? OpenRawBodyAsync { get; }
}
