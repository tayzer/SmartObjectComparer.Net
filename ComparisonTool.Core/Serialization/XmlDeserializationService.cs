using System.Collections.Concurrent;
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

    // XML Reader/Writer pools for efficient reuse
    private readonly ConcurrentDictionary<Type, XmlSerializer> _serializerCache = new();
    private readonly ConcurrentQueue<XmlReader> _readerPool = new();
    private readonly int _maxPoolSize = 20;
    private readonly bool _enableFastReading = true;

    // Cache for recently deserialized objects
    private readonly ConcurrentDictionary<string, (DateTime LastAccess, object Data)> _deserializationCache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(10);
    private readonly int _maxCacheSize = 100;
    private DateTime _lastCacheCleanup = DateTime.Now;

    public XmlDeserializationService(
        ILogger<XmlDeserializationService> logger,
        XmlSerializerFactory serializerFactory)
    {
        this.logger = logger;
        this.serializerFactory = serializerFactory;

        // Initialize reader pool with empty readers
        for (int i = 0; i < 5; i++)
        {
            _readerPool.Enqueue(XmlReader.Create(new StringReader(string.Empty)));
        }
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
                    cacheKey = Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(bytes));

                    // Check cache first
                    if (_deserializationCache.TryGetValue(cacheKey, out var cached))
                    {
                        _deserializationCache[cacheKey] = (DateTime.Now, cached.Data);
                        return (T)cached.Data;
                    }

                    // Reset position for deserialization
                    xmlStream.Position = 0;
                }
            }

            // Use fast path for small XML files
            if (_enableFastReading && xmlStream.Length < 5 * 1024 * 1024)
            {
                var serializer = GetCachedSerializer<T>();
                xmlStream.Position = 0;

                // Get a reader from the pool or create a new one
                XmlReader reader = null;
                bool fromPool = _readerPool.TryDequeue(out reader);

                if (!fromPool)
                {
                    reader = XmlReader.Create(xmlStream, GetOptimizedReaderSettings());
                }
                else
                {
                    // Can't reuse the reader directly with a new stream, so create a new one
                    // but we'll still return the old one to the pool when done
                    reader.Dispose(); // Close the old reader
                    reader = XmlReader.Create(xmlStream, GetOptimizedReaderSettings());
                }

                try
                {
                    // Deserialize using the reader
                    var result = (T)serializer.Deserialize(reader);

                    // Cache result if needed
                    if (cacheKey != null && _deserializationCache.Count < _maxCacheSize)
                    {
                        _deserializationCache[cacheKey] = (DateTime.Now, result);
                        CleanupCacheIfNeeded();
                    }

                    return result;
                }
                finally
                {
                    // Return reader to pool if there's space
                    if (_readerPool.Count < _maxPoolSize)
                    {
                        try
                        {
                            reader.Close();
                            _readerPool.Enqueue(reader);
                        }
                        catch
                        {
                            // If we can't reuse the reader, just let it be garbage collected
                        }
                    }
                    else
                    {
                        reader.Dispose();
                    }
                }
            }
            else
            {
                // Fallback for large files - avoid cached readers to prevent memory leaks
                var serializer = GetCachedSerializer<T>();
                xmlStream.Position = 0;

                return (T)serializer.Deserialize(xmlStream);
            }
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
            return (T)serializer.Deserialize(stream);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cloning object of type {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Get optimized XML reader settings
    /// </summary>
    private XmlReaderSettings GetOptimizedReaderSettings()
    {
        return new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = 50 * 1024 * 1024, // 50MB limit
            CloseInput = false // Don't close the underlying stream
        };
    }

    /// <summary>
    /// Get or create a cached XmlSerializer
    /// </summary>
    private XmlSerializer GetCachedSerializer<T>()
    {
        var type = typeof(T);
        return _serializerCache.GetOrAdd(type, t => serializerFactory.GetSerializer<T>());
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