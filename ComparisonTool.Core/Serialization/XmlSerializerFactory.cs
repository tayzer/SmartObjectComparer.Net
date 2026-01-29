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

    /// <summary>
    /// Creates a custom serializer for ComplexOrderResponse with specific root element configuration.
    /// Call RegisterType to register this serializer for the ComplexOrderResponse type.
    /// </summary>
    public XmlSerializer CreateComplexOrderResponseSerializer() {
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

    /// <summary>
    /// Creates a namespace-ignorant serializer for a type with a custom root element name.
    /// This properly handles all nested types, clearing namespaces from all child elements.
    /// Use this when you need to deserialize XML with any namespace (or no namespace).
    /// </summary>
    /// <typeparam name="T">The type to create a serializer for.</typeparam>
    /// <param name="rootElementName">The expected XML root element name.</param>
    /// <returns>A fully configured XmlSerializer that ignores namespaces.</returns>
    public XmlSerializer CreateNamespaceIgnorantSerializer<T>(string rootElementName) {
        var overrides = new XmlAttributeOverrides();
        var type = typeof(T);

        // Process ALL types for namespace removal - this is critical for nested elements
        this.ProcessTypeForAttributeNormalization(type, overrides, new HashSet<Type>(), removeNamespaces: true);

        // Create root attribute with empty namespace
        var namespaceIgnorantRootAttr = new XmlRootAttribute(rootElementName) {
            Namespace = string.Empty,
        };

        // Create serializer with all overrides applied
        var serializer = new XmlSerializer(type, overrides, Type.EmptyTypes, namespaceIgnorantRootAttr, string.Empty);

        // Add event handlers for unknown elements/attributes
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

        // Process all types for namespace and order attribute removal in a single pass
        // This avoids conflicts from adding overrides twice for the same property
        this.ProcessTypeForAttributeNormalization(type, overrides, new HashSet<Type>(), hasNamespacedRoot);

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
    /// Process a type and all its properties to normalize XML attributes.
    /// This removes Order attributes and optionally clears namespaces for all elements.
    /// Combined into a single method to avoid conflicts from adding overrides twice.
    /// </summary>
    private void ProcessTypeForAttributeNormalization(Type type, XmlAttributeOverrides overrides, HashSet<Type> processedTypes, bool removeNamespaces) {
        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(decimal)) {
            return;
        }

        if (!processedTypes.Add(type)) {
            return; // Already processed, avoid infinite recursion
        }

        foreach (var property in type.GetProperties()) {
            // Check existing XML attributes
            var xmlElementAttrs = property.GetCustomAttributes<XmlElementAttribute>().ToList();
            var xmlArrayAttr = property.GetCustomAttributes<XmlArrayAttribute>().FirstOrDefault();
            var xmlArrayItemAttrs = property.GetCustomAttributes<XmlArrayItemAttribute>().ToList();

            // Determine if this is an array/collection property (uses XmlArray) vs element property (uses XmlElement)
            var isArrayProperty = xmlArrayAttr != null || xmlArrayItemAttrs.Any();

            // Determine if we need to override this property
            var hasOrderAttribute = xmlElementAttrs.Any(attr => attr.Order > 0);
            var hasNamespace = xmlElementAttrs.Any(attr => !string.IsNullOrEmpty(attr.Namespace)) ||
                               (xmlArrayAttr != null && !string.IsNullOrEmpty(xmlArrayAttr.Namespace)) ||
                               xmlArrayItemAttrs.Any(attr => !string.IsNullOrEmpty(attr.Namespace));

            // Only add override if we need to modify something
            if (removeNamespaces || hasOrderAttribute || hasNamespace) {
                var xmlAttributes = new XmlAttributes();

                // Check if property type is Nullable<T> - if so, IsNullable must be true
                var isNullableValueType = Nullable.GetUnderlyingType(property.PropertyType) != null;

                // CRITICAL: XmlElement and XmlArray/XmlArrayItem are mutually exclusive
                // Only use one or the other based on the original attribute configuration
                if (isArrayProperty) {
                    // Handle XmlArray attribute for collection properties
                    if (xmlArrayAttr != null) {
                        var arrayAttr = new XmlArrayAttribute(xmlArrayAttr.ElementName ?? property.Name) {
                            Namespace = removeNamespaces ? string.Empty : xmlArrayAttr.Namespace,
                        };

                        // Only set IsNullable if it won't conflict with Nullable<T> types
                        if (!isNullableValueType || xmlArrayAttr.IsNullable) {
                            arrayAttr.IsNullable = xmlArrayAttr.IsNullable;
                        }

                        xmlAttributes.XmlArray = arrayAttr;
                    }

                    // Handle XmlArrayItem attributes
                    foreach (var attr in xmlArrayItemAttrs) {
                        var arrayItemAttr = new XmlArrayItemAttribute(attr.ElementName) {
                            Type = attr.Type,
                            Namespace = removeNamespaces ? string.Empty : attr.Namespace,
                        };

                        // Only set IsNullable if it won't conflict with Nullable<T> types
                        if (!isNullableValueType || attr.IsNullable) {
                            arrayItemAttr.IsNullable = attr.IsNullable;
                        }

                        xmlAttributes.XmlArrayItems.Add(arrayItemAttr);
                    }
                }
                else {
                    // Handle XmlElement attributes for non-array properties
                    if (xmlElementAttrs.Any()) {
                        foreach (var attr in xmlElementAttrs) {
                            var newAttr = new XmlElementAttribute(attr.ElementName ?? property.Name) {
                                Namespace = removeNamespaces ? string.Empty : attr.Namespace,
                                // Deliberately exclude Order to prevent deserialization issues
                            };

                            // Only set IsNullable if it won't conflict with Nullable<T> types
                            // For Nullable<T>, IsNullable cannot be false
                            if (!isNullableValueType || attr.IsNullable) {
                                newAttr.IsNullable = attr.IsNullable;
                            }

                            if (attr.Type != null) {
                                newAttr.Type = attr.Type;
                            }

                            if (!string.IsNullOrEmpty(attr.DataType)) {
                                newAttr.DataType = attr.DataType;
                            }

                            xmlAttributes.XmlElements.Add(newAttr);
                        }
                    }
                    else if (removeNamespaces) {
                        // No XmlElement attribute, but we need to ensure empty namespace
                        var newAttr = new XmlElementAttribute(property.Name) {
                            Namespace = string.Empty,
                        };
                        xmlAttributes.XmlElements.Add(newAttr);
                    }
                }

                overrides.Add(type, property.Name, xmlAttributes);
            }

            // Recursively process property types
            var propertyType = property.PropertyType;
            if (propertyType.IsClass && propertyType != typeof(string)) {
                this.ProcessTypeForAttributeNormalization(propertyType, overrides, processedTypes, removeNamespaces);
            }
            else if (propertyType.IsGenericType) {
                var genericArgs = propertyType.GetGenericArguments();
                foreach (var genericArg in genericArgs) {
                    if (genericArg.IsClass && genericArg != typeof(string)) {
                        this.ProcessTypeForAttributeNormalization(genericArg, overrides, processedTypes, removeNamespaces);
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
}
