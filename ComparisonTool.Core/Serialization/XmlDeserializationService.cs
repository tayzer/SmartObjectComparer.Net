// <copyright file="XmlDeserializationService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using ComparisonTool.Core.Comparison.Configuration;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Service responsible for XML deserialization operations.
/// </summary>
public class XmlDeserializationService : IXmlDeserializationService {
    private readonly ILogger<XmlDeserializationService> logger;
    private readonly Dictionary<string, Type> registeredDomainModels = new();
    private readonly XmlSerializerFactory serializerFactory;

    // CRITICAL FIX: Use thread-local storage to prevent shared state corruption during parallel processing
    private readonly ThreadLocal<ConcurrentDictionary<Type, XmlSerializer>> threadLocalSerializerCache;
    private readonly ConcurrentDictionary<Type, XmlSerializer> serializerCache;

    // Cache for recently deserialized objects
    private readonly ConcurrentDictionary<string, (DateTime LastAccess, object Data)> deserializationCache = new();
    private readonly TimeSpan cacheExpiration = TimeSpan.FromMinutes(10);
    private readonly int maxCacheSize = 100;
    private DateTime lastCacheCleanup = DateTime.Now;

    // Application session identifier to invalidate cache on restart
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];

    private readonly IComparisonConfigurationService? configService;

    public XmlDeserializationService(ILogger<XmlDeserializationService> logger, XmlSerializerFactory serializerFactory, IComparisonConfigurationService? configService = null) {
        this.logger = logger;
        this.serializerFactory = serializerFactory;
        this.configService = configService;

        // CRITICAL FIX: Initialize thread-local storage for serializer cache
        this.threadLocalSerializerCache = new ThreadLocal<ConcurrentDictionary<Type, XmlSerializer>>(() => new ConcurrentDictionary<Type, XmlSerializer>());
        this.serializerCache = new ConcurrentDictionary<Type, XmlSerializer>();
    }

    /// <summary>
    /// Register a domain model for XML deserialization.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <param name="modelName">Name to identify this model type.</param>
    public void RegisterDomainModel<T>(string modelName)
        where T : class {
        this.registeredDomainModels[modelName] = typeof(T);
        this.logger.LogInformation("Registered model {ModelName} as {ModelType}", modelName, typeof(T).Name);

        // Pre-cache the serializer for this type to avoid creation during comparison
        this.GetCachedSerializer<T>();

        // Automatically add XmlIgnore properties to the ignore list if config service is available
        if (this.configService != null) {
            try {
                this.configService.AddXmlIgnorePropertiesToIgnoreList(typeof(T));
            }
            catch (Exception ex) {
                this.logger.LogWarning(ex, "Error adding XmlIgnore properties to ignore list for model {ModelName}", modelName);
            }
        }
    }

    /// <summary>
    /// Get all registered domain model names.
    /// </summary>
    /// <returns></returns>
    public IEnumerable<string> GetRegisteredModelNames() {
        return this.registeredDomainModels.Keys;
    }

    /// <summary>
    /// Get type for a registered model name.
    /// </summary>
    /// <param name="modelName">The name of the model to retrieve.</param>
    /// <returns>The type associated with the given model name.</returns>
    /// <exception cref="ArgumentException">Thrown when no model is registered with the given name.</exception>
    public Type GetModelType(string modelName) {
        if (!this.registeredDomainModels.ContainsKey(modelName)) {
            this.logger.LogError("No model registered with name: {ModelName}", modelName);
            throw new ArgumentException($"No model registered with name: {modelName}");
        }

        return this.registeredDomainModels[modelName];
    }

    /// <summary>
    /// Deserialize XML stream to object with efficient reader pooling and caching.
    /// </summary>
    /// <returns></returns>
    public T DeserializeXml<T>(Stream xmlStream)
        where T : class {
        if (xmlStream == null) {
            this.logger.LogError("XML stream cannot be null");
            throw new ArgumentNullException(nameof(xmlStream));
        }

        try {
            // PERFORMANCE OPTIMIZATION: Skip hashing for cache - rely on ComparisonResultCacheService instead
            // The MD5 hashing was causing double memory usage and blocking the comparison start
            // For deserialization, we now just deserialize directly since the result caching happens at a higher level

            var serializer = this.GetCachedSerializer<T>();
            xmlStream.Position = 0;

            // Create a fresh XmlReader with consistent settings for every deserialization
            using var reader = XmlReader.Create(xmlStream, this.GetOptimizedReaderSettings());

            // Deserialize using the fresh reader
            var result = (T)serializer.Deserialize(reader);

            return result;
        }
        catch (Exception ex) {
            this.logger.LogError(ex, "Error deserializing XML to type {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Clone object efficiently using serialization.
    /// </summary>
    /// <returns></returns>
    public T CloneObject<T>(T source) {
        if (source == null) {
            return default;
        }

        try {
            var serializer = this.GetCachedSerializer<T>();
            using var stream = new MemoryStream();
            serializer.Serialize(stream, source);
            stream.Position = 0;

            // CRITICAL FIX: Use conservative reader settings for cloning to preserve data fidelity
            // The aggressive GetOptimizedReaderSettings() was causing data loss during round-trip
            using var reader = XmlReader.Create(stream, this.GetConservativeCloneReaderSettings());
            var clonedResult = (T)serializer.Deserialize(reader);

            // DIAGNOSTIC: Log cloning operation for debugging serialization issues
            this.logger.LogDebug(
                "Successfully cloned object of type {Type}. Stream size: {StreamSize} bytes",
                typeof(T).Name, stream.Length);

            return clonedResult;
        }
        catch (Exception ex) {
            this.logger.LogError(ex, "Error cloning object of type {Type}. This could indicate XML serialization round-trip issues.", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Clear the internal deserialization cache - useful for testing or when serialization logic changes.
    /// </summary>
    public void ClearDeserializationCache() {
        var count = this.deserializationCache.Count;
        this.deserializationCache.Clear();
        this.logger.LogInformation("Cleared internal deserialization cache: {Count} entries removed", count);
    }

    /// <summary>
    /// Get cache statistics for diagnostics.
    /// </summary>
    /// <returns></returns>
    public (int CacheSize, int SerializerCacheSize) GetCacheStatistics() {
        return (this.deserializationCache.Count, this.serializerCache.Count);
    }

    /// <summary>
    /// Force clear all caches - useful for debugging deserialization inconsistencies.
    /// </summary>
    public void ClearAllCaches() {
        var deserializationCount = this.deserializationCache.Count;
        var serializerCount = this.serializerCache.Count;

        this.deserializationCache.Clear();
        this.serializerCache.Clear();

        this.logger.LogWarning(
            "CLEARED ALL CACHES: {DeserializationCache} deserialization entries, {SerializerCache} serializer entries removed",
            deserializationCount, serializerCount);
    }

    /// <summary>
    /// Get optimized XML reader settings that are forgiving of XML variations
    /// and ensure consistent parsing behavior across all scenarios.
    /// </summary>
    private XmlReaderSettings GetOptimizedReaderSettings() {
        return new XmlReaderSettings {
            // CRITICAL: Ensure consistent whitespace handling
            IgnoreWhitespace = true,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,

            // Security and performance settings
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = 50 * 1024 * 1024, // 50MB limit
            CloseInput = false, // Don't close the underlying stream

            // ENHANCED: More lenient parsing for consistency
            CheckCharacters = false, // More lenient character checking for encoding issues
            ConformanceLevel = ConformanceLevel.Document, // Require well-formed documents (more strict than Fragment)
            ValidationFlags = System.Xml.Schema.XmlSchemaValidationFlags.None, // No validation
            ValidationType = ValidationType.None, // No validation

            // WHITESPACE FIX: Ensure consistent normalization
            // This helps ensure that whitespace-sensitive elements are handled consistently
            Async = false, // Synchronous processing for consistency

            // Character encoding handling
            XmlResolver = null, // Disable external resource resolution for security and consistency
        };
    }

    /// <summary>
    /// Get conservative XML reader settings for cloning to preserve data fidelity
    /// These settings prioritize preserving all data over performance optimizations.
    /// </summary>
    private XmlReaderSettings GetConservativeCloneReaderSettings() {
        return new XmlReaderSettings {
            // CONSERVATIVE: Preserve whitespace to avoid data loss during cloning
            IgnoreWhitespace = false,  // CRITICAL FIX: Don't ignore whitespace during cloning
            IgnoreComments = false,    // Preserve comments
            IgnoreProcessingInstructions = false, // Preserve processing instructions

            // Security settings (keep these strict)
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = 50 * 1024 * 1024,
            CloseInput = false,

            // Conservative parsing settings for fidelity
            CheckCharacters = true,    // Strict character checking for data integrity
            ConformanceLevel = ConformanceLevel.Document,
            ValidationFlags = System.Xml.Schema.XmlSchemaValidationFlags.None,
            ValidationType = ValidationType.None,

            // Synchronous processing for consistency
            Async = false,

            // Disable external resources for security
            XmlResolver = null,
        };
    }

    /// <summary>
    /// Get cached XmlSerializer with enhanced configuration for handling unknown elements.
    /// </summary>
    private XmlSerializer GetCachedSerializer<T>() {
        var type = typeof(T);

        // CRITICAL FIX: Use thread-local storage to prevent shared state corruption
        // Each thread gets its own serializer cache to avoid parallel processing issues
        var threadLocalCache = this.threadLocalSerializerCache.Value;

        return threadLocalCache.GetOrAdd(type, _ => {
            // CRITICAL FIX: Use the public GetSerializer method instead of private CreateComplexOrderResponseSerializer
            var serializer = this.serializerFactory.GetSerializer<T>();

            // CRITICAL FIX: Add event handlers to gracefully handle unknown elements
            serializer.UnknownElement += (sender, e) => {
                // Log unknown elements for debugging but don't throw exceptions
                this.logger.LogDebug(
                    "Unknown XML element encountered: {ElementName} at line {LineNumber}, position {LinePosition}. This element will be ignored during deserialization.",
                    e.Element.Name, e.LineNumber, e.LinePosition);
            };

            serializer.UnknownAttribute += (sender, e) => {
                // Log unknown attributes for debugging but don't throw exceptions
                this.logger.LogDebug(
                    "Unknown XML attribute encountered: {AttributeName} at line {LineNumber}, position {LinePosition}. This attribute will be ignored during deserialization.",
                    e.Attr.Name, e.LineNumber, e.LinePosition);
            };

            return serializer;
        });
    }

    /// <summary>
    /// Clean up expired cache entries.
    /// </summary>
    private void CleanupCacheIfNeeded() {
        // Only clean up occasionally
        if ((DateTime.Now - this.lastCacheCleanup).TotalMinutes < 5) {
            return;
        }

        this.lastCacheCleanup = DateTime.Now;

        // Remove expired items
        foreach (var entry in this.deserializationCache.ToArray()) {
            if ((DateTime.Now - entry.Value.LastAccess) > this.cacheExpiration) {
                this.deserializationCache.TryRemove(entry.Key, out _);
            }
        }

        // If still too many entries, remove oldest ones
        if (this.deserializationCache.Count > this.maxCacheSize) {
            var oldestEntries = this.deserializationCache
                .OrderBy(x => x.Value.LastAccess)
                .Take(this.deserializationCache.Count - (this.maxCacheSize / 2));

            foreach (var entry in oldestEntries) {
                this.deserializationCache.TryRemove(entry.Key, out _);
            }
        }
    }
}
