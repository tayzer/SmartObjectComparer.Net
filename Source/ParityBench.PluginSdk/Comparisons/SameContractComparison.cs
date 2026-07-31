using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.PluginSdk.Comparisons;

/// <summary>
/// The common case: endpoint A and endpoint B are the same API at two versions, so
/// there is one contract, not two. Declare it once and both slots get it.
/// </summary>
/// <remarks>
/// Needs no middleware at all — the built-in mapping step deserializes each
/// response straight into <typeparamref name="TResponse"/> when no plugin step has
/// produced a comparison instance.
/// </remarks>
public sealed class SameContractComparison<TResponse> : IComparisonDefinition<TResponse>
    where TResponse : class
{
    public SameContractComparison(
        string comparisonId,
        string displayName,
        ContractEndpointProfile? endpoint = null,
        ComparisonRuleDefaults? defaultComparisonRules = null,
        IEnumerable<string>? defaultStepIds = null,
        IEnumerable<string>? requiredStepIds = null)
    {
        if (string.IsNullOrWhiteSpace(comparisonId))
        {
            throw new ArgumentException("Comparison id must not be empty.", nameof(comparisonId));
        }

        ComparisonId = comparisonId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? ComparisonId : displayName.Trim();
        Endpoint = endpoint ?? new ContractEndpointProfile(PayloadFormat.Json, "application/json", PayloadFormat.Json);
        DefaultComparisonRules = defaultComparisonRules ?? new ComparisonRuleDefaults();
        DefaultStepIds = (defaultStepIds ?? Array.Empty<string>()).ToArray();
        RequiredStepIds = (requiredStepIds ?? Array.Empty<string>()).ToArray();
    }

    /// <summary>Gets the one contract both endpoint slots speak.</summary>
    public ContractEndpointProfile Endpoint { get; }

    public string ComparisonId { get; }

    public string DisplayName { get; }

    public Type ComparisonType => typeof(TResponse);

    public ContractEndpointProfile EndpointA => Endpoint;

    public ContractEndpointProfile EndpointB => Endpoint;

    public IReadOnlyList<string> DefaultStepIds { get; }

    public IReadOnlyList<string> RequiredStepIds { get; }

    public ComparisonRuleDefaults DefaultComparisonRules { get; }
}
