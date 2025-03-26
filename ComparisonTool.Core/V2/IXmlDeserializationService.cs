namespace ComparisonTool.Core.V2;

/// <summary>
/// Interface for XML deserialization operations
/// </summary>
public interface IXmlDeserializationService
{
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

    T DeserializeXml<T>(Stream xmlStream) where T : class;

    T CloneObject<T>(T source);
}