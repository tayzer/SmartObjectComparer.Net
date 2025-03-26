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
    private readonly XmlSerializerFactory _serializerFactory;

    public XmlDeserializationService(
        ILogger<XmlDeserializationService> logger,
        XmlSerializerFactory serializerFactory)
    {
        _logger = logger;
        _serializerFactory = serializerFactory;
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

    public T DeserializeXml<T>(Stream xmlStream) where T : class
    {
        if (xmlStream == null)
        {
            _logger.LogError("XML stream cannot be null");
            throw new ArgumentNullException(nameof(xmlStream));
        }

        try
        {
            var serializer = _serializerFactory.GetSerializer<T>();
            xmlStream.Position = 0;
            return (T)serializer.Deserialize(xmlStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializing XML to type {Type}", typeof(T).Name);
            throw;
        }
    }

    public T CloneObject<T>(T source)
    {
        if (source == null)
            return default;

        try
        {
            var serializer = _serializerFactory.GetSerializer<T>();
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