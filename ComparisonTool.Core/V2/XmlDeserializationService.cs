using System.Xml.Serialization;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.V2;

/// <summary>
/// Service responsible for XML deserialization operations
/// </summary>
public class XmlDeserializationService : IXmlDeserializationService
{
    private readonly ILogger<XmlDeserializationService> _logger;
    private readonly Dictionary<string, Type> _registeredDomainModels = new();

    public XmlDeserializationService(ILogger<XmlDeserializationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a domain model for XML deserialization
    /// </summary>
    /// <typeparam name="T">The type to register</typeparam>
    /// <param name="modelName">Name to identify this model type</param>
    public void RegisterDomainModel<T>(string modelName) where T : class
    {
        _registeredDomainModels[modelName] = typeof(T);
        _logger.LogInformation("Registered model {ModelName} as {ModelType}", modelName, typeof(T).Name);
    }

    /// <summary>
    /// Get all registered domain model names
    /// </summary>
    public IEnumerable<string> GetRegisteredModelNames()
    {
        return _registeredDomainModels.Keys;
    }

    /// <summary>
    /// Get type for a registered model name
    /// </summary>
    /// <param name="modelName">The name of the model to retrieve</param>
    /// <returns>The type associated with the given model name</returns>
    /// <exception cref="ArgumentException">Thrown when no model is registered with the given name</exception>
    public Type GetModelType(string modelName)
    {
        if (!_registeredDomainModels.ContainsKey(modelName))
        {
            _logger.LogError("No model registered with name: {ModelName}", modelName);
            throw new ArgumentException($"No model registered with name: {modelName}");
        }

        return _registeredDomainModels[modelName];
    }

    /// <summary>
    /// Deserialize an XML stream to the specified model type
    /// </summary>
    /// <typeparam name="T">The type to deserialize to</typeparam>
    /// <param name="xmlStream">Stream containing XML data</param>
    /// <returns>The deserialized object</returns>
    /// <exception cref="ArgumentNullException">Thrown when xmlStream is null</exception>
    public T DeserializeXml<T>(Stream xmlStream) where T : class
    {
        if (xmlStream == null)
        {
            _logger.LogError("XML stream cannot be null");
            throw new ArgumentNullException(nameof(xmlStream));
        }

        try
        {
            var xmlSerializer = new XmlSerializer(typeof(T));
            xmlStream.Position = 0;
            return (T)xmlSerializer.Deserialize(xmlStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializing XML to type {Type}", typeof(T).Name);
            throw;
        }
    }

    public SoapEnvelope DeserializeSoapEnvelopeXml(Stream xmlStream)
    {
        if (xmlStream == null)
        {
            _logger.LogError("XML stream cannot be null");
            throw new ArgumentNullException(nameof(xmlStream));
        }

        try
        {
            var xmlSerializer = new XmlSerializer(typeof(SoapEnvelope));
            xmlStream.Position = 0;
            return (SoapEnvelope)xmlSerializer.Deserialize(xmlStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializing XML to type {Type}", nameof(SoapEnvelope));
            throw;
        }
    }

    public T DeserializeXml<T>(Stream xmlStream, T source)
    {
        if (xmlStream == null)
        {
            _logger.LogError("XML stream cannot be null");
            throw new ArgumentNullException(nameof(xmlStream));
        }

        try
        {
            var xmlSerializer = new XmlSerializer(typeof(T));
            xmlStream.Position = 0;
            return (T)xmlSerializer.Deserialize(xmlStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializing XML to type {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Deserialize an XML stream using a registered model name
    /// </summary>
    /// <param name="xmlStream">Stream containing XML data</param>
    /// <param name="modelName">Name of the registered model to use for deserialization</param>
    /// <returns>The deserialized object</returns>
    /// <exception cref="ArgumentException">Thrown when no model is registered with the given name</exception>
    public object DeserializeXml(Stream xmlStream, string modelName)
    {
        if (!_registeredDomainModels.TryGetValue(modelName, out Type modelType))
        {
            _logger.LogError("No model registered with name: {ModelName}", modelName);
            throw new ArgumentException($"No model registered with name: {modelName}");
        }

        try
        {
            var xmlSerializer = new XmlSerializer(modelType);
            xmlStream.Position = 0;
            return xmlSerializer.Deserialize(xmlStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializing XML to model {ModelName}", modelName);
            throw;
        }
    }

    /// <summary>
    /// Creates a deep clone of an object using XML serialization
    /// </summary>
    /// <typeparam name="T">The type of object to clone</typeparam>
    /// <param name="source">The source object to clone</param>
    /// <returns>A deep clone of the source object</returns>
    public T CloneObject<T>(T source)
    {
        if (source == null)
            return default;

        try
        {
            // Serialize to XML and deserialize back to create a clone
            var serializer = new XmlSerializer(typeof(T));
            using var stream = new MemoryStream();
            serializer.Serialize(stream, source);
            stream.Position = 0;
            return (T)serializer.Deserialize(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cloning object of type {Type}", typeof(T).Name);
            throw;
        }
    }
}