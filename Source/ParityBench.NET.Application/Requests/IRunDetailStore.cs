using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Requests;

/// <summary>
/// Persists lightweight run detail indexes separately from raw response bodies.
/// </summary>
public interface IRunDetailStore
{
    /// <summary>
    /// Creates an incremental writer for page-oriented run details.
    /// </summary>
    Task<IRunDetailWriter> CreateWriterAsync(
        RunId runId,
        int pageSize = 250,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IRunDetailWriter>(new BufferedRunDetailWriter(this, runId));

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

    Task<StaticReportAnalysisSnapshot?> LoadAnalysisAsync(
        RunDetailReference detailReference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StaticReportAnalysisSnapshot?>(null);

    Task<StaticReportDifferenceIndex?> LoadDifferenceIndexAsync(
        RunDetailReference detailReference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StaticReportDifferenceIndex?>(null);
}