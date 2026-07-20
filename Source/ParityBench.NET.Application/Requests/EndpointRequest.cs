using System.Collections.ObjectModel;

using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Requests;

public sealed record EndpointRequest
{
    public EndpointRequest(
        EndpointSlot endpoint,
        EndpointDefinition endpointDefinition,
        RequestItem request,
        Stream body,
        string contentType,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(endpointDefinition);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(headers);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }

        Endpoint = endpoint;
        EndpointDefinition = endpointDefinition;
        Request = request;
        Body = body;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "text/plain" : contentType;
        Timeout = timeout;
        Headers = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase));
    }

    public EndpointSlot Endpoint { get; }

    public EndpointDefinition EndpointDefinition { get; }

    public RequestItem Request { get; }

    public Stream Body { get; }

    public string ContentType { get; }

    public TimeSpan Timeout { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }
}
