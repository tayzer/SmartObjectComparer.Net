// <copyright file="XmlDeserializationService.cs" company="PlaceholderCompany">



using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Serialization;
using ComparisonTool.Core.Comparison.Configuration;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Service responsible for XML deserialization operations.
/// </summary>
public class XmlDeserializationService : IXmlDeserializationService
{
    // Application session identifier to invalidate cache on restart
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];

    private readonly ILogger<XmlDeserializationService> logger;
    private readonly Dictionary<string, Type> registeredDomainModels = new Dictionary<string, Type>(StringComparer.Ordinal);
    private readonly XmlSerializerFactory serializerFactory;
    private readonly IComparisonConfigurationService? configService;

    // CRITICAL FIX: Use thread-local storage to prevent shared state corruption during parallel processing
    // Key is (Type, IgnoreXmlNamespaces) to ensure correct serializer is used for each mode
    private readonly ThreadLocal<ConcurrentDictionary<(Type, bool), XmlSerializer>> threadLocalSerializerCache;
    private readonly ConcurrentDictionary<(Type, bool), XmlSerializer> serializerCache;

    // Cache for recently deserialized objects
    private readonly ConcurrentDictionary<string, (DateTime LastAccess, object Data)> deserializationCache = new ConcurrentDictionary<string, (DateTime LastAccess, object Data)>(StringComparer.Ordinal);
    private readonly TimeSpan cacheExpiration = TimeSpan.FromMinutes(10);
    private readonly int maxCacheSize = 100;

    private DateTime lastCacheCleanup = DateTime.Now;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlDeserializationService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="serializerFactory">The XML serializer factory.</param>
    /// <param name="configService">Optional comparison configuration service.</param>
    public XmlDeserializationService(ILogger<XmlDeserializationService> logger, XmlSerializerFactory serializerFactory, IComparisonConfigurationService? configService = null)
    {
        this.logger = logger;
        this.serializerFactory = serializerFactory;
        this.configService = configService;

        // Uses tuple key (Type, IgnoreXmlNamespaces) to cache different serializers for each mode
        threadLocalSerializerCache = new ThreadLocal<ConcurrentDictionary<(Type, bool), XmlSerializer>>(() => new ConcurrentDictionary<(Type, bool), XmlSerializer>());
        serializerCache = new ConcurrentDictionary<(Type, bool), XmlSerializer>();
    }

    /// <summary>
    /// Gets or sets a value indicating whether XML namespaces are ignored during deserialization.
    /// <para>
    /// <b>Lenient mode (true, default):</b> Uses namespace-agnostic serializer overrides.
    /// XML documents with any namespace (or no namespace) will deserialize correctly
    /// regardless of what namespace the domain model expects. The xsi:nil attribute
    /// is preserved, allowing nullable types to deserialize properly.
    /// </para>
    /// <para>
    /// <b>Strict mode (false):</b> Requires XML namespaces to exactly match the
    /// registered domain model's XmlRoot/XmlElement namespace attributes.
    /// Use this for production validation where namespace conformance is required.
    /// </para>
    /// </summary>
    public bool IgnoreXmlNamespaces { get; set; } = true;

    /// <summary>
    /// Register a domain model for XML deserialization.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <param name="modelName">Name to identify this model type.</param>
    public void RegisterDomainModel<T>(string modelName)
        where T : class
    {
        registeredDomainModels[modelName] = typeof(T);
        logger.LogInformation("Registered model {ModelName} as {ModelType}", modelName, typeof(T).Name);

        // Pre-cache the serializer for this type to avoid creation during comparison
        GetCachedSerializer<T>();

        // Automatically add XmlIgnore properties to the ignore list if config service is available
        if (configService != null)
        {
            try
            {
                configService.AddXmlIgnorePropertiesToIgnoreList(typeof(T));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error adding XmlIgnore properties to ignore list for model {ModelName}", modelName);
            }
        }
    }

    /// <summary>
    /// Get all registered domain model names.
    /// </summary>
    /// <returns>Collection of registered model names.</returns>
    public IEnumerable<string> GetRegisteredModelNames() => registeredDomainModels.Keys;

    /// <summary>
    /// Get type for a registered model name.
    /// </summary>
    /// <param name="modelName">The name of the model to retrieve.</param>
    /// <returns>The type associated with the given model name.</returns>
    /// <exception cref="ArgumentException">Thrown when no model is registered with the given name.</exception>
    public Type GetModelType(string modelName)
    {
        if (!registeredDomainModels.ContainsKey(modelName))
        {
            logger.LogError("No model registered with name: {ModelName}", modelName);
            throw new ArgumentException($"No model registered with name: {modelName}", nameof(modelName));
        }

        return registeredDomainModels[modelName];
    }

    /// <summary>
    /// Deserialize XML stream to object with efficient reader pooling and caching.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="xmlStream">The XML stream to deserialize.</param>
    /// <returns>The deserialized object.</returns>
    public T DeserializeXml<T>(Stream xmlStream)
        where T : class
    {
        if (xmlStream == null)
        {
            logger.LogError("XML stream cannot be null");
            throw new ArgumentNullException(nameof(xmlStream));
        }

        try
        {
            // PERFORMANCE OPTIMIZATION: Skip hashing for cache - rely on ComparisonResultCacheService instead
            // The MD5 hashing was causing double memory usage and blocking the comparison start
            // For deserialization, we now just deserialize directly since the result caching happens at a higher level
            var serializer = GetCachedSerializer<T>();
            xmlStream.Position = 0;

            // Create base XML reader with optimized settings
            using var baseReader = XmlReader.Create(xmlStream, GetOptimizedReaderSettings());

            // When ignoring XML namespaces, wrap with NamespaceAgnosticXmlReader
            // This reader strips namespaces from elements BUT preserves xsi:nil attribute
            // which is required for nullable types (DateTime?, int?, etc.)
            XmlReader reader = baseReader;
            if (IgnoreXmlNamespaces)
            {
                reader = new NamespaceAgnosticXmlReader(baseReader);
                logger.LogDebug("Using namespace-agnostic reader for type {Type} (xsi:nil preserved)", typeof(T).Name);
            }

            // Deserialize using the serializer with optional namespace-stripping reader
            var result = serializer.Deserialize(reader)
                ?? throw new InvalidOperationException($"Deserialization of type {typeof(T).Name} returned null.");

            return (T)result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deserializing XML to type {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Clone object efficiently using serialization.
    /// </summary>
    /// <typeparam name="T">The type to clone.</typeparam>
    /// <param name="source">The source object to clone.</param>
    /// <returns>A cloned copy of the source object.</returns>
    public T CloneObject<T>(T source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        try
        {
            var serializer = GetCachedSerializer<T>();
            using var stream = new MemoryStream();
            serializer.Serialize(stream, source);
            stream.Position = 0;

            // CRITICAL FIX: Use conservative reader settings for cloning to preserve data fidelity
            // The aggressive GetOptimizedReaderSettings() was causing data loss during round-trip
            using var reader = XmlReader.Create(stream, GetConservativeCloneReaderSettings());
            var clonedResult = serializer.Deserialize(reader)
                ?? throw new InvalidOperationException($"Cloning of type {typeof(T).Name} returned null.");

            // DIAGNOSTIC: Log cloning operation for debugging serialization issues
            logger.LogDebug(
                "Successfully cloned object of type {Type}. Stream size: {StreamSize} bytes",
                typeof(T).Name,
                stream.Length);

            return (T)clonedResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cloning object of type {Type}. This could indicate XML serialization round-trip issues.", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Clear the internal deserialization cache - useful for testing or when serialization logic changes.
    /// </summary>
    public void ClearDeserializationCache()
    {
        var count = deserializationCache.Count;
        deserializationCache.Clear();
        logger.LogInformation("Cleared internal deserialization cache: {Count} entries removed", count);
    }

    /// <summary>
    /// Get cache statistics for diagnostics.
    /// </summary>
    /// <returns>A tuple containing the cache size and serializer cache size.</returns>
    public (int CacheSize, int SerializerCacheSize) GetCacheStatistics() => (deserializationCache.Count, serializerCache.Count);

    /// <summary>
    /// Force clear all caches - useful for debugging deserialization inconsistencies.
    /// </summary>
    public void ClearAllCaches()
    {
        var deserializationCount = deserializationCache.Count;
        var serializerCount = serializerCache.Count;

        deserializationCache.Clear();
        serializerCache.Clear();

        logger.LogWarning(
            "CLEARED ALL CACHES: {DeserializationCache} deserialization entries, {SerializerCache} serializer entries removed",
            deserializationCount,
            serializerCount);
    }

    /// <summary>
    /// Get optimized XML reader settings that are forgiving of XML variations
    /// and ensure consistent parsing behavior across all scenarios.
    /// </summary>
    private XmlReaderSettings GetOptimizedReaderSettings() => new XmlReaderSettings
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
        XmlResolver = null, // Disable external resource resolution for security and consistency
    };

    /// <summary>
    /// Get conservative XML reader settings for cloning to preserve data fidelity
    /// These settings prioritize preserving all data over performance optimizations.
    /// </summary>
    private XmlReaderSettings GetConservativeCloneReaderSettings() => new XmlReaderSettings
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
        XmlResolver = null,
    };

    /// <summary>
    /// Get cached XmlSerializer with enhanced configuration for handling unknown elements.
    /// The serializer is created based on the current IgnoreXmlNamespaces setting:
    /// - Lenient mode (true): uses namespace-agnostic serializer that accepts any namespace.
    /// - Strict mode (false): uses strict serializer that requires exact namespace matching.
    /// </summary>
    private XmlSerializer GetCachedSerializer<T>()
    {
        var type = typeof(T);

        // CRITICAL FIX: Use thread-local storage to prevent shared state corruption
        // Each thread gets its own serializer cache to avoid parallel processing issues
        var threadLocalCache = threadLocalSerializerCache.Value
            ?? throw new InvalidOperationException("Thread-local serializer cache is not initialized.");

        // Use tuple cache key: (Type, IgnoreXmlNamespaces)
        // This means changing IgnoreXmlNamespaces at runtime will use a different cached serializer
        var cacheKey = (type, IgnoreXmlNamespaces);

        return threadLocalCache.GetOrAdd(cacheKey, _ =>
        {
            // Use the appropriate serializer based on the namespace mode
            var serializer = serializerFactory.GetSerializer<T>(IgnoreXmlNamespaces);

            // CRITICAL FIX: Add event handlers to gracefully handle unknown elements
            serializer.UnknownElement += (sender, e) =>
                logger.LogDebug(
                    "Unknown XML element encountered: {ElementName} at line {LineNumber}, position {LinePosition}. This element will be ignored during deserialization.",
                    e.Element.Name,
                    e.LineNumber,
                    e.LinePosition);

            serializer.UnknownAttribute += (sender, e) =>
                logger.LogDebug(
                    "Unknown XML attribute encountered: {AttributeName} at line {LineNumber}, position {LinePosition}. This attribute will be ignored during deserialization.",
                    e.Attr.Name,
                    e.LineNumber,
                    e.LinePosition);

            return serializer;
        });
    }

    /// <summary>
    /// Clean up expired cache entries.
    /// </summary>
    private void CleanupCacheIfNeeded()
    {
        // Only clean up occasionally
        if ((DateTime.Now - lastCacheCleanup).TotalMinutes < 5)
        {
            return;
        }

        lastCacheCleanup = DateTime.Now;

        // Remove expired items
        foreach (var entry in deserializationCache.ToArray())
        {
            if ((DateTime.Now - entry.Value.LastAccess) > cacheExpiration)
            {
                deserializationCache.TryRemove(entry.Key, out _);
            }
        }

        // If still too many entries, remove oldest ones
        if (deserializationCache.Count > maxCacheSize)
        {
            var oldestEntries = deserializationCache
                .OrderBy(x => x.Value.LastAccess)
                .Take(deserializationCache.Count - (maxCacheSize / 2));

            foreach (var entry in oldestEntries)
            {
                deserializationCache.TryRemove(entry.Key, out _);
            }
        }
    }
}
