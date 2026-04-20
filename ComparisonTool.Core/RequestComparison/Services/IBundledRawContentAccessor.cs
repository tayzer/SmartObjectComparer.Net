using ComparisonTool.Core.Comparison.Results;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Provides access to raw-content sidecars bundled with static reports.
/// </summary>
public interface IBundledRawContentAccessor
{
    /// <summary>
    /// Tries to load bundled raw content for the provided pair.
    /// Returns <c>null</c> when no bundled content source is available for the pair.
    /// </summary>
    /// <param name="pair">The comparison pair whose bundled raw content should be resolved.</param>
    /// <returns>The loaded raw content result, or <c>null</c> when no bundled content applies.</returns>
    Task<RawContentResult?> TryLoadAsync(FilePairComparisonResult pair);
}