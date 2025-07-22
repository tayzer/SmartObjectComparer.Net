namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Interface for XML deserialization operations
/// </summary>
public interface IXmlDeserializationService
{
    /// <summary>
    /// Register a domain model for XML deserialization
    /// </summary>
    void RegisterDomainModel<T>(string modelName) where T : class;

    /// <summary>
    /// Get all registered domain model names
    /// </summary>
    IEnumerable<string> GetRegisteredModelNames();

    /// <summary>
    /// Get type for a registered model name
    /// </summary>
    Type GetModelType(string modelName);

    T DeserializeXml<T>(Stream xmlStream) where T : class;

    T CloneObject<T>(T source);
    
    /// <summary>
    /// Clear the internal deserialization cache
    /// </summary>
    void ClearDeserializationCache();
    
    /// <summary>
    /// Get cache statistics for diagnostics
    /// </summary>
    (int CacheSize, int SerializerCacheSize) GetCacheStatistics();
    
    /// <summary>
    /// Force clear all caches - useful for debugging deserialization inconsistencies
    /// </summary>
    void ClearAllCaches();
}