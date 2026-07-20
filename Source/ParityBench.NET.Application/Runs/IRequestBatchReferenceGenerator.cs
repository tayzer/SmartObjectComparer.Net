using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

/// <summary>
/// Creates stable request-batch references without exposing storage layout.
/// </summary>
public interface IRequestBatchReferenceGenerator
{
    /// <summary>
    /// Creates the next request-batch reference.
    /// </summary>
    RequestBatchReference CreateReference();
}
