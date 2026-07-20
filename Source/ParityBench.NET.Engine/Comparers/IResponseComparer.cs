using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

/// <summary>
/// Classifies a persisted response pair using the comparison behavior configured for a run.
/// </summary>
public interface IResponseComparer
{
    /// <summary>
    /// Compares or classifies a response pair and returns lightweight pair metadata.
    /// </summary>
    Task<RequestPairResult> CompareAsync(
        RequestItem request,
        RunOptions options,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage,
        CancellationToken cancellationToken = default);
}
