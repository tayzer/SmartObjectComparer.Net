using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;

using ParityBench.PluginSdk.Configuration;

namespace ParityBench.PluginSdk.Comparisons;

/// <summary>
/// One comparison a plugin offers: the pair of endpoint contracts, the in-memory
/// type both sides are projected onto, and the steps that do the projecting.
/// The desktop app discovers comparisons through this metadata alone.
/// </summary>
public interface IComparisonDefinition
{
    /// <summary>
    /// Gets the stable logical id a run profile selects (e.g.
    /// <c>acme.customer-lookup.soap-vs-json</c>).
    /// </summary>
    string ComparisonId { get; }

    /// <summary>Gets the operator-facing name.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the type both endpoint slots must produce in the mapping phase. Two
    /// instances of this type are what Compare-Net-Objects is handed.
    /// </summary>
    Type ComparisonType { get; }

    /// <summary>Gets endpoint A's contract metadata.</summary>
    ContractEndpointProfile EndpointA { get; }

    /// <summary>Gets endpoint B's contract metadata.</summary>
    ContractEndpointProfile EndpointB { get; }

    /// <summary>
    /// Gets the step ids enabled by default for this comparison, in addition to
    /// the product's built-in steps. A profile may disable any of them.
    /// </summary>
    IReadOnlyList<string> DefaultStepIds { get; }

    /// <summary>
    /// Gets step ids that cannot be disabled because the comparison cannot
    /// produce its comparison type without them.
    /// </summary>
    IReadOnlyList<string> RequiredStepIds { get; }

    /// <summary>
    /// Gets the comparison rules that apply whenever this comparison is selected —
    /// the sane baseline (known-noisy timestamps, trace ids) a profile starts from.
    /// </summary>
    ComparisonRuleDefaults DefaultComparisonRules { get; }
}

/// <summary>
/// Type-safe form of <see cref="IComparisonDefinition"/>.
/// </summary>
public interface IComparisonDefinition<TComparison> : IComparisonDefinition
    where TComparison : class
{
}

/// <summary>
/// Optional per-run tuning of the comparison rules for a comparison type. Kept in
/// terms of <see cref="ComparisonRuleDefaults"/> rather than Compare-Net-Objects
/// settings so the SDK does not put a third-party comparer in the plugin contract.
/// </summary>
public interface IComparisonConfigurator<TComparison>
    where TComparison : class
{
    ComparisonRuleDefaults Configure(ComparisonRuleDefaults defaults, IStepConfiguration configuration);
}
