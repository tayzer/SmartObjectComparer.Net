using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Infrastructure;

public sealed class SameContractProfile : IContractProfile
{
    public SameContractProfile(string responseModelName)
    {
        ResponseModelName = string.IsNullOrWhiteSpace(responseModelName) ? "Auto" : responseModelName.Trim();
        PayloadFormat[] supportedFormats = new[] { PayloadFormat.Json, PayloadFormat.Xml, PayloadFormat.Text };
        EndpointA = new ContractEndpointProfile(PayloadFormat.Text, "text/plain", PayloadFormat.Text, supportedSourceRequestFormats: supportedFormats);
        EndpointB = new ContractEndpointProfile(PayloadFormat.Text, "text/plain", PayloadFormat.Text, supportedSourceRequestFormats: supportedFormats);
    }

    public string ProfileId => ContractProfileSelection.SameContractProfileId;

    public string ResponseModelName { get; }

    public string? ProfileVersion => "1";

    public Type EndpointARequestType => typeof(Stream);

    public Type EndpointBRequestType => typeof(Stream);

    public Type CanonicalResponseType => typeof(Stream);

    public Type EndpointBResponseType => typeof(Stream);

    public ContractEndpointProfile EndpointA { get; }

    public ContractEndpointProfile EndpointB { get; }

    public PayloadFormat CanonicalResponseFormat => PayloadFormat.Text;

    public string CanonicalResponseContentType => "text/plain";

    public IReadOnlyList<IgnoreRuleDefinition> DefaultIgnoreRules => Array.Empty<IgnoreRuleDefinition>();

    public IReadOnlyDictionary<string, string> CanonicalToEndpointResponseMaskPathMap => new Dictionary<string, string>();

    public ValueTask<PreparedContractRequest> PrepareRequestAsync(
        EndpointSlot endpoint,
        ContractRequestPreparationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ContractPayload payload = new ContractPayload(
            context.SourceFormat,
            context.SourceContentType,
            context.OpenSourceRequestBodyAsync,
            context.Request.ContentLength);

        return ValueTask.FromResult(new PreparedContractRequest(payload, ProfileId));
    }

    public ValueTask<NormalizedContractResponse> NormalizeResponseAsync(
        EndpointSlot endpoint,
        ContractResponseNormalizationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ContractPayload payload = new ContractPayload(
            context.SourceFormat,
            string.IsNullOrWhiteSpace(context.ContentType) ? "application/octet-stream" : context.ContentType,
            context.OpenSourceResponseBodyAsync);

        return ValueTask.FromResult(new NormalizedContractResponse(payload, ProfileId));
    }
}
