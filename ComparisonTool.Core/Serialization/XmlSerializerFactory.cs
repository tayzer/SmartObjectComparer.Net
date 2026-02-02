// <copyright file="XmlSerializerFactory.cs" company="PlaceholderCompany">



using System.Reflection;
using System.Xml.Serialization;
using ComparisonTool.Core.Models;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Serialization;

public class XmlSerializerFactory
{
    private readonly Dictionary<Type, Func<XmlSerializer>> serializerFactories = new Dictionary<Type, Func<XmlSerializer>>();
    private readonly ILogger<XmlSerializerFactory>? logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlSerializerFactory"/> class.
    /// </summary>
    /// <param name="logger">Optional logger instance.</param>
    public XmlSerializerFactory(ILogger<XmlSerializerFactory>? logger = null) => this.logger = logger;

    public void RegisterType<T>(Func<XmlSerializer> factory) => serializerFactories[typeof(T)] = factory;

    /// <summary>
    /// Gets a namespace-agnostic (lenient) serializer for the specified type.
    /// This serializer ignores XML namespaces but preserves xsi:nil for nullable types.
    /// </summary>
    /// <typeparam name="T">The type to serialize/deserialize.</typeparam>
    /// <returns>A namespace-agnostic XmlSerializer.</returns>
    public XmlSerializer GetSerializer<T>()
    {
        if (serializerFactories.TryGetValue(typeof(T), out var factory))
        {
            return factory();
        }

        return CreateDefaultSerializer<T>();
    }

    /// <summary>
    /// Gets a strict serializer for the specified type that requires exact namespace matching.
    /// Use this for production validation where namespace conformance is required.
    /// </summary>
    /// <typeparam name="T">The type to serialize/deserialize.</typeparam>
    /// <returns>A strict XmlSerializer that requires namespace matching.</returns>
    public XmlSerializer GetStrictSerializer<T>()
    {
        if (serializerFactories.TryGetValue(typeof(T), out var factory))
        {
            return factory();
        }

        return CreateStrictSerializer<T>();
    }

    /// <summary>
    /// Gets a serializer based on the specified namespace mode.
    /// </summary>
    /// <typeparam name="T">The type to serialize/deserialize.</typeparam>
    /// <param name="ignoreNamespaces">If true, returns a lenient serializer; if false, returns a strict serializer.</param>
    /// <returns>The appropriate XmlSerializer based on the mode.</returns>
    public XmlSerializer GetSerializer<T>(bool ignoreNamespaces) => ignoreNamespaces ? GetSerializer<T>() : GetStrictSerializer<T>();

    public XmlSerializer GetSerializer(Type type)
    {
        if (serializerFactories.TryGetValue(type, out var factory))
        {
            return factory();
        }

        return new XmlSerializer(type);
    }

    /// <summary>
    /// Creates a custom serializer for ComplexOrderResponse with specific root element configuration.
    /// Call RegisterType to register this serializer for the ComplexOrderResponse type.
    /// </summary>
    /// <returns>The configured XmlSerializer for ComplexOrderResponse.</returns>
    public XmlSerializer CreateComplexOrderResponseSerializer()
    {
        var serializer = new XmlSerializer(
            typeof(ComplexOrderResponse),
            root:
            new XmlRootAttribute
            {
                ElementName = "OrderManagementResponse",
                Namespace = string.Empty,
            });

        // Add event handlers to gracefully handle unknown elements and attributes
        serializer.UnknownElement += OnUnknownElement;
        serializer.UnknownAttribute += OnUnknownAttribute;
        serializer.UnknownNode += OnUnknownNode;

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
    public XmlSerializer CreateNamespaceIgnorantSerializer<T>(string rootElementName)
    {
        var overrides = new XmlAttributeOverrides();
        var type = typeof(T);

        // Process ALL types for namespace removal - this is critical for nested elements
        ProcessTypeForAttributeNormalization(type, overrides, new HashSet<Type>(), removeNamespaces: true);

        // Create root attribute with empty namespace
        var namespaceIgnorantRootAttr = new XmlRootAttribute(rootElementName)
        {
            Namespace = string.Empty,
        };

        // Create serializer with all overrides applied
        var serializer = new XmlSerializer(type, overrides, Type.EmptyTypes, namespaceIgnorantRootAttr, string.Empty);

        // Add event handlers for unknown elements/attributes
        serializer.UnknownElement += OnUnknownElement;
        serializer.UnknownAttribute += OnUnknownAttribute;
        serializer.UnknownNode += OnUnknownNode;

        return serializer;
    }

    private XmlSerializer CreateDefaultSerializer<T>()
    {
        var overrides = new XmlAttributeOverrides();
        var type = typeof(T);

        // Check if the type has an XmlRootAttribute with a namespace
        var xmlRootAttr = type.GetCustomAttributes(typeof(XmlRootAttribute), true).FirstOrDefault() as XmlRootAttribute;
        var hasNamespacedRoot = xmlRootAttr != null && !string.IsNullOrEmpty(xmlRootAttr.Namespace);

        // Process all types for namespace and order attribute removal in a single pass
        // This avoids conflicts from adding overrides twice for the same property
        ProcessTypeForAttributeNormalization(type, overrides, new HashSet<Type>(), hasNamespacedRoot);

        XmlSerializer serializer;
        if (hasNamespacedRoot)
        {
            // Create a new XmlRoot attribute without the namespace
            var namespaceIgnorantRootAttr = new XmlRootAttribute(xmlRootAttr!.ElementName)
            {
                IsNullable = xmlRootAttr.IsNullable,
                Namespace = string.Empty, // Clear the namespace to allow any namespace
            };

            // Use the constructor that takes root directly - this properly applies the namespace override
            // The last parameter (defaultNamespace = "") ensures all elements default to empty namespace
            serializer = new XmlSerializer(type, overrides, Type.EmptyTypes, namespaceIgnorantRootAttr, string.Empty);
        }
        else
        {
            // No XmlRoot attribute with namespace, use standard constructor with overrides
            serializer = new XmlSerializer(type, overrides);
        }

        // Add event handlers for unknown elements/attributes
        serializer.UnknownElement += OnUnknownElement;
        serializer.UnknownAttribute += OnUnknownAttribute;
        serializer.UnknownNode += OnUnknownNode;

        return serializer;
    }

    /// <summary>
    /// Creates a strict serializer that requires exact namespace matching.
    /// This serializer respects the original XmlRoot/XmlElement namespace attributes
    /// and will fail if the XML namespaces don't match the model definition.
    /// </summary>
    private XmlSerializer CreateStrictSerializer<T>()
    {
        var type = typeof(T);

        // Use a simple serializer without namespace overrides - this enforces strict namespace matching
        var serializer = new XmlSerializer(type);

        // Add event handlers for unknown elements/attributes
        serializer.UnknownElement += OnUnknownElement;
        serializer.UnknownAttribute += OnUnknownAttribute;
        serializer.UnknownNode += OnUnknownNode;

        return serializer;
    }

    /// <summary>
    /// Process a type and all its properties to normalize XML attributes.
    /// This removes Order attributes and optionally clears namespaces for all elements.
    /// Combined into a single method to avoid conflicts from adding overrides twice.
    /// </summary>
    private void ProcessTypeForAttributeNormalization(Type type, XmlAttributeOverrides overrides, HashSet<Type> processedTypes, bool removeNamespaces)
    {
        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(decimal))
        {
            return;
        }

        if (!processedTypes.Add(type))
        {
            return; // Already processed, avoid infinite recursion
        }

        foreach (var property in type.GetProperties())
        {
            ProcessPropertyForAttributeNormalization(property, type, overrides, removeNamespaces);
            ProcessPropertyTypeRecursively(property.PropertyType, overrides, processedTypes, removeNamespaces);
        }
    }

    private void ProcessPropertyForAttributeNormalization(PropertyInfo property, Type type, XmlAttributeOverrides overrides, bool removeNamespaces)
    {
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
        if (!removeNamespaces && !hasOrderAttribute && !hasNamespace)
        {
            return;
        }

        var xmlAttributes = new XmlAttributes();

        // Check if property type is Nullable<T> - if so, IsNullable must be true
        var isNullableValueType = Nullable.GetUnderlyingType(property.PropertyType) != null;

        // CRITICAL: XmlElement and XmlArray/XmlArrayItem are mutually exclusive
        if (isArrayProperty)
        {
            ProcessArrayProperty(xmlAttributes, xmlArrayAttr, xmlArrayItemAttrs, property, isNullableValueType, removeNamespaces);
        }
        else
        {
            ProcessElementProperty(xmlAttributes, xmlElementAttrs, property, isNullableValueType, removeNamespaces);
        }

        overrides.Add(type, property.Name, xmlAttributes);
    }

    private void ProcessArrayProperty(
        XmlAttributes xmlAttributes,
        XmlArrayAttribute? xmlArrayAttr,
        List<XmlArrayItemAttribute> xmlArrayItemAttrs,
        PropertyInfo property,
        bool isNullableValueType,
        bool removeNamespaces)
    {
        // Handle XmlArray attribute for collection properties
        if (xmlArrayAttr != null)
        {
            var arrayAttr = new XmlArrayAttribute(xmlArrayAttr.ElementName ?? property.Name)
            {
                Namespace = removeNamespaces ? string.Empty : xmlArrayAttr.Namespace,
            };

            // Only set IsNullable if it won't conflict with Nullable<T> types
            if (!isNullableValueType || xmlArrayAttr.IsNullable)
            {
                arrayAttr.IsNullable = xmlArrayAttr.IsNullable;
            }

            xmlAttributes.XmlArray = arrayAttr;
        }

        // Handle XmlArrayItem attributes
        foreach (var attr in xmlArrayItemAttrs)
        {
            var arrayItemAttr = new XmlArrayItemAttribute(attr.ElementName)
            {
                Type = attr.Type,
                Namespace = removeNamespaces ? string.Empty : attr.Namespace,
            };

            // Only set IsNullable if it won't conflict with Nullable<T> types
            if (!isNullableValueType || attr.IsNullable)
            {
                arrayItemAttr.IsNullable = attr.IsNullable;
            }

            xmlAttributes.XmlArrayItems.Add(arrayItemAttr);
        }
    }

    private void ProcessElementProperty(
        XmlAttributes xmlAttributes,
        List<XmlElementAttribute> xmlElementAttrs,
        PropertyInfo property,
        bool isNullableValueType,
        bool removeNamespaces)
    {
        // Handle XmlElement attributes for non-array properties
        if (xmlElementAttrs.Any())
        {
            foreach (var attr in xmlElementAttrs)
            {
                var newAttr = new XmlElementAttribute(attr.ElementName ?? property.Name)
                {
                    Namespace = removeNamespaces ? string.Empty : attr.Namespace,

                    // Deliberately exclude Order to prevent deserialization issues
                };

                // Only set IsNullable if it won't conflict with Nullable<T> types
                // For Nullable<T>, IsNullable cannot be false
                if (!isNullableValueType || attr.IsNullable)
                {
                    newAttr.IsNullable = attr.IsNullable;
                }

                if (attr.Type != null)
                {
                    newAttr.Type = attr.Type;
                }

                if (!string.IsNullOrEmpty(attr.DataType))
                {
                    newAttr.DataType = attr.DataType;
                }

                xmlAttributes.XmlElements.Add(newAttr);
            }
        }
        else if (removeNamespaces)
        {
            // No XmlElement attribute, but we need to ensure empty namespace
            var newAttr = new XmlElementAttribute(property.Name)
            {
                Namespace = string.Empty,
            };
            xmlAttributes.XmlElements.Add(newAttr);
        }
    }

    private void ProcessPropertyTypeRecursively(Type propertyType, XmlAttributeOverrides overrides, HashSet<Type> processedTypes, bool removeNamespaces)
    {
        // Recursively process property types
        if (propertyType.IsClass && propertyType != typeof(string))
        {
            ProcessTypeForAttributeNormalization(propertyType, overrides, processedTypes, removeNamespaces);
        }
        else if (propertyType.IsGenericType)
        {
            var genericArgs = propertyType.GetGenericArguments();
            foreach (var genericArg in genericArgs)
            {
                if (genericArg.IsClass && genericArg != typeof(string))
                {
                    ProcessTypeForAttributeNormalization(genericArg, overrides, processedTypes, removeNamespaces);
                }
            }
        }
    }

    private void OnUnknownElement(object? sender, XmlElementEventArgs e) =>
        logger?.LogDebug(
            "Unknown XML element encountered: {ElementName} at line {LineNumber}, column {LinePosition}. Ignoring element.",
            e.Element.Name,
            e.LineNumber,
            e.LinePosition);

    private void OnUnknownAttribute(object? sender, XmlAttributeEventArgs e) =>
        logger?.LogDebug(
            "Unknown XML attribute encountered: {AttributeName}='{AttributeValue}' at line {LineNumber}, column {LinePosition}. Ignoring attribute.",
            e.Attr.Name,
            e.Attr.Value,
            e.LineNumber,
            e.LinePosition);

    private void OnUnknownNode(object? sender, XmlNodeEventArgs e) =>
        logger?.LogDebug(
            "Unknown XML node encountered: {NodeType} '{NodeName}' at line {LineNumber}, column {LinePosition}. Ignoring node.",
            e.NodeType,
            e.Name,
            e.LineNumber,
            e.LinePosition);
}
