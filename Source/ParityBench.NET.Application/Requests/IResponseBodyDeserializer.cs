using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Application.Requests;

/// <summary>
/// Deserializes response bodies into registered comparison models for a run.
/// </summary>
public interface IResponseBodyDeserializer
{
    /// <summary>
    /// Deserializes a response stream using the requested model name and comparison options.
    /// </summary>
    Task<object> DeserializeAsync(
        string modelName,
        Stream body,
        string? contentType,
        ComparisonOptions comparisonOptions,
        CancellationToken cancellationToken = default);
}
