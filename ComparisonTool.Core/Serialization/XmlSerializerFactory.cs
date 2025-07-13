using System.Reflection;
using System.Xml.Serialization;
using ComparisonTool.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using System.Linq;
using System;
using System.Collections.Generic;

namespace ComparisonTool.Core.Serialization;

public class XmlSerializerFactory
{
    // Thread-safe mapping from model type ⇒ factory delegate that builds a NEW serializer each call.
    private readonly ConcurrentDictionary<Type, Func<XmlSerializer>> _factoryCache = new();
    private readonly ILogger<XmlSerializerFactory> _logger;

    // Per-type locks to ensure only one thread builds a serializer for a given type at a time.
    private static readonly ConcurrentDictionary<Type, object> _buildLocks = new();

    public XmlSerializerFactory(ILogger<XmlSerializerFactory> logger = null)
    {
        _logger = logger;
        
        // Pre-register ComplexOrderResponse with its custom root handling
        RegisterType<ComplexOrderResponse>(() => CreateComplexOrderResponseSerializer());
    }

    public void RegisterType<T>(Func<XmlSerializer> factory)
    {
        _factoryCache[typeof(T)] = factory;
    }

    public XmlSerializer GetSerializer<T>()
    {
        return GetSerializer(typeof(T));
    }

    public XmlSerializer GetSerializer(Type type)
    {
        if (_factoryCache.TryGetValue(type, out var factory))
        {
            return factory(); // Always new instance
        }

        // Ensure single-threaded build for this type to avoid race conditions inside XmlSerializer
        var lockObj = _buildLocks.GetOrAdd(type, _ => new object());
        lock (lockObj)
        {
            // Double-check after acquiring lock
            if (_factoryCache.TryGetValue(type, out factory))
                return factory();

            var newSerializer = CreateFlexibleSerializer(type);
            _factoryCache[type] = () => CreateFlexibleSerializer(type); // factory for future calls
            return newSerializer;
        }
    }

    /// <summary>
    /// Creates a flexible XmlSerializer that ignores Order attributes entirely
    /// This is the new standard method for creating serializers in the factory
    /// </summary>
    public XmlSerializer CreateFlexibleSerializer<T>()
    {
        return CreateFlexibleSerializer(typeof(T));
    }

    private XmlSerializer CreateFlexibleSerializer(Type type)
    {
        // Use the original approach but with Order removal
        return ProcessTypeForOrderRemoval(type);
    }

    private XmlSerializer ProcessTypeForOrderRemoval(Type type)
    {
        var overrides = new XmlAttributeOverrides();
        var visited = new HashSet<Type>();
        ProcessTypeForOrderRemoval(type, overrides, visited);
        var serializer = new XmlSerializer(type, overrides);
        serializer.UnknownElement += (sender, e) => { };
        serializer.UnknownAttribute += (sender, e) => { };
        return serializer;
    }

    private void ProcessTypeForOrderRemoval(Type type, XmlAttributeOverrides overrides)
    {
        var visited = new HashSet<Type>();
        ProcessTypeForOrderRemoval(type, overrides, visited);
    }

    private void ProcessTypeForOrderRemoval(Type type, XmlAttributeOverrides overrides, HashSet<Type> visited)
    {
        if (type == null || type.IsPrimitive || type == typeof(string) || type == typeof(DateTime))
            return;
        if (!visited.Add(type))
            return; // Already processed

        // Check if any property has an Order attribute
        bool hasAnyOrderAttribute = false;
        // Use deterministic ordering to avoid variability between runs
        foreach (var prop in type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var xmlElementAttrs = prop.GetCustomAttributes<XmlElementAttribute>();
            var xmlArrayAttrs = prop.GetCustomAttributes<XmlArrayAttribute>();
            var xmlArrayItemAttrs = prop.GetCustomAttributes<XmlArrayItemAttribute>();
            
            foreach (var attr in xmlElementAttrs)
            {
                try
                {
                    var orderProperty = attr.GetType().GetProperty("Order");
                    if (orderProperty != null && orderProperty.PropertyType == typeof(int))
                    {
                        var orderValue = (int)orderProperty.GetValue(attr);
                        if (orderValue >= 0)
                        {
                            hasAnyOrderAttribute = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // Ignore any reflection errors
                }
            }
            
            if (hasAnyOrderAttribute) break;
            
            foreach (var attr in xmlArrayAttrs)
            {
                try
                {
                    var orderProperty = attr.GetType().GetProperty("Order");
                    if (orderProperty != null && orderProperty.PropertyType == typeof(int))
                    {
                        var orderValue = (int)orderProperty.GetValue(attr);
                        if (orderValue >= 0)
                        {
                            hasAnyOrderAttribute = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // Ignore any reflection errors
                }
            }
            
            if (hasAnyOrderAttribute) break;
            
            foreach (var attr in xmlArrayItemAttrs)
            {
                try
                {
                    var orderProperty = attr.GetType().GetProperty("Order");
                    if (orderProperty != null && orderProperty.PropertyType == typeof(int))
                    {
                        var orderValue = (int)orderProperty.GetValue(attr);
                        if (orderValue >= 0)
                        {
                            hasAnyOrderAttribute = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // Ignore any reflection errors
                }
            }
            
            if (hasAnyOrderAttribute) break;
        }

        if (hasAnyOrderAttribute)
        {
            // Override ALL particle-like members to remove Order
            foreach (var property in type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var xmlElementAttrs = property.GetCustomAttributes<XmlElementAttribute>();
                var xmlArrayAttrs = property.GetCustomAttributes<XmlArrayAttribute>();
                var xmlArrayItemAttrs = property.GetCustomAttributes<XmlArrayItemAttribute>();

                // Only process if this property is a particle-like member (element or array)
                if (xmlElementAttrs.Any() || xmlArrayAttrs.Any())
                {
                    var xmlAttributes = new XmlAttributes();

                    // Override XmlElement attributes
                    foreach (var attr in xmlElementAttrs)
                    {
                        var newAttr = new XmlElementAttribute(attr.ElementName ?? property.Name)
                        {
                            IsNullable = attr.IsNullable,
                            DataType = attr.DataType,
                            Type = attr.Type
                            // Deliberately exclude Order
                        };
                        xmlAttributes.XmlElements.Add(newAttr);
                    }

                    // Override XmlArray attributes
                    if (xmlArrayAttrs.Any())
                    {
                        var xmlArrayAttr = xmlArrayAttrs.First();
                        xmlAttributes.XmlArray = new XmlArrayAttribute(xmlArrayAttr.ElementName)
                        {
                            IsNullable = xmlArrayAttr.IsNullable
                            // Deliberately exclude Order
                        };
                    }

                    // Override XmlArrayItem attributes
                    foreach (var attr in xmlArrayItemAttrs)
                    {
                        xmlAttributes.XmlArrayItems.Add(new XmlArrayItemAttribute(attr.ElementName)
                        {
                            IsNullable = attr.IsNullable,
                            Type = attr.Type
                            // Deliberately exclude Order
                        });
                    }

                    try
                    {
                        overrides.Add(type, property.Name, xmlAttributes);
                    }
                    catch (InvalidOperationException)
                    {
                        // Duplicate entry – log and continue.  Duplicate means this property loses its mapping.
                        _logger?.LogError("Duplicate XmlAttributeOverride ignored for {Type}.{Property}", type.FullName, property.Name);
                    }
                }
            }
        }

        // Recursively process property types
        foreach (var property in type.GetProperties())
        {
            if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
            {
                ProcessTypeForOrderRemoval(property.PropertyType, overrides, visited);
            }
            else if (property.PropertyType.IsGenericType)
            {
                var genericArgs = property.PropertyType.GetGenericArguments();
                foreach (var genericArg in genericArgs)
                {
                    if (genericArg.IsClass && genericArg != typeof(string))
                    {
                        ProcessTypeForOrderRemoval(genericArg, overrides, visited);
                    }
                }
            }
        }
    }

    private XmlSerializer CreateComplexOrderResponseSerializer()
    {
        var type = typeof(ComplexOrderResponse);
        var overrides = new XmlAttributeOverrides();
        // Apply the 'ignore Order' logic to all properties
        ProcessTypeForOrderRemoval(type, overrides);
        // Set the custom root attribute
        var attrs = new XmlAttributes();
        attrs.XmlRoot = new XmlRootAttribute("OrderManagementResponse");
        overrides.Add(type, attrs);
        var serializer = new XmlSerializer(type, overrides);
        serializer.UnknownElement += (sender, e) => { };
        serializer.UnknownAttribute += (sender, e) => { };
        return serializer;
    }

    private XmlSerializer CreateDefaultSerializer<T>()
    {
        // DEPRECATED: Use CreateFlexibleSerializer instead
        // This method is kept for backward compatibility but now delegates to the new approach
        return CreateFlexibleSerializer<T>();
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