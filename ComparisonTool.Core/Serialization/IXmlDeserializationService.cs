// <copyright file="IXmlDeserializationService.cs" company="PlaceholderCompany">



namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Interface for XML deserialization operations.
/// </summary>
public interface IXmlDeserializationService {
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
