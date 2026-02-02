// <copyright file="XmlComparisonOptions.cs" company="PlaceholderCompany">



using System.Xml.Serialization;
using ComparisonTool.Core.Serialization;
using CoreXmlSerializerFactory = ComparisonTool.Core.Serialization.XmlSerializerFactory;

namespace ComparisonTool.Core.DI;

/// <summary>
/// Options for configuring XML comparison services including domain model registration
/// and custom serializer factories.
/// </summary>
public class XmlComparisonOptions
{
    private readonly List<(Type Type, Func<XmlSerializer> Factory)> serializerRegistrations = new();
    private readonly List<Action<IXmlDeserializationService>> domainModelRegistrations = new();

    /// <summary>
    /// Gets or sets a value indicating whether to ignore XML namespaces during deserialization.
    /// <para>
    /// <b>Lenient mode (true, default):</b> Uses namespace-agnostic serializer overrides.
    /// XML documents with any namespace (or no namespace) will deserialize correctly.
    /// </para>
    /// <para>
    /// <b>Strict mode (false):</b> Requires XML namespaces to exactly match the
    /// registered domain model's XmlRoot/XmlElement namespace attributes.
    /// </para>
    /// </summary>
    public bool IgnoreXmlNamespaces { get; set; } = true;

    /// <summary>
    /// Register a custom serializer factory for a specific type.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <param name="factory">Factory function that creates the XmlSerializer.</param>
    public void RegisterSerializer<T>(Func<XmlSerializer> factory) => serializerRegistrations.Add((typeof(T), factory));

    /// <summary>
    /// Register a domain model type for deserialization by name.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <param name="modelName">The name used to identify this model.</param>
    public void RegisterDomainModel<T>(string modelName)
        where T : class =>
        domainModelRegistrations.Add(service => service.RegisterDomainModel<T>(modelName));

    /// <summary>
    /// Register a domain model with a custom serializer.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <param name="modelName">The name used to identify this model.</param>
    /// <param name="serializerFactory">Factory function that creates the XmlSerializer.</param>
    public void RegisterDomainModelWithSerializer<T>(string modelName, Func<XmlSerializer> serializerFactory)
        where T : class
    {
        RegisterSerializer<T>(serializerFactory);
        RegisterDomainModel<T>(modelName);
    }

    /// <summary>
    /// Register a domain model with a custom root element name.
    /// This is useful when the XML root element name differs from the CLR type name.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <param name="modelName">The name used to identify this model.</param>
    /// <param name="rootElementName">The XML root element name to use during deserialization.</param>
    public void RegisterDomainModelWithRootElement<T>(string modelName, string rootElementName)
        where T : class
    {
        // Store the registration to be applied when the factory is available
        // We need to capture rootElementName for use later with the XmlSerializerFactory
        serializerRegistrations.Add((typeof(T), () =>
        {
            // Create a factory that will be called with the XmlSerializerFactory
            // For now, create a basic serializer - the actual namespace-ignorant one will be created
            // when ApplySerializerRegistrations is called with access to the factory
            var factory = new CoreXmlSerializerFactory(null);
            return factory.CreateNamespaceIgnorantSerializer<T>(rootElementName);
        }));
        RegisterDomainModel<T>(modelName);
    }

    /// <summary>
    /// Apply all registered serializer factories to the given factory.
    /// </summary>
    /// <param name="factory">The serializer factory to configure.</param>
    internal void ApplySerializerRegistrations(CoreXmlSerializerFactory factory)
    {
        foreach (var (type, serializerFactory) in serializerRegistrations)
        {
            // Use reflection to call the generic RegisterType method
            var method = typeof(CoreXmlSerializerFactory).GetMethod(nameof(CoreXmlSerializerFactory.RegisterType));
            var genericMethod = method?.MakeGenericMethod(type);
            genericMethod?.Invoke(factory, new object[] { serializerFactory });
        }
    }

    /// <summary>
    /// Apply all registered domain model registrations to the given service.
    /// </summary>
    /// <param name="service">The deserialization service to configure.</param>
    internal void ApplyDomainModelRegistrations(IXmlDeserializationService service)
    {
        // Apply the namespace mode setting
        if (service is XmlDeserializationService xmlService)
        {
            xmlService.IgnoreXmlNamespaces = IgnoreXmlNamespaces;
        }

        foreach (var registration in domainModelRegistrations)
        {
            registration(service);
        }
    }
}
