// <copyright file="XmlComparisonOptions.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Xml.Serialization;
using ComparisonTool.Core.Serialization;
using CoreXmlSerializerFactory = ComparisonTool.Core.Serialization.XmlSerializerFactory;

namespace ComparisonTool.Core.DI;

/// <summary>
/// Options for configuring XML comparison services, including domain model registration.
/// </summary>
public class XmlComparisonOptions
{
    private readonly List<Action<CoreXmlSerializerFactory>> serializerRegistrations = new List<Action<CoreXmlSerializerFactory>>();
    private readonly List<Action<IXmlDeserializationService>> domainModelRegistrations = new List<Action<IXmlDeserializationService>>();

    /// <summary>
    /// Register a custom XmlSerializer factory for a specific type.
    /// Use this when your type requires special serialization configuration (custom root element, namespace handling, etc.).
    /// <para>
    /// <strong>Important:</strong> Since namespace-ignorant mode is enabled by default, your serializer should use
    /// <c>Namespace = string.Empty</c> in any XmlRootAttribute. Use <see cref="RegisterDomainModelWithRootElement{T}"/>
    /// for a simpler API that handles this automatically.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <param name="serializerFactory">A factory function that creates the XmlSerializer for this type.</param>
    /// <returns>The options instance for chaining.</returns>
    public XmlComparisonOptions RegisterSerializer<T>(Func<XmlSerializer> serializerFactory)
    {
        serializerRegistrations.Add(factory => factory.RegisterType<T>(serializerFactory));
        return this;
    }

    /// <summary>
    /// Register a domain model for XML deserialization.
    /// This makes the model available for selection in the comparison tool.
    /// The model will use the default namespace-ignorant serializer.
    /// </summary>
    /// <typeparam name="T">The domain model type.</typeparam>
    /// <param name="modelName">The display name for this model in the comparison tool.</param>
    /// <returns>The options instance for chaining.</returns>
    public XmlComparisonOptions RegisterDomainModel<T>(string modelName)
        where T : class
    {
        domainModelRegistrations.Add(service => service.RegisterDomainModel<T>(modelName));
        return this;
    }

    /// <summary>
    /// Register a domain model with a custom root element name.
    /// This is the recommended way to register models that need a specific XML root element name.
    /// The serializer is automatically configured for namespace-ignorant mode, including all nested types.
    /// </summary>
    /// <typeparam name="T">The domain model type.</typeparam>
    /// <param name="modelName">The display name for this model in the comparison tool.</param>
    /// <param name="rootElementName">The XML root element name (e.g., "Envelope", "Order").</param>
    /// <example>
    /// <code>
    /// options.RegisterDomainModelWithRootElement&lt;SoapEnvelope&gt;("SoapEnvelope", "Envelope");
    /// </code>
    /// </example>
    /// <returns>The options instance for chaining.</returns>
    public XmlComparisonOptions RegisterDomainModelWithRootElement<T>(string modelName, string rootElementName)
        where T : class
    {
        // Use the factory's CreateNamespaceIgnorantSerializer which properly handles ALL nested types
        serializerRegistrations.Add(factory =>
            factory.RegisterType<T>(() => factory.CreateNamespaceIgnorantSerializer<T>(rootElementName)));
        RegisterDomainModel<T>(modelName);
        return this;
    }

    /// <summary>
    /// Register a domain model with a custom serializer.
    /// This is a convenience method that registers both the serializer and the domain model.
    /// <para>
    /// <strong>Important:</strong> Since namespace-ignorant mode is enabled by default, your serializer should use
    /// <c>Namespace = string.Empty</c> in any XmlRootAttribute. Consider using
    /// <see cref="RegisterDomainModelWithRootElement{T}"/> for a simpler API.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The domain model type.</typeparam>
    /// <param name="modelName">The display name for this model.</param>
    /// <param name="serializerFactory">A factory function that creates the XmlSerializer for this type.</param>
    /// <returns>The options instance for chaining.</returns>
    public XmlComparisonOptions RegisterDomainModelWithSerializer<T>(string modelName, Func<XmlSerializer> serializerFactory)
        where T : class
    {
        RegisterSerializer<T>(serializerFactory);
        RegisterDomainModel<T>(modelName);
        return this;
    }

    /// <summary>
    /// Applies the serializer registrations to the factory.
    /// </summary>
    /// <param name="factory">The XML serializer factory.</param>
    internal void ApplySerializerRegistrations(CoreXmlSerializerFactory factory)
    {
        foreach (var registration in serializerRegistrations)
        {
            registration(factory);
        }
    }

    /// <summary>
    /// Applies the domain model registrations to the service.
    /// </summary>
    /// <param name="service">The XML deserialization service.</param>
    internal void ApplyDomainModelRegistrations(IXmlDeserializationService service)
    {
        foreach (var registration in domainModelRegistrations)
        {
            registration(service);
        }
    }
}
