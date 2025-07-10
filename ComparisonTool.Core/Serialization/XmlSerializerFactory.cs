using System.Xml.Serialization;
using ComparisonTool.Core.Models;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Serialization;

public class XmlSerializerFactory
{
    private readonly Dictionary<Type, Func<XmlSerializer>> serializerFactories = new();
    private readonly ILogger<XmlSerializerFactory> _logger;

    public XmlSerializerFactory(ILogger<XmlSerializerFactory> logger = null)
    {
        _logger = logger;
        
        // Register the ComplexOrderResponse with custom configuration
        RegisterType<ComplexOrderResponse>(() => CreateComplexOrderResponseSerializer());
    }

    public void RegisterType<T>(Func<XmlSerializer> factory)
    {
        serializerFactories[typeof(T)] = factory;
    }

    public XmlSerializer GetSerializer<T>()
    {
        if (serializerFactories.TryGetValue(typeof(T), out var factory))
        {
            return factory();
        }

        return CreateDefaultSerializer<T>();
    }

    public XmlSerializer GetSerializer(Type type)
    {
        if (serializerFactories.TryGetValue(type, out var factory))
        {
            return factory();
        }

        return new XmlSerializer(type);
    }

    private XmlSerializer CreateComplexOrderResponseSerializer()
    {
        var serializer = new XmlSerializer(
            typeof(ComplexOrderResponse),
            new XmlRootAttribute
            {
                ElementName = "OrderManagementResponse",
                Namespace = ""
            });

        // Add event handlers to gracefully handle unknown elements and attributes
        serializer.UnknownElement += OnUnknownElement;
        serializer.UnknownAttribute += OnUnknownAttribute;
        serializer.UnknownNode += OnUnknownNode;

        return serializer;
    }

    private XmlSerializer CreateDefaultSerializer<T>()
    {
        var serializer = new XmlSerializer(typeof(T));
        
        // Add event handlers for unknown elements/attributes
        serializer.UnknownElement += OnUnknownElement;
        serializer.UnknownAttribute += OnUnknownAttribute;
        serializer.UnknownNode += OnUnknownNode;

        return serializer;
    }

    private void OnUnknownElement(object sender, XmlElementEventArgs e)
    {
        // Log but don't throw - this allows deserialization to continue
        _logger?.LogDebug("Unknown XML element encountered: {ElementName} at line {LineNumber}, column {LinePosition}. Ignoring element.",
            e.Element.Name, e.LineNumber, e.LinePosition);
    }

    private void OnUnknownAttribute(object sender, XmlAttributeEventArgs e)
    {
        // Log but don't throw - this allows deserialization to continue  
        _logger?.LogDebug("Unknown XML attribute encountered: {AttributeName}='{AttributeValue}' at line {LineNumber}, column {LinePosition}. Ignoring attribute.",
            e.Attr.Name, e.Attr.Value, e.LineNumber, e.LinePosition);
    }

    private void OnUnknownNode(object sender, XmlNodeEventArgs e)
    {
        // Log but don't throw - this allows deserialization to continue
        _logger?.LogDebug("Unknown XML node encountered: {NodeType} '{NodeName}' at line {LineNumber}, column {LinePosition}. Ignoring node.",
            e.NodeType, e.Name, e.LineNumber, e.LinePosition);
    }
}