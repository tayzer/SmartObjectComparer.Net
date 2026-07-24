using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline;

/// <summary>
/// Shared state and failure handling for both pipeline scopes.
/// </summary>
public abstract class PipelineContextBase : IPipelineContext
{
    protected PipelineContextBase(
        string runId,
        RequestItem request,
        IServiceProvider services,
        IPipelineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        RunId = runId;
        Request = request;
        Services = services;
        Configuration = configuration;
    }

    public string RunId { get; }

    public RequestItem Request { get; }

    public IServiceProvider Services { get; }

    public IPipelineConfiguration Configuration { get; }

    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    public bool IsFailed => FailureReason is not null;

    public string? FailureReason { get; private set; }

    // First reason wins: a later step reacting to broken state must not overwrite
    // the original cause with its own symptom.
    public void Fail(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Failure reason must not be empty.", nameof(reason));
        }

        FailureReason ??= reason.Trim();
    }
}

/// <summary>
/// Endpoint-scoped context, one instance per endpoint slot per request pair.
/// </summary>
public sealed class EndpointPipelineContext : PipelineContextBase, IEndpointPipelineContext
{
    private readonly Func<CancellationToken, ValueTask<Stream>> openSourceRequestBodyAsync;

    public EndpointPipelineContext(
        string runId,
        RequestItem request,
        EndpointSlot endpoint,
        EndpointDefinition endpointDefinition,
        PayloadFormat sourceFormat,
        string sourceContentType,
        Func<CancellationToken, ValueTask<Stream>> openSourceRequestBodyAsync,
        IDictionary<string, string> requestHeaders,
        IServiceProvider services,
        IPipelineConfiguration configuration)
        : base(runId, request, services, configuration)
    {
        ArgumentNullException.ThrowIfNull(endpointDefinition);
        ArgumentNullException.ThrowIfNull(openSourceRequestBodyAsync);
        ArgumentNullException.ThrowIfNull(requestHeaders);

        Endpoint = endpoint;
        EndpointDefinition = endpointDefinition;
        SourceFormat = sourceFormat;
        SourceContentType = sourceContentType;
        RequestHeaders = requestHeaders;
        this.openSourceRequestBodyAsync = openSourceRequestBodyAsync;
    }

    public EndpointSlot Endpoint { get; }

    public EndpointDefinition EndpointDefinition { get; }

    public PayloadFormat SourceFormat { get; }

    public string SourceContentType { get; }

    public IDictionary<string, string> RequestHeaders { get; }

    public ContractPayload? RequestBody { get; set; }

    public PipelineTransportResponse? Response { get; set; }

    public ResponseArtifactMetadata? ResponseArtifact { get; set; }

    public object? ComparisonInstance { get; set; }

    /// <summary>
    /// Opens the persisted response. Set by the response-persistence step; a mapping
    /// step calling it before then is a pipeline-ordering error, surfaced as such.
    /// </summary>
    public Func<CancellationToken, ValueTask<Stream>>? OpenResponseArtifact { get; set; }

    public ValueTask<Stream> OpenSourceRequestBodyAsync(CancellationToken cancellationToken = default) =>
        openSourceRequestBodyAsync(cancellationToken);

    public ValueTask<Stream> OpenResponseArtifactAsync(CancellationToken cancellationToken = default) =>
        OpenResponseArtifact is null
            ? throw new InvalidOperationException("The response artifact is not available until the response phase has persisted it.")
            : OpenResponseArtifact(cancellationToken);
}

/// <summary>
/// Pair-scoped context, built from the two completed endpoint contexts.
/// </summary>
public sealed class PairPipelineContext : PipelineContextBase, IPairPipelineContext
{
    public PairPipelineContext(
        string runId,
        RequestItem request,
        IEndpointPipelineContext endpointA,
        IEndpointPipelineContext endpointB,
        ComparisonOptions comparisonOptions,
        IServiceProvider services,
        IPipelineConfiguration configuration)
        : base(runId, request, services, configuration)
    {
        ArgumentNullException.ThrowIfNull(endpointA);
        ArgumentNullException.ThrowIfNull(endpointB);
        ArgumentNullException.ThrowIfNull(comparisonOptions);

        EndpointA = endpointA;
        EndpointB = endpointB;
        ComparisonOptions = comparisonOptions;
    }

    public IEndpointPipelineContext EndpointA { get; }

    public IEndpointPipelineContext EndpointB { get; }

    public object? ComparisonA => EndpointA.ComparisonInstance;

    public object? ComparisonB => EndpointB.ComparisonInstance;

    public ResponseArtifactMetadata? ResponseArtifactA => EndpointA.ResponseArtifact;

    public ResponseArtifactMetadata? ResponseArtifactB => EndpointB.ResponseArtifact;

    public ComparisonOptions ComparisonOptions { get; }

    public PairComparisonResult Result { get; } = new PairComparisonResult();
}
