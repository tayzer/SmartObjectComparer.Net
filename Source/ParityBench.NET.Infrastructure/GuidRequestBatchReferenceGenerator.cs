using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Infrastructure;

public sealed class GuidRequestBatchReferenceGenerator : IRequestBatchReferenceGenerator
{
    public RequestBatchReference CreateReference() => new RequestBatchReference($"batch-{Guid.NewGuid():N}");
}
