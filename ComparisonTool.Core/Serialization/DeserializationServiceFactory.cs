// <copyright file="DeserializationServiceFactory.cs" company="PlaceholderCompany">



using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Serialization;

/// <summary>
/// Factory for creating appropriate deserialization services based on file format.
/// </summary>
public class DeserializationServiceFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<DeserializationServiceFactory> logger;

    public DeserializationServiceFactory(IServiceProvider serviceProvider, ILogger<DeserializationServiceFactory> logger)
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Get the appropriate deserialization service for a specific format.
    /// </summary>
    /// <param name="format">Serialization format.</param>
    /// <returns>Deserialization service for the specified format.</returns>
    public IDeserializationService GetService(SerializationFormat format) =>
        format switch
        {
            SerializationFormat.Xml => GetXmlService(),
            SerializationFormat.Json => GetJsonService(),
            _ => throw new NotSupportedException($"Unsupported serialization format: {format}")
        };

    /// <summary>
    /// Get the appropriate deserialization service based on file path.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>Deserialization service for the detected file format.</returns>
    public IDeserializationService GetService(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        var format = FileTypeDetector.DetectFormat(filePath);
        logger.LogDebug("Detected format {Format} for file {FilePath}", format, filePath);

        return GetService(format);
    }

    /// <summary>
    /// Get the appropriate deserialization service based on stream content.
    /// </summary>
    /// <param name="stream">Stream to analyze.</param>
    /// <param name="fallbackFilePath">Optional file path for fallback format detection.</param>
    /// <returns>Deserialization service for the detected format.</returns>
    public IDeserializationService GetServiceFromContent(Stream stream, string? fallbackFilePath = null)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        // Try to detect format from content first
        var detectedFormat = FileTypeDetector.DetectFormatFromContent(stream, logger);

        if (detectedFormat.HasValue)
        {
            logger.LogDebug("Detected format {Format} from stream content", detectedFormat.Value);
            return GetService(detectedFormat.Value);
        }

        // Fallback to file path if content detection fails
        if (!string.IsNullOrEmpty(fallbackFilePath))
        {
            logger.LogDebug("Content detection failed, falling back to file path detection for {FilePath}", fallbackFilePath);
            return GetService(fallbackFilePath);
        }

        // Default to XML if all detection methods fail (maintain backward compatibility)
        logger.LogWarning("Could not detect format from content or file path, defaulting to XML format");
        return GetXmlService();
    }

    /// <summary>
    /// Get all available deserialization services.
    /// </summary>
    /// <returns>Dictionary of format to service mappings.</returns>
    public Dictionary<SerializationFormat, IDeserializationService> GetAllServices() =>
        new Dictionary<SerializationFormat, IDeserializationService>
        {
            { SerializationFormat.Xml, GetXmlService() },
            { SerializationFormat.Json, GetJsonService() },
        };

    /// <summary>
    /// Get all supported formats across all services.
    /// </summary>
    /// <returns>List of all supported serialization formats.</returns>
    public IEnumerable<SerializationFormat> GetSupportedFormats() => new[] { SerializationFormat.Xml, SerializationFormat.Json };

    /// <summary>
    /// Check if a specific format is supported.
    /// </summary>
    /// <param name="format">Format to check.</param>
    /// <returns>True if the format is supported.</returns>
    public bool IsFormatSupported(SerializationFormat format) => GetSupportedFormats().Contains(format);

    /// <summary>
    /// Register a domain model across all deserialization services.
    /// </summary>
    /// <typeparam name="T">Type to register.</typeparam>
    /// <param name="modelName">Name to identify this model type.</param>
    public void RegisterDomainModelAcrossAllServices<T>(string modelName)
        where T : class
    {
        logger.LogInformation("Registering domain model {ModelName} across all deserialization services", modelName);

        try
        {
            GetXmlService().RegisterDomainModel<T>(modelName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register model {ModelName} with XML service", modelName);
        }

        try
        {
            GetJsonService().RegisterDomainModel<T>(modelName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register model {ModelName} with JSON service", modelName);
        }
    }

    /// <summary>
    /// Get unified deserialization service that can handle multiple formats.
    /// </summary>
    /// <returns>Unified service that delegates to appropriate format-specific services.</returns>
    public IDeserializationService GetUnifiedService() => new UnifiedDeserializationService(this, logger);

    internal IDeserializationService GetXmlService()
    {
        var xmlService = serviceProvider.GetService(typeof(IXmlDeserializationService)) as IXmlDeserializationService;
        if (xmlService == null)
        {
            throw new InvalidOperationException("XML deserialization service is not registered. Ensure AddXmlComparisonServices() is called during service registration.");
        }

        // Wrap the XML service to implement the new interface
        return new XmlDeserializationServiceAdapter(xmlService);
    }

    internal IDeserializationService GetJsonService()
    {
        var jsonService = serviceProvider.GetService(typeof(JsonDeserializationService)) as JsonDeserializationService;
        if (jsonService == null)
        {
            throw new InvalidOperationException("JSON deserialization service is not registered. Ensure AddJsonComparisonServices() is called during service registration.");
        }

        return jsonService;
    }
}

/// <summary>
/// Adapter to make existing IXmlDeserializationService compatible with IDeserializationService.
/// </summary>
internal class XmlDeserializationServiceAdapter : IDeserializationService
{
    private readonly IXmlDeserializationService xmlService;

    public XmlDeserializationServiceAdapter(IXmlDeserializationService xmlService) => this.xmlService = xmlService;

    public IEnumerable<SerializationFormat> SupportedFormats => new[] { SerializationFormat.Xml };

    public void RegisterDomainModel<T>(string modelName)
        where T : class =>
        xmlService.RegisterDomainModel<T>(modelName);

    public IEnumerable<string> GetRegisteredModelNames() => xmlService.GetRegisteredModelNames();

    public Type GetModelType(string modelName) => xmlService.GetModelType(modelName);

    public T Deserialize<T>(Stream stream, SerializationFormat? format = null)
        where T : class
    {
        // Validate format if specified
        if (format.HasValue && format.Value != SerializationFormat.Xml)
        {
            throw new ArgumentException($"XmlDeserializationService only supports XML format, but {format.Value} was specified");
        }

        return xmlService.DeserializeXml<T>(stream);
    }

    public T CloneObject<T>(T source) => xmlService.CloneObject<T>(source);

    public void ClearDeserializationCache() => xmlService.ClearDeserializationCache();

    public (int CacheSize, int SerializerCacheSize) GetCacheStatistics() => xmlService.GetCacheStatistics();

    public void ClearAllCaches() => xmlService.ClearAllCaches();
}

/// <summary>
/// Unified deserialization service that can handle multiple formats.
/// </summary>
internal class UnifiedDeserializationService : IDeserializationService
{
    private readonly DeserializationServiceFactory factory;
    private readonly ILogger logger;
    private readonly Dictionary<string, Type> registeredModels = new Dictionary<string, Type>(StringComparer.Ordinal);

    public UnifiedDeserializationService(DeserializationServiceFactory factory, ILogger logger)
    {
        this.factory = factory;
        this.logger = logger;
    }

    public IEnumerable<SerializationFormat> SupportedFormats => factory.GetSupportedFormats();

    public void RegisterDomainModel<T>(string modelName)
        where T : class
    {
        registeredModels[modelName] = typeof(T);
        factory.RegisterDomainModelAcrossAllServices<T>(modelName);
    }

    public IEnumerable<string> GetRegisteredModelNames()
    {
        // Get models from all services, not just the unified service's internal dictionary
        var allModels = new HashSet<string>(StringComparer.Ordinal);

        // Add models from XML service
        try
        {
            var xmlModels = factory.GetXmlService().GetRegisteredModelNames();
            foreach (var model in xmlModels)
            {
                allModels.Add(model);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get registered models from XML service");
        }

        // Add models from JSON service
        try
        {
            var jsonModels = factory.GetJsonService().GetRegisteredModelNames();
            foreach (var model in jsonModels)
            {
                allModels.Add(model);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get registered models from JSON service");
        }

        // Add models registered directly with this unified service
        foreach (var model in registeredModels.Keys)
        {
            allModels.Add(model);
        }

        return allModels;
    }

    public Type GetModelType(string modelName)
    {
        // Check internal models first
        if (registeredModels.ContainsKey(modelName))
        {
            return registeredModels[modelName];
        }

        // Check XML service
        try
        {
            return factory.GetXmlService().GetModelType(modelName);
        }
        catch (ArgumentException)
        {
            // Model not found in XML service, continue to JSON service
        }

        // Check JSON service
        try
        {
            return factory.GetJsonService().GetModelType(modelName);
        }
        catch (ArgumentException)
        {
            // Model not found in JSON service
        }

        throw new ArgumentException($"No model registered with name: {modelName}");
    }

    public T Deserialize<T>(Stream stream, SerializationFormat? format = null)
        where T : class
    {
        IDeserializationService service;

        if (format.HasValue)
        {
            service = factory.GetService(format.Value);
        }
        else
        {
            // Auto-detect format from content
            service = factory.GetServiceFromContent(stream);
        }

        return service.Deserialize<T>(stream, format);
    }

    public T CloneObject<T>(T source)
    {
        // For cloning, use JSON as it's typically more reliable for round-trip serialization
        var jsonService = factory.GetService(SerializationFormat.Json);
        return jsonService.CloneObject<T>(source);
    }

    public void ClearDeserializationCache()
    {
        foreach (var service in factory.GetAllServices().Values)
        {
            service.ClearDeserializationCache();
        }
    }

    public (int CacheSize, int SerializerCacheSize) GetCacheStatistics()
    {
        var allServices = factory.GetAllServices().Values;
        var totalCacheSize = allServices.Sum(s => s.GetCacheStatistics().CacheSize);
        var totalSerializerCacheSize = allServices.Sum(s => s.GetCacheStatistics().SerializerCacheSize);

        return (totalCacheSize, totalSerializerCacheSize);
    }

    public void ClearAllCaches()
    {
        foreach (var service in factory.GetAllServices().Values)
        {
            service.ClearAllCaches();
        }
    }
}
