using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Requests;

/// <summary>
/// Incrementally writes pair-level run details without requiring the full run result set in memory.
/// </summary>
public interface IRunDetailWriter : IAsyncDisposable
{
    Task AppendAsync(
        IReadOnlyList<RequestPairResult> results,
        CancellationToken cancellationToken = default);

    Task<RunDetailReference> CompleteAsync(CancellationToken cancellationToken = default);
}
