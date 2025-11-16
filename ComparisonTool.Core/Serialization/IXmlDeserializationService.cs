// <copyright file="IXmlDeserializationService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Interface for XML deserialization operations.
/// </summary>
public interface IXmlDeserializationService
{
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
