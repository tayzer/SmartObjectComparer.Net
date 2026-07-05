using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Requests;

/// <summary>
/// Persists lightweight run detail indexes separately from raw response bodies.
/// </summary>
public interface IRunDetailStore
{
    /// <summary>
    /// Saves pair-level result metadata and returns a logical detail index reference.
    /// </summary>
    Task<RunDetailReference> SaveDetailsAsync(
        RunId runId,
        IReadOnlyList<RequestPairResult> results,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads pair-level result metadata without loading raw response bodies.
    /// </summary>
    Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(
        RunDetailReference detailReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a filtered page of pair-level result metadata without loading raw response bodies.
    /// </summary>
    Task<RunDetailPage> LoadPageAsync(
        RunDetailReference detailReference,
        RunDetailQuery query,
        CancellationToken cancellationToken = default);
}