namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Interface for format-agnostic deserialization operations
/// Supports both XML and JSON file formats.
/// </summary>
public interface IDeserializationService
{
    /// <summary>
    /// Gets get supported file formats for this service.
    /// </summary>
    IEnumerable<SerializationFormat> SupportedFormats
    {
        get;
    }

    /// <summary>
    /// Register a domain model for deserialization.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <param name="modelName">Name to identify this model type.</param>
    void RegisterDomainModel<T>(string modelName)
        where T : class;

    /// <summary>
    /// Get all registered domain model names.
    /// </summary>
    /// <returns>Collection of registered model names.</returns>
    IEnumerable<string> GetRegisteredModelNames();

    /// <summary>
    /// Get type for a registered model name.
    /// </summary>
    /// <param name="modelName">The name of the model to retrieve.</param>
    /// <returns>The type associated with the given model name.</returns>
    Type GetModelType(string modelName);

    /// <summary>
    /// Deserialize stream to object with format auto-detection or explicit format.
    /// </summary>
    /// <typeparam name="T">Type to deserialize to.</typeparam>
    /// <param name="stream">Stream containing the serialized data.</param>
    /// <param name="format">Optional explicit format (XML/JSON). If null, will auto-detect from stream content.</param>
    /// <returns>Deserialized object.</returns>
    T Deserialize<T>(Stream stream, SerializationFormat? format = null)
        where T : class;

    /// <summary>
    /// Clone object efficiently using serialization.
    /// </summary>
    /// <typeparam name="T">Type to clone.</typeparam>
    /// <param name="source">Source object to clone.</param>
    /// <returns>Cloned object.</returns>
    T CloneObject<T>(T source);

    /// <summary>
    /// Clear the internal deserialization cache.
    /// </summary>
    void ClearDeserializationCache();

    /// <summary>
    /// Get cache statistics for diagnostics.
    /// </summary>
    /// <returns>Tuple with cache size and serializer cache size.</returns>
    (int CacheSize, int SerializerCacheSize) GetCacheStatistics();

    /// <summary>
    /// Force clear all caches - useful for debugging deserialization inconsistencies.
    /// </summary>
    void ClearAllCaches();
}

/// <summary>
/// Supported serialization formats.
/// </summary>
public enum SerializationFormat
{
    /// <summary>
    /// XML format using System.Xml.Serialization
    /// </summary>
    Xml,

    /// <summary>
    /// JSON format using System.Text.Json
    /// </summary>
    Json,
}
