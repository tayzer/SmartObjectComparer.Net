using System.Collections.Concurrent;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Service responsible for XML deserialization operations
/// </summary>
public class XmlDeserializationService : IXmlDeserializationService
{
    private readonly ILogger<XmlDeserializationService> logger;
    private readonly Dictionary<string, Type> registeredDomainModels = new();
    private readonly XmlSerializerFactory serializerFactory;
    
    // CRITICAL FIX: Use thread-local storage to prevent shared state corruption during parallel processing
    private readonly ThreadLocal<ConcurrentDictionary<Type, XmlSerializer>> _threadLocalSerializerCache;
    private readonly ConcurrentDictionary<Type, XmlSerializer> _serializerCache;

    // Cache for recently deserialized objects
    private readonly ConcurrentDictionary<string, (DateTime LastAccess, object Data)> _deserializationCache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(10);
    private readonly int _maxCacheSize = 100;
    private DateTime _lastCacheCleanup = DateTime.Now;
    
    // Application session identifier to invalidate cache on restart
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];

    public XmlDeserializationService(ILogger<XmlDeserializationService> logger, XmlSerializerFactory serializerFactory)
    {
        this.logger = logger;
        this.serializerFactory = serializerFactory;
        
        // CRITICAL FIX: Initialize thread-local storage for serializer cache
        _threadLocalSerializerCache = new ThreadLocal<ConcurrentDictionary<Type, XmlSerializer>>(() => new ConcurrentDictionary<Type, XmlSerializer>());
        _serializerCache = new ConcurrentDictionary<Type, XmlSerializer>();
    }

    /// <summary>
    /// Register a domain model for XML deserialization
    /// </summary>
    /// <typeparam name="T">The type to register</typeparam>
    /// <param name="modelName">Name to identify this model type</param>
    public void RegisterDomainModel<T>(string modelName) where T : class
    {
        registeredDomainModels[modelName] = typeof(T);
        logger.LogInformation("Registered model {ModelName} as {ModelType}", modelName, typeof(T).Name);

        // Pre-cache the serializer for this type to avoid creation during comparison
        GetCachedSerializer<T>();
    }

    /// <summary>
    /// Get all registered domain model names
    /// </summary>
    public IEnumerable<string> GetRegisteredModelNames()
    {
        return registeredDomainModels.Keys;
    }

    /// <summary>
    /// Get type for a registered model name
    /// </summary>
    /// <param name="modelName">The name of the model to retrieve</param>
    /// <returns>The type associated with the given model name</returns>
    /// <exception cref="ArgumentException">Thrown when no model is registered with the given name</exception>
    public Type GetModelType(string modelName)
    {
        if (!registeredDomainModels.ContainsKey(modelName))
        {
            logger.LogError("No model registered with name: {ModelName}", modelName);
            throw new ArgumentException($"No model registered with name: {modelName}");
        }

        return registeredDomainModels[modelName];
    }

    /// <summary>
    /// Deserialize XML stream to object with efficient reader pooling and caching
    /// </summary>
    public T DeserializeXml<T>(Stream xmlStream) where T : class
    {
        if (xmlStream == null)
        {
            logger.LogError("XML stream cannot be null");
            throw new ArgumentNullException(nameof(xmlStream));
        }

        try
        {
            // Try to determine if we've seen this exact XML before
            string cacheKey = null;

            if (xmlStream.CanSeek && xmlStream.Length < 1024 * 1024) // Only for reasonably sized files
            {
                // Generate a simple hash as cache key
                using (var ms = new MemoryStream())
                {
                    xmlStream.Position = 0;
                    xmlStream.CopyTo(ms);
                    byte[] bytes = ms.ToArray();
                    var contentHash = Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(bytes));
                    cacheKey = $"{contentHash}_{SessionId}"; // Include session ID for cache invalidation

                    // Check cache first
                    if (_deserializationCache.TryGetValue(cacheKey, out var cached))
                    {
                        _deserializationCache[cacheKey] = (DateTime.Now, cached.Data);
                        logger.LogDebug("MD5-based cache HIT for XML content hash: {CacheKey} (SessionId: {SessionId})", 
                            contentHash[..8], SessionId);
                        return (T)cached.Data;
                    }
                    else
                    {
                        logger.LogDebug("MD5-based cache MISS for XML content hash: {CacheKey} (SessionId: {SessionId})", 
                            contentHash[..8], SessionId);
                    }

                    // Reset position for deserialization
                    xmlStream.Position = 0;
                }
            }

            // CRITICAL FIX: Always create fresh XmlReader to avoid state corruption
            // The pooling was causing inconsistent behavior between single and batch processing
            var serializer = GetCachedSerializer<T>();
            xmlStream.Position = 0;

            // Create a fresh XmlReader with consistent settings for every deserialization
            using var reader = XmlReader.Create(xmlStream, GetOptimizedReaderSettings());
            
            // Deserialize using the fresh reader
            var result = (T)serializer.Deserialize(reader);

            // Cache result if needed
            if (cacheKey != null && _deserializationCache.Count < _maxCacheSize)
            {
                _deserializationCache[cacheKey] = (DateTime.Now, result);
                CleanupCacheIfNeeded();
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deserializing XML to type {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Clone object efficiently using serialization
    /// </summary>
    public T CloneObject<T>(T source)
    {
        if (source == null)
            return default;

        try
        {
            var serializer = GetCachedSerializer<T>();
            using var stream = new MemoryStream();
            serializer.Serialize(stream, source);
            stream.Position = 0;
            
            // CRITICAL FIX: Use conservative reader settings for cloning to preserve data fidelity
            // The aggressive GetOptimizedReaderSettings() was causing data loss during round-trip
            using var reader = XmlReader.Create(stream, GetConservativeCloneReaderSettings());
            var clonedResult = (T)serializer.Deserialize(reader);
            
            // DIAGNOSTIC: Log cloning operation for debugging serialization issues
            logger.LogDebug("Successfully cloned object of type {Type}. Stream size: {StreamSize} bytes", 
                typeof(T).Name, stream.Length);
            
            return clonedResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cloning object of type {Type}. This could indicate XML serialization round-trip issues.", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Clear the internal deserialization cache - useful for testing or when serialization logic changes
    /// </summary>
    public void ClearDeserializationCache()
    {
        var count = _deserializationCache.Count;
        _deserializationCache.Clear();
        logger.LogInformation("Cleared internal deserialization cache: {Count} entries removed", count);
    }

    /// <summary>
    /// Get cache statistics for diagnostics
    /// </summary>
    public (int CacheSize, int SerializerCacheSize) GetCacheStatistics()
    {
        return (_deserializationCache.Count, _serializerCache.Count);
    }

    /// <summary>
    /// Force clear all caches - useful for debugging deserialization inconsistencies
    /// </summary>
    public void ClearAllCaches()
    {
        var deserializationCount = _deserializationCache.Count;
        var serializerCount = _serializerCache.Count;
        
        _deserializationCache.Clear();
        _serializerCache.Clear();
        
        logger.LogWarning("CLEARED ALL CACHES: {DeserializationCache} deserialization entries, {SerializerCache} serializer entries removed", 
            deserializationCount, serializerCount);
    }

    /// <summary>
    /// Get optimized XML reader settings that are forgiving of XML variations
    /// and ensure consistent parsing behavior across all scenarios
    /// </summary>
    private XmlReaderSettings GetOptimizedReaderSettings()
    {
        return new XmlReaderSettings
        {
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
            XmlResolver = null // Disable external resource resolution for security and consistency
        };
    }

    /// <summary>
    /// Get conservative XML reader settings for cloning to preserve data fidelity
    /// These settings prioritize preserving all data over performance optimizations
    /// </summary>
    private XmlReaderSettings GetConservativeCloneReaderSettings()
    {
        return new XmlReaderSettings
        {
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
            XmlResolver = null
        };
    }

    /// <summary>
    /// Get cached XmlSerializer with enhanced configuration for handling unknown elements
    /// </summary>
    private XmlSerializer GetCachedSerializer<T>()
    {
        var type = typeof(T);
        
        // CRITICAL FIX: Use thread-local storage to prevent shared state corruption
        // Each thread gets its own serializer cache to avoid parallel processing issues
        var threadLocalCache = _threadLocalSerializerCache.Value;
        
        return threadLocalCache.GetOrAdd(type, _ => 
        {
            // CRITICAL FIX: Use the public GetSerializer method instead of private CreateComplexOrderResponseSerializer
            var serializer = serializerFactory.GetSerializer<T>();
            
            // CRITICAL FIX: Add event handlers to gracefully handle unknown elements
            serializer.UnknownElement += (sender, e) =>
            {
                // Log unknown elements for debugging but don't throw exceptions
                logger.LogDebug("Unknown XML element encountered: {ElementName} at line {LineNumber}, position {LinePosition}. This element will be ignored during deserialization.", 
                    e.Element.Name, e.LineNumber, e.LinePosition);
            };
            
            serializer.UnknownAttribute += (sender, e) =>
            {
                // Log unknown attributes for debugging but don't throw exceptions
                logger.LogDebug("Unknown XML attribute encountered: {AttributeName} at line {LineNumber}, position {LinePosition}. This attribute will be ignored during deserialization.", 
                    e.Attr.Name, e.LineNumber, e.LinePosition);
            };
            
            return serializer;
        });
    }



    /// <summary>
    /// Clean up expired cache entries
    /// </summary>
    private void CleanupCacheIfNeeded()
    {
        // Only clean up occasionally
        if ((DateTime.Now - _lastCacheCleanup).TotalMinutes < 5)
            return;

        _lastCacheCleanup = DateTime.Now;

        // Remove expired items
        foreach (var entry in _deserializationCache.ToArray())
        {
            if ((DateTime.Now - entry.Value.LastAccess) > _cacheExpiration)
            {
                _deserializationCache.TryRemove(entry.Key, out _);
            }
        }

        // If still too many entries, remove oldest ones
        if (_deserializationCache.Count > _maxCacheSize)
        {
            var oldestEntries = _deserializationCache
                .OrderBy(x => x.Value.LastAccess)
                .Take(_deserializationCache.Count - _maxCacheSize / 2);

            foreach (var entry in oldestEntries)
            {
                _deserializationCache.TryRemove(entry.Key, out _);
            }
        }
    }
}