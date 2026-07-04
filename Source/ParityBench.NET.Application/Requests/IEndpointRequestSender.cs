namespace ParityBench.NET.Application.Requests;

/// <summary>
/// Sends one staged request body to a configured endpoint and returns a streaming response.
/// </summary>
public interface IEndpointRequestSender
{
    /// <summary>
    /// Sends the request and returns a response whose body stream must be disposed by the caller.
    /// </summary>
    Task<EndpointResponse> SendAsync(
        EndpointRequest request,
        CancellationToken cancellationToken = default);
}
