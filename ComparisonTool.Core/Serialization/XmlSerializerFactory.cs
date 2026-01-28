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
        var type = typeof(T);

        // Check if the type has an XmlRootAttribute with a namespace
        var xmlRootAttr = type.GetCustomAttributes(typeof(XmlRootAttribute), true).FirstOrDefault() as XmlRootAttribute;
        var hasNamespacedRoot = xmlRootAttr != null && !string.IsNullOrEmpty(xmlRootAttr.Namespace);

        // If the root has a namespace, we need to override all element namespaces to empty
        // This allows the serializer to deserialize XML regardless of what namespace is present
        if (hasNamespacedRoot) {
            this.ProcessTypeForNamespaceRemoval(type, overrides, new HashSet<Type>());
        }

        // Recursively process all types to remove Order attributes
        this.ProcessTypeForOrderRemoval(type, overrides);

        XmlSerializer serializer;
        if (hasNamespacedRoot) {
            // Create a new XmlRoot attribute without the namespace
            var namespaceIgnorantRootAttr = new XmlRootAttribute(xmlRootAttr!.ElementName) {
                IsNullable = xmlRootAttr.IsNullable,
                Namespace = string.Empty, // Clear the namespace to allow any namespace
            };

            // Use the constructor that takes root directly - this properly applies the namespace override
            // The last parameter (defaultNamespace = "") ensures all elements default to empty namespace
            serializer = new XmlSerializer(type, overrides, Type.EmptyTypes, namespaceIgnorantRootAttr, string.Empty);
        }
        else {
            // No XmlRoot attribute with namespace, use standard constructor with overrides
            serializer = new XmlSerializer(type, overrides);
        }

        // Add event handlers for unknown elements/attributes
        serializer.UnknownElement += this.OnUnknownElement;
        serializer.UnknownAttribute += this.OnUnknownAttribute;
        serializer.UnknownNode += this.OnUnknownNode;

        return serializer;
    }

    /// <summary>
    /// Process a type and all its properties to remove namespace requirements.
    /// This ensures that child elements can be deserialized regardless of namespace.
    /// </summary>
    private void ProcessTypeForNamespaceRemoval(Type type, XmlAttributeOverrides overrides, HashSet<Type> processedTypes) {
        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(decimal)) {
            return;
        }

        if (!processedTypes.Add(type)) {
            return; // Already processed, avoid infinite recursion
        }

        foreach (var property in type.GetProperties()) {
            // Check existing XmlElement attributes
            var xmlElementAttrs = property.GetCustomAttributes<XmlElementAttribute>().ToList();

            // Create new attributes without namespace (or with empty namespace)
            var xmlAttributes = new XmlAttributes();

            if (xmlElementAttrs.Any()) {
                foreach (var attr in xmlElementAttrs) {
                    var newAttr = new XmlElementAttribute(attr.ElementName ?? property.Name) {
                        IsNullable = attr.IsNullable,
                        Namespace = string.Empty, // Force empty namespace
                    };
                    if (attr.Type != null) {
                        newAttr.Type = attr.Type;
                    }

                    if (!string.IsNullOrEmpty(attr.DataType)) {
                        newAttr.DataType = attr.DataType;
                    }

                    xmlAttributes.XmlElements.Add(newAttr);
                }
            }
            else {
                // No XmlElement attribute, create one with empty namespace
                var newAttr = new XmlElementAttribute(property.Name) {
                    Namespace = string.Empty,
                };
                xmlAttributes.XmlElements.Add(newAttr);
            }

            overrides.Add(type, property.Name, xmlAttributes);

            // Recursively process property types
            var propertyType = property.PropertyType;
            if (propertyType.IsClass && propertyType != typeof(string)) {
                this.ProcessTypeForNamespaceRemoval(propertyType, overrides, processedTypes);
            }
            else if (propertyType.IsGenericType) {
                var genericArgs = propertyType.GetGenericArguments();
                foreach (var genericArg in genericArgs) {
                    if (genericArg.IsClass && genericArg != typeof(string)) {
                        this.ProcessTypeForNamespaceRemoval(genericArg, overrides, processedTypes);
                    }
                }
            }
        }
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
