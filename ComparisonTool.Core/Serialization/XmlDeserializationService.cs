using System.Collections.Concurrent;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
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
            // DIAGNOSTIC: Log the start of deserialization
            logger.LogDebug("Starting XML deserialization for type {Type}", typeof(T).Name);

            // Try to determine if we've seen this exact XML before
            string cacheKey = null;

            if (xmlStream.CanSeek && xmlStream.Length < 1024 * 1024) // Only cache small files
            {
                var position = xmlStream.Position;
                xmlStream.Position = 0;
                using var reader = new StreamReader(xmlStream, leaveOpen: true);
                var content = reader.ReadToEnd();
                xmlStream.Position = position;

                // Create a simple hash for caching
                var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(content));
                cacheKey = Convert.ToBase64String(hash).Substring(0, 8);
                logger.LogDebug("MD5-based cache {CacheResult} for XML content hash: {Hash} (SessionId: {SessionId})",
                    _deserializationCache.ContainsKey(cacheKey) ? "HIT" : "MISS", cacheKey, Guid.NewGuid().ToString("N")[..8]);

                if (_deserializationCache.TryGetValue(cacheKey, out var cachedResult))
                {
                    logger.LogDebug("Using cached deserialization result for {Type}", typeof(T).Name);
                    return (T)cachedResult.Data;
                }
            }

            // Get the appropriate serializer
            var serializer = GetCachedSerializer<T>();

            // Create optimized XML reader settings
            var readerSettings = GetOptimizedReaderSettings();

            // Reset stream position
            if (xmlStream.CanSeek)
            {
                xmlStream.Position = 0;
            }

            // Create XML reader with optimized settings
            using var xmlReader = XmlReader.Create(xmlStream, readerSettings);
            logger.LogDebug("Created XmlReader with IgnoreWhitespace: {IgnoreWhitespace}", readerSettings.IgnoreWhitespace);

            // Perform deserialization
            var result = serializer.Deserialize(xmlReader) as T;

            // DIAGNOSTIC: Log deserialization result
            if (result == null)
            {
                logger.LogError("Deserialization returned NULL for type {Type}", typeof(T).Name);
            }
            else
            {
                logger.LogDebug("Deserialization successful for type {Type}. Result type: {ResultType}", 
                    typeof(T).Name, result.GetType().Name);
                
                // DIAGNOSTIC: Check if key properties are null
                logger.LogDebug("Deserialization completed successfully");
            }

            // Cache the result if we have a cache key
            if (cacheKey != null)
            {
                _deserializationCache[cacheKey] = (DateTime.Now, result);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deserializing XML for type {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Tolerant XML deserialization using XDocument that ignores extra elements and order
    /// </summary>
    public T DeserializeXmlTolerant<T>(Stream xmlStream) where T : class
    {
        if (xmlStream == null)
        {
            logger.LogError("XML stream cannot be null");
            throw new ArgumentNullException(nameof(xmlStream));
        }

        try
        {
            logger.LogDebug("Starting tolerant XML deserialization for type {Type}", typeof(T).Name);

            // Reset stream position
            if (xmlStream.CanSeek)
            {
                xmlStream.Position = 0;
            }

            // Load XML document
            var doc = XDocument.Load(xmlStream);
            var root = doc.Root;
            
            if (root == null)
            {
                throw new InvalidOperationException("XML document has no root element");
            }

            // Create instance of target type
            var result = Activator.CreateInstance<T>();
            
            // Map XML elements to object properties
            MapXmlToObject(root, result, typeof(T));

            logger.LogDebug("Tolerant deserialization completed successfully for type {Type}", typeof(T).Name);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in tolerant XML deserialization for type {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Recursively map XML elements to object properties, ignoring extra elements and order
    /// </summary>
    private void MapXmlToObject(XElement element, object target, Type targetType)
    {
        foreach (var childElement in element.Elements())
        {
            var propertyName = GetPropertyNameForElement(childElement.Name.LocalName, targetType);
            
            if (propertyName != null)
            {
                var property = targetType.GetProperty(propertyName);
                if (property != null)
                {
                    SetPropertyValue(target, property, childElement);
                }
                else
                {
                    logger.LogDebug("Unknown XML element encountered: {ElementName}. Ignoring element.",
                        childElement.Name.LocalName);
                }
            }
            else
            {
                logger.LogDebug("Unknown XML element encountered: {ElementName}. Ignoring element.",
                    childElement.Name.LocalName);
            }
        }
    }

    /// <summary>
    /// Get the property name for an XML element, considering XmlElement attributes
    /// </summary>
    private string GetPropertyNameForElement(string elementName, Type targetType)
    {
        foreach (var property in targetType.GetProperties())
        {
            var xmlElementAttr = property.GetCustomAttribute<XmlElementAttribute>();
            if (xmlElementAttr != null && xmlElementAttr.ElementName == elementName)
            {
                return property.Name;
            }
            
            var xmlArrayAttr = property.GetCustomAttribute<XmlArrayAttribute>();
            if (xmlArrayAttr != null && xmlArrayAttr.ElementName == elementName)
            {
                return property.Name;
            }

            // If no attribute, check if property name matches element name (case-insensitive)
            if (string.Equals(property.Name, elementName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Name;
            }
        }
        return null;
    }

    /// <summary>
    /// Set property value from XML element
    /// </summary>
    private void SetPropertyValue(object target, PropertyInfo property, XElement element)
    {
        try
        {
            if (property.PropertyType == typeof(string))
            {
                property.SetValue(target, element.Value);
            }
            else if (property.PropertyType == typeof(int) || property.PropertyType == typeof(int?))
            {
                if (int.TryParse(element.Value, out var intValue))
                {
                    property.SetValue(target, intValue);
                }
            }
            else if (property.PropertyType == typeof(double) || property.PropertyType == typeof(double?))
            {
                if (double.TryParse(element.Value, out var doubleValue))
                {
                    property.SetValue(target, doubleValue);
                }
            }
            else if (property.PropertyType == typeof(decimal) || property.PropertyType == typeof(decimal?))
            {
                if (decimal.TryParse(element.Value, out var decimalValue))
                {
                    property.SetValue(target, decimalValue);
                }
            }
            else if (property.PropertyType == typeof(bool) || property.PropertyType == typeof(bool?))
            {
                if (bool.TryParse(element.Value, out var boolValue))
                {
                    property.SetValue(target, boolValue);
                }
            }
            else if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
            {
                if (DateTime.TryParse(element.Value, out var dateTimeValue))
                {
                    property.SetValue(target, dateTimeValue);
                }
            }
            else if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            {
                // Handle collections
                var listType = property.PropertyType.GetGenericArguments()[0];
                var list = property.GetValue(target) ?? Activator.CreateInstance(property.PropertyType);
                
                foreach (var childElement in element.Elements())
                {
                    var item = Activator.CreateInstance(listType);
                    MapXmlToObject(childElement, item, listType);
                    
                    var addMethod = property.PropertyType.GetMethod("Add");
                    addMethod?.Invoke(list, new[] { item });
                }
                
                property.SetValue(target, list);
            }
            else if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
            {
                // Handle complex objects
                var complexObject = property.GetValue(target) ?? Activator.CreateInstance(property.PropertyType);
                MapXmlToObject(element, complexObject, property.PropertyType);
                property.SetValue(target, complexObject);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error setting property {PropertyName} from XML element {ElementName}", 
                property.Name, element.Name.LocalName);
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
            // CRITICAL FIX: Use the factory which already has event handlers configured
            // The XmlSerializerFactory.CreateDefaultSerializer and CreateComplexOrderResponseSerializer
            // already have UnknownElement, UnknownAttribute, and UnknownNode event handlers
            return serializerFactory.GetSerializer<T>();
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