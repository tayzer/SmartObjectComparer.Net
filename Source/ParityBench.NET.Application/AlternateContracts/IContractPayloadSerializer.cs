using ParityBench.NET.Domain.AlternateContracts;

namespace ParityBench.NET.Application.AlternateContracts;

/// <summary>
/// Serializes and deserializes contract payloads used by alternate-contract profiles.
/// </summary>
public interface IContractPayloadSerializer
{
    /// <summary>
    /// Deserializes a payload stream into the requested contract type.
    /// </summary>
    Task<object> DeserializeAsync(
        Type targetType,
        Stream body,
        PayloadFormat format,
        bool ignoreXmlNamespaces = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Serializes a contract instance into the requested payload format.
    /// </summary>
    Task<byte[]> SerializeAsync(
        object value,
        Type valueType,
        PayloadFormat format,
        CancellationToken cancellationToken = default);
}
