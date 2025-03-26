namespace ComparisonTool.Core.V2;

/// <summary>
/// Interface for XML deserialization operations
/// </summary>
public interface IXmlDeserializationService
{

    SoapEnvelope DeserializeSoapEnvelopeXml(Stream xmlStream);

    /// <summary>
    /// Register a domain model for XML deserialization
    /// </summary>
    void RegisterDomainModel<T>(string modelName) where T : class;

    /// <summary>
    /// Get all registered domain model names
    /// </summary>
    IEnumerable<string> GetRegisteredModelNames();

    /// <summary>
    /// Get type for a registered model name
    /// </summary>
    Type GetModelType(string modelName);

    /// <summary>
    /// Deserialize an XML stream to the specified model type
    /// </summary>
    T DeserializeXml<T>(Stream xmlStream) where T : class;

    T DeserializeXml<T>(Stream xmlStream, T source);

    /// <summary>
    /// Deserialize an XML stream using a registered model name
    /// </summary>
    object DeserializeXml(Stream xmlStream, string modelName);

    /// <summary>
    /// Creates a deep clone of an object using XML serialization
    /// </summary>
    T CloneObject<T>(T source);
}