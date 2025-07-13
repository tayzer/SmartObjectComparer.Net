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
    
    // Serializer caching removed – we now create a fresh serializer per operation to avoid hidden state issues.

    // Object-level caching disabled – caused shared state issues under concurrency.

    public XmlDeserializationService(ILogger<XmlDeserializationService> logger, XmlSerializerFactory serializerFactory)
    {
        this.logger = logger;
        this.serializerFactory = serializerFactory;
        

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

        // No pre-caching – we build fresh serializers on demand to ensure clean state.
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
            // Always use a fresh serializer (factory supplies new instance)
            var serializer = serializerFactory.GetSerializer<T>();
            xmlStream.Position = 0;

            // Create a fresh XmlReader with consistent settings for every deserialization
            using var reader = XmlReader.Create(xmlStream, GetOptimizedReaderSettings());
            
            // Deserialize using the fresh reader
            var result = (T)serializer.Deserialize(reader);

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
            var serializer = serializerFactory.GetSerializer<T>();
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
        logger.LogWarning("Deserialization cache disabled – nothing to clear.");
    }

    /// <summary>
    /// Get cache statistics for diagnostics
    /// </summary>
    public (int CacheSize, int SerializerCacheSize) GetCacheStatistics() => (0, 0);

    /// <summary>
    /// Force clear all caches - useful for debugging deserialization inconsistencies
    /// </summary>
    public void ClearAllCaches()
    {
        logger.LogWarning("Deserialization cache disabled – nothing to clear.");
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

    // Removed per-thread serializer caching – fresh serializer instances are obtained via factory as needed.


    // Cleanup cache method removed – caching disabled.
}