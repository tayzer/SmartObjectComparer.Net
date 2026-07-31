using System.Net.Http.Headers;

using ParityBench.NET.Application.Requests;

namespace ParityBench.NET.Infrastructure;

public sealed class HttpClientEndpointRequestSender : IEndpointRequestSender
{
    private readonly HttpClient httpClient;

    public HttpClientEndpointRequestSender(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<EndpointResponse> SendAsync(
        EndpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(request.Timeout);

        try
        {
            using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, request.EndpointDefinition.Uri);
            message.Content = new StreamContent(request.Body);
            ApplyContentType(message.Content.Headers, request.ContentType);
            ApplyHeaders(message, request.Headers);

            HttpResponseMessage response = await httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);

            Stream body = await response.Content
                .ReadAsStreamAsync(timeoutSource.Token)
                .ConfigureAwait(false);

            return new EndpointResponse(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                body,
                new IDisposable[] { response, timeoutSource },
                timeoutSource.Token);
        }
        catch
        {
            timeoutSource.Dispose();
            throw;
        }
    }

    private static void ApplyContentType(HttpContentHeaders headers, string contentType)
    {
        // Drop whatever is there first — parsed and unparsed values alike. Without
        // this, two unparseable values (the payload's, then a Content-Type header's)
        // both get appended and two Content-Type headers go on the wire.
        headers.Remove("Content-Type");

        if (MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsedContentType))
        {
            headers.ContentType = parsedContentType;
            return;
        }

        headers.TryAddWithoutValidation("Content-Type", contentType);
    }

    private static void ApplyHeaders(HttpRequestMessage message, IReadOnlyDictionary<string, string> headers)
    {
        foreach (KeyValuePair<string, string> header in headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                ApplyContentType(message.Content!.Headers, header.Value);
                continue;
            }

            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                message.Content!.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }
}
