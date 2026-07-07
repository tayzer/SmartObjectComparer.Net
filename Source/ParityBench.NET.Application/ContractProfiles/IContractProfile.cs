using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.ContractProfiles;

/// <summary>
/// Defines request preparation and response normalization for a selectable contract profile.
/// </summary>
public interface IContractProfile
{
    /// <summary>
    /// Gets the stable logical id used by a run to select this profile.
    /// </summary>
    string ProfileId { get; }

    /// <summary>
    /// Gets the canonical response model name this profile normalizes into.
    /// </summary>
    string ResponseModelName { get; }

    /// <summary>
    /// Gets the optional profile schema or implementation version.
    /// </summary>
    string? ProfileVersion { get; }

    /// <summary>
    /// Gets the CLR type used to deserialize the source request.
    /// </summary>
    Type EndpointARequestType { get; }

    /// <summary>
    /// Gets the CLR type produced for the endpoint B request.
    /// </summary>
    Type EndpointBRequestType { get; }

    /// <summary>
    /// Gets the CLR type used for canonical response comparison.
    /// </summary>
    Type CanonicalResponseType { get; }

    /// <summary>
    /// Gets the CLR type used to deserialize endpoint B responses.
    /// </summary>
    Type EndpointBResponseType { get; }

    /// <summary>
    /// Gets endpoint A contract metadata.
    /// </summary>
    ContractEndpointProfile EndpointA { get; }

    /// <summary>
    /// Gets endpoint B contract metadata.
    /// </summary>
    ContractEndpointProfile EndpointB { get; }

    /// <summary>
    /// Gets the normalized payload format persisted for comparison.
    /// </summary>
    PayloadFormat CanonicalResponseFormat { get; }

    /// <summary>
    /// Gets the normalized content type persisted for comparison.
    /// </summary>
    string CanonicalResponseContentType { get; }

    /// <summary>
    /// Gets comparison ignore rules that should apply whenever this profile is selected.
    /// </summary>
    IReadOnlyList<IgnoreRuleDefinition> DefaultIgnoreRules { get; }

    /// <summary>
    /// Gets comparison defaults that should apply whenever this profile is selected.
    /// </summary>
    ComparisonRuleDefaults DefaultComparisonRules { get; }

    /// <summary>
    /// Gets canonical-to-endpoint response mask path mappings owned by this profile.
    /// </summary>
    IReadOnlyDictionary<string, string> CanonicalToEndpointResponseMaskPathMap { get; }

    /// <summary>
    /// Prepares the outbound request for the selected endpoint.
    /// </summary>
    ValueTask<PreparedContractRequest> PrepareRequestAsync(
        EndpointSlot endpoint,
        ContractRequestPreparationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes an endpoint response into the canonical comparison payload.
    /// </summary>
    ValueTask<NormalizedContractResponse> NormalizeResponseAsync(
        EndpointSlot endpoint,
        ContractResponseNormalizationContext context,
        CancellationToken cancellationToken = default);
}
