namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Interface for XML deserialization operations.
/// </summary>
public interface IXmlDeserializationService
{
    /// <summary>
    /// Gets or sets whether to ignore XML namespaces during deserialization.
    /// When true, XML documents with any namespace (or no namespace) will deserialize correctly
    /// regardless of what namespace the domain model expects.
    /// Default is true to support version-agnostic XML comparison.
    /// </summary>
    bool IgnoreXmlNamespaces { get; set; }

    /// <summary>
    /// Register a domain model for XML deserialization.
    /// </summary>
    void RegisterDomainModel<T>(string modelName)
        where T : class;

    /// <summary>
    /// Get all registered domain model names.
    /// </summary>
    /// <returns></returns>
    IEnumerable<string> GetRegisteredModelNames();

    /// <summary>
    /// Get type for a registered model name.
    /// </summary>
    /// <returns></returns>
    Type GetModelType(string modelName);

    T DeserializeXml<T>(Stream xmlStream)
        where T : class;

    /// <summary>
    /// Attempts to deserialize an XML stream to the specified model type without throwing exceptions
    /// for expected failure cases (SOAP faults, wrong root elements, empty files, malformed XML).
    /// <para>
    /// This method pre-validates the XML root element before attempting full deserialization,
    /// preventing <see cref="System.InvalidOperationException"/> from being thrown by
    /// <see cref="System.Xml.Serialization.XmlSerializer.Deserialize(System.Xml.XmlReader)"/>.
    /// This is critical for folder comparisons where SOAP faults are expected and should not
    /// cause the VS debugger to break on first-chance exceptions.
    /// </para>
    /// </summary>
    /// <param name="xmlStream">The XML stream to deserialize.</param>
    /// <param name="modelType">The target model type (resolved at runtime).</param>
    /// <returns>A <see cref="DeserializationResult"/> containing the object or an error message.</returns>
    DeserializationResult TryDeserializeXml(Stream xmlStream, Type modelType);

    T CloneObject<T>(T source);

    /// <summary>
    /// Clear the internal deserialization cache.
    /// </summary>
    void ClearDeserializationCache();

    /// <summary>
    /// Get cache statistics for diagnostics.
    /// </summary>
    /// <returns></returns>
    (int CacheSize, int SerializerCacheSize) GetCacheStatistics();

    /// <summary>
    /// Force clear all caches - useful for debugging deserialization inconsistencies.
    /// </summary>
    void ClearAllCaches();
}
