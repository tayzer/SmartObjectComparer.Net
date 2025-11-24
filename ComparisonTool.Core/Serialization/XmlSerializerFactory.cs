// <copyright file="XmlSerializerFactory.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Reflection;
using System.Xml.Serialization;
using ComparisonTool.Core.Models;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Serialization;

public class XmlSerializerFactory {
    private readonly Dictionary<Type, Func<XmlSerializer>> serializerFactories = new();
    private readonly ILogger<XmlSerializerFactory> logger;

    public XmlSerializerFactory(ILogger<XmlSerializerFactory> logger = null) {
        this.logger = logger;

        // Register the ComplexOrderResponse with custom configuration
        this.RegisterType<ComplexOrderResponse>(() => this.CreateComplexOrderResponseSerializer());
    }

    public void RegisterType<T>(Func<XmlSerializer> factory) {
        this.serializerFactories[typeof(T)] = factory;
    }

    public XmlSerializer GetSerializer<T>() {
        if (this.serializerFactories.TryGetValue(typeof(T), out var factory)) {
            return factory();
        }

        return this.CreateDefaultSerializer<T>();
    }

    public XmlSerializer GetSerializer(Type type) {
        if (this.serializerFactories.TryGetValue(type, out var factory)) {
            return factory();
        }

        return new XmlSerializer(type);
    }

    private XmlSerializer CreateComplexOrderResponseSerializer() {
        var serializer = new XmlSerializer(
            typeof(ComplexOrderResponse),
            root:
            new XmlRootAttribute {
                ElementName = "OrderManagementResponse",
                Namespace = string.Empty,
            });

        // Add event handlers to gracefully handle unknown elements and attributes
        serializer.UnknownElement += this.OnUnknownElement;
        serializer.UnknownAttribute += this.OnUnknownAttribute;
        serializer.UnknownNode += this.OnUnknownNode;

        return serializer;
    }

    private XmlSerializer CreateDefaultSerializer<T>() {
        var overrides = new XmlAttributeOverrides();

        // Recursively process all types to remove Order attributes
        this.ProcessTypeForOrderRemoval(typeof(T), overrides);

        var serializer = new XmlSerializer(typeof(T), overrides);

        // Add event handlers for unknown elements/attributes
        serializer.UnknownElement += this.OnUnknownElement;
        serializer.UnknownAttribute += this.OnUnknownAttribute;
        serializer.UnknownNode += this.OnUnknownNode;

        return serializer;
    }

    private void OnUnknownElement(object sender, XmlElementEventArgs e) {
        // Log but don't throw - this allows deserialization to continue
        this.logger?.LogDebug(
            "Unknown XML element encountered: {ElementName} at line {LineNumber}, column {LinePosition}. Ignoring element.",
            e.Element.Name, e.LineNumber, e.LinePosition);
    }

    private void OnUnknownAttribute(object sender, XmlAttributeEventArgs e) {
        // Log but don't throw - this allows deserialization to continue
        this.logger?.LogDebug(
            "Unknown XML attribute encountered: {AttributeName}='{AttributeValue}' at line {LineNumber}, column {LinePosition}. Ignoring attribute.",
            e.Attr.Name, e.Attr.Value, e.LineNumber, e.LinePosition);
    }

    private void OnUnknownNode(object sender, XmlNodeEventArgs e) {
        // Log but don't throw - this allows deserialization to continue
        this.logger?.LogDebug(
            "Unknown XML node encountered: {NodeType} '{NodeName}' at line {LineNumber}, column {LinePosition}. Ignoring node.",
            e.NodeType, e.Name, e.LineNumber, e.LinePosition);
    }

    private void ProcessTypeForOrderRemoval(Type type, XmlAttributeOverrides overrides) {
        // Skip primitive types and already processed types
        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(decimal)) {
            return;
        }

        foreach (var property in type.GetProperties()) {
            var xmlElementAttrs = property.GetCustomAttributes<XmlElementAttribute>();
            var hasOrderAttribute = xmlElementAttrs.Any(attr => attr.Order > 0);

            if (hasOrderAttribute) {
                var xmlAttributes = new XmlAttributes();

                // Recreate XmlElement attributes without Order
                foreach (var attr in xmlElementAttrs) {
                    var newAttr = new XmlElementAttribute(attr.ElementName) {
                        IsNullable = attr.IsNullable,
                        DataType = attr.DataType,
                        Type = attr.Type,

                        // Deliberately exclude Order to prevent deserialization issues
                    };
                    xmlAttributes.XmlElements.Add(newAttr);
                }

                // Handle other XML attributes if present
                var xmlArrayAttrs = property.GetCustomAttributes<XmlArrayAttribute>();
                if (xmlArrayAttrs.Any()) {
                    var xmlArrayAttr = xmlArrayAttrs.First();
                    xmlAttributes.XmlArray = new XmlArrayAttribute(xmlArrayAttr.ElementName) {
                        IsNullable = xmlArrayAttr.IsNullable,
                    };
                }

                var xmlArrayItemAttrs = property.GetCustomAttributes<XmlArrayItemAttribute>();
                foreach (var attr in xmlArrayItemAttrs) {
                    xmlAttributes.XmlArrayItems.Add(new XmlArrayItemAttribute(attr.ElementName) {
                        IsNullable = attr.IsNullable,
                        Type = attr.Type,
                    });
                }

                overrides.Add(type, property.Name, xmlAttributes);
            }

            // Recursively process property types
            if (property.PropertyType.IsClass && property.PropertyType != typeof(string)) {
                this.ProcessTypeForOrderRemoval(property.PropertyType, overrides);
            }
            else if (property.PropertyType.IsGenericType) {
                // Handle generic types like List<T>
                var genericArgs = property.PropertyType.GetGenericArguments();
                foreach (var genericArg in genericArgs) {
                    if (genericArg.IsClass && genericArg != typeof(string)) {
                        this.ProcessTypeForOrderRemoval(genericArg, overrides);
                    }
                }
            }
        }
    }
}
