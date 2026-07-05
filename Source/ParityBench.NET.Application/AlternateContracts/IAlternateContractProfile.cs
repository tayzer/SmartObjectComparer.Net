using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Application.AlternateContracts;

/// <summary>
/// Defines request transformation and response normalization for an alternate endpoint contract.
/// </summary>
public interface IAlternateContractProfile
{
    /// <summary>
    /// Gets the stable logical id used by a run to select this profile.
    /// </summary>
    string ProfileId { get; }

    /// <summary>
    /// Gets the canonical response model name this profile normalizes into.
    /// </summary>
    string CanonicalModelName { get; }

    /// <summary>
    /// Gets the CLR type used to deserialize the source request.
    /// </summary>
    Type CanonicalRequestType { get; }

    /// <summary>
    /// Gets the CLR type produced for the endpoint B request.
    /// </summary>
    Type AlternateRequestType { get; }

    /// <summary>
    /// Gets the CLR type used for canonical response comparison.
    /// </summary>
    Type CanonicalResponseType { get; }

    /// <summary>
    /// Gets the CLR type used to deserialize endpoint B responses.
    /// </summary>
    Type AlternateResponseType { get; }

    /// <summary>
    /// Gets the source request formats this profile can transform.
    /// </summary>
    IReadOnlyCollection<PayloadFormat> SupportedSourceRequestFormats { get; }

    /// <summary>
    /// Gets the payload format sent to endpoint B.
    /// </summary>
    PayloadFormat AlternateRequestFormat { get; }

    /// <summary>
    /// Gets the content type sent to endpoint B after transformation.
    /// </summary>
    string AlternateRequestContentType { get; }

    /// <summary>
    /// Gets the response format expected from endpoint B.
    /// </summary>
    PayloadFormat AlternateResponseFormat { get; }

    /// <summary>
    /// Gets the normalized payload format persisted for comparison.
    /// </summary>
    PayloadFormat CanonicalResponseFormat { get; }

    /// <summary>
    /// Gets the normalized content type persisted for comparison.
    /// </summary>
    string CanonicalResponseContentType { get; }

    /// <summary>
    /// Gets an optional endpoint A suggestion a host can use when preselecting endpoints.
    /// </summary>
    string? SuggestedEndpointAId { get; }

    /// <summary>
    /// Gets an optional endpoint B suggestion a host can use when preselecting endpoints.
    /// </summary>
    string? SuggestedEndpointBId { get; }

    /// <summary>
    /// Gets comparison ignore rules that should apply whenever this profile is selected.
    /// </summary>
    IReadOnlyList<IgnoreRuleDefinition> DefaultIgnoreRules { get; }

    /// <summary>
    /// Gets canonical-to-alternate response mask path mappings owned by this profile.
    /// </summary>
    IReadOnlyDictionary<string, string> CanonicalToAlternateResponseMaskPathMap { get; }

    /// <summary>
    /// Prepares the endpoint B request payload from a canonical source request body.
    /// </summary>
    ValueTask<PreparedAlternateContractRequest> PrepareEndpointBRequestAsync(
        AlternateContractRequestPreparationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes endpoint A's response into the canonical comparison payload.
    /// </summary>
    ValueTask<NormalizedAlternateContractResponse> NormalizeEndpointAResponseAsync(
        AlternateContractResponseNormalizationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes endpoint B's alternate response into the canonical comparison payload.
    /// </summary>
    ValueTask<NormalizedAlternateContractResponse> NormalizeEndpointBResponseAsync(
        AlternateContractResponseNormalizationContext context,
        CancellationToken cancellationToken = default);
}
