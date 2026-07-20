using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine.Comparers;

public sealed class HashOnlyResponseComparer : IResponseComparer
{
    public Task<RequestPairResult> CompareAsync(
        RequestItem request,
        RunOptions options,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RequestPairResult.Classify(request, responseA, responseB, errorMessage));
}
