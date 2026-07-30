using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Application.ContractProfiles;

/// <summary>
/// Serializes and deserializes payloads used by contract profiles.
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
    /// Serializes a contract instance into the requested payload format without owning the destination stream.
    /// </summary>
    Task SerializeAsync(
        object value,
        Type valueType,
        PayloadFormat format,
        Stream destination,
        CancellationToken cancellationToken = default);
}
