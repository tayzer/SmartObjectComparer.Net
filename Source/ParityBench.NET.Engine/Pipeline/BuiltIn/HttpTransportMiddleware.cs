using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.ContractProfiles;

using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline.BuiltIn;

/// <summary>
/// Transport phase: sends the prepared request and exposes the response to the
/// later phases.
/// </summary>
/// <remarks>
/// The response is published to the context and the rest of the pipeline is
/// invoked <em>inside</em> the response's lifetime, so the response body is
/// streamed straight into persistence instead of being buffered. That is what
/// keeps a run's memory bounded by concurrency rather than by response size.
/// </remarks>
public sealed class HttpTransportMiddleware : IEndpointComparisonMiddleware
{
    private readonly IEndpointRequestSender endpointRequestSender;
    private readonly TimeSpan timeout;

    public HttpTransportMiddleware(IEndpointRequestSender endpointRequestSender, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(endpointRequestSender);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }

        this.endpointRequestSender = endpointRequestSender;
        this.timeout = timeout;
    }

    public string StepId => BuiltInStepIds.HttpTransport;

    public PipelinePhase Phase => PipelinePhase.Transport;

    public int Order => 0;

    public async ValueTask InvokeAsync(
        IEndpointPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken)
    {
        ContractPayload requestBody = context.RequestBody
            ?? throw new InvalidOperationException(
                $"No request body was prepared for endpoint {context.Endpoint} of '{context.Request.RelativePath}'.");

        await using Stream bodyStream = await requestBody.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        EndpointRequest endpointRequest = new EndpointRequest(
            context.Endpoint,
            context.EndpointDefinition,
            context.Request,
            bodyStream,
            requestBody.ContentType,
            timeout,
            new Dictionary<string, string>(context.RequestHeaders, StringComparer.OrdinalIgnoreCase));

        await using EndpointResponse response = await endpointRequestSender
            .SendAsync(endpointRequest, cancellationToken)
            .ConfigureAwait(false);

        // The body read is bounded by the request timeout as well as the run's own
        // cancellation: headers arriving quickly says nothing about the body.
        using CancellationTokenSource bodyReadTimeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, response.Timeout);

        context.Response = new PipelineTransportResponse(
            response.StatusCode,
            response.ContentType,
            new ContractPayload(
                PayloadFormat.Text,
                response.ContentType ?? "application/octet-stream",
                _ => ValueTask.FromResult(response.Body)));

        await next(bodyReadTimeoutSource.Token).ConfigureAwait(false);
    }
}
