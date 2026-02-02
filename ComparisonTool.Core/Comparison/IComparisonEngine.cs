// <copyright file="IComparisonEngine.cs" company="PlaceholderCompany">



using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Interface for the core comparison engine that handles object-to-object comparisons.
/// </summary>
public interface IComparisonEngine {
    /// <summary>
    /// Performs a comparison between two objects using the specified model type.
    /// </summary>
    /// <param name="oldResponse">The old/reference object.</param>
    /// <param name="newResponse">The new/comparison object.</param>
    /// <param name="modelType">The type of the model being compared.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Comparison result with differences.</returns>
    Task<ComparisonResult> CompareObjectsAsync(
        object oldResponse,
        object newResponse,
        Type modelType,
        CancellationToken cancellationToken = default);
}
