using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Engine;

using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline.BuiltIn;

/// <summary>
/// Comparison phase: diffs the two comparison instances the mapping phase produced
/// and records the outcome. Result-processing steps run after this and can amend
/// what it recorded.
/// </summary>
public sealed class CompareNetObjectsMiddleware : IPairComparisonMiddleware
{
    private readonly DetailedCompareMetricsCollector? timing;

    public CompareNetObjectsMiddleware(DetailedCompareMetricsCollector? timing = null) => this.timing = timing;
    public string StepId => BuiltInStepIds.CompareNetObjects;

    public PipelinePhase Phase => PipelinePhase.Comparison;

    public int Order => 0;

    public async ValueTask InvokeAsync(
        IPairPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken)
    {
        if (context.ComparisonA is null || context.ComparisonB is null)
        {
            context.Result.Outcome = RequestPairOutcome.ExecutionFailed;
            context.Result.ErrorMessage = "One or both endpoints did not produce a comparison instance.";
            context.Fail(context.Result.ErrorMessage);
            return;
        }

        IReadOnlyList<ComparisonDifference> differences = CompareNetObjectsResponseComparer.CompareModels(
            context.ComparisonA,
            context.ComparisonB,
            context.ComparisonOptions,
            timing);

        context.Result.SetDifferences(differences);
        context.Result.AreEqual = differences.Count == 0;
        context.Result.Outcome = differences.Count == 0 ? RequestPairOutcome.Equal : RequestPairOutcome.Different;
        context.Result.OutcomeMessage = differences.Count == 0
            ? "Responses are equal after configured comparison rules."
            : null;

        await next(cancellationToken).ConfigureAwait(false);
    }
}
