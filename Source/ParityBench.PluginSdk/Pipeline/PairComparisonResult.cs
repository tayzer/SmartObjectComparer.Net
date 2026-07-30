using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.PluginSdk.Pipeline;

/// <summary>
/// The comparison outcome for one request pair as the pipeline sees it. The host
/// translates this into its persisted result model, so the plugin surface stays
/// free of report, retention and artifact-lifecycle concerns.
/// </summary>
public sealed class PairComparisonResult
{
    private readonly List<ComparisonDifference> differences = new List<ComparisonDifference>();

    public RequestPairOutcome Outcome { get; set; } = RequestPairOutcome.Equal;

    public bool? AreEqual { get; set; }

    public string? ErrorMessage { get; set; }

    public string? OutcomeMessage { get; set; }

    public IReadOnlyList<ComparisonDifference> Differences => differences;

    public void AddDifference(ComparisonDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        differences.Add(difference);
    }

    public void SetDifferences(IEnumerable<ComparisonDifference> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        differences.Clear();
        differences.AddRange(replacement);
    }
}
