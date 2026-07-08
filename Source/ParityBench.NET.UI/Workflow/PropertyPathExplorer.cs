using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace ParityBench.NET.UI.Workflow;

internal static class PropertyPathExplorer
{
    private const int MaxDepth = 8;

    public static IReadOnlyList<PropertyPathEntry> GetPropertyPaths(Type? type)
    {
        Dictionary<string, Type> paths = GetAllPropertyPaths(type);
        return paths
            .Select(path => new PropertyPathEntry(path.Key, path.Value, GetFriendlyTypeName(path.Value), IsCollectionType(path.Value), IsPrimitiveType(path.Value)))
            .OrderBy(path => path.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static Dictionary<string, Type> GetAllPropertyPaths(
        Type? type,
        string basePath = "",
        int depth = 0,
        HashSet<Type>? visitedTypes = null)
    {
        Dictionary<string, Type> result = new Dictionary<string, Type>(StringComparer.Ordinal);
        if (type is null)
        {
            return result;
        }

        Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        if (IsPrimitiveType(effectiveType))
        {
            return result;
        }

        visitedTypes ??= new HashSet<Type>();
        if (depth > MaxDepth || visitedTypes.Contains(effectiveType))
        {
            return result;
        }

        visitedTypes.Add(effectiveType);
        bool preferXmlNames = HasXmlMetadata(effectiveType);

        foreach (PropertyInfo property in effectiveType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || ShouldIgnore(property))
            {
                continue;
            }

            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            string segmentName = GetSerializedPropertyName(property, preferXmlNames);
            if (string.IsNullOrWhiteSpace(segmentName))
            {
                continue;
            }

            string prefix = basePath;
            if (depth == 0 && string.IsNullOrEmpty(prefix))
            {
                prefix = GetRootPathPrefix(effectiveType, preferXmlNames);
            }

            string path = string.IsNullOrEmpty(prefix) ? segmentName : $"{prefix}.{segmentName}";
            result[path] = propertyType;

            if (IsCollectionType(propertyType))
            {
                Type? itemType = GetCollectionElementType(propertyType);
                if (itemType is null)
                {
                    continue;
                }

                string itemPath = GetCollectionItemPath(path, property, preferXmlNames);
                Type effectiveItemType = Nullable.GetUnderlyingType(itemType) ?? itemType;
                result[itemPath] = effectiveItemType;

                if (!IsPrimitiveType(effectiveItemType))
                {
                    Dictionary<string, Type> childPaths = GetAllPropertyPaths(effectiveItemType, itemPath, depth + 1, new HashSet<Type>(visitedTypes));
                    foreach (KeyValuePair<string, Type> child in childPaths)
                    {
                        result[child.Key] = child.Value;
                    }
                }

                continue;
            }

            if (!IsPrimitiveType(propertyType))
            {
                Dictionary<string, Type> childPaths = GetAllPropertyPaths(propertyType, path, depth + 1, new HashSet<Type>(visitedTypes));
                foreach (KeyValuePair<string, Type> child in childPaths)
                {
                    result[child.Key] = child.Value;
                }
            }
        }

        return result;
    }

    public static bool IsPrimitiveType(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType.IsPrimitive
            || effectiveType.IsEnum
            || effectiveType == typeof(string)
            || effectiveType == typeof(decimal)
            || effectiveType == typeof(DateTime)
            || effectiveType == typeof(DateTimeOffset)
            || effectiveType == typeof(Guid)
            || effectiveType == typeof(TimeSpan);
    }

    public static bool IsCollectionType(Type? type)
    {
        if (type is null || type == typeof(string))
        {
            return false;
        }

        return typeof(IEnumerable).IsAssignableFrom(type);
    }

    public static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType)
        {
            return type.GetGenericArguments().FirstOrDefault();
        }

        Type? enumerableInterface = type
            .GetInterfaces()
            .FirstOrDefault(interfaceType => interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments().FirstOrDefault();
    }

    public static string GetFriendlyTypeName(Type? type)
    {
        if (type is null)
        {
            return "Unknown";
        }

        Type? nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
        {
            return $"{GetFriendlyTypeName(nullableType)}?";
        }

        if (type.IsArray)
        {
            return $"{GetFriendlyTypeName(type.GetElementType())}[]";
        }

        if (type.IsGenericType)
        {
            string typeName = type.Name.Split('`')[0];
            return $"{typeName}<{string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName))}>";
        }

        return type.Name switch
        {
            nameof(String) => "string",
            nameof(Int32) => "int",
            nameof(Int64) => "long",
            nameof(Boolean) => "bool",
            nameof(Decimal) => "decimal",
            nameof(Double) => "double",
            nameof(Single) => "float",
            _ => type.Name,
        };
    }

    private static bool HasXmlMetadata(MemberInfo member) =>
        member.GetCustomAttribute<XmlRootAttribute>() is not null
        || member.GetCustomAttribute<XmlElementAttribute>() is not null
        || member.GetCustomAttribute<XmlArrayAttribute>() is not null
        || member.GetCustomAttribute<XmlArrayItemAttribute>() is not null
        || member.GetCustomAttribute<XmlAttributeAttribute>() is not null;

    private static bool ShouldIgnore(PropertyInfo property) =>
        property.GetCustomAttribute<JsonIgnoreAttribute>() is not null
        || property.GetCustomAttribute<XmlIgnoreAttribute>() is not null;

    private static string GetRootPathPrefix(Type type, bool preferXmlNames)
    {
        if (!preferXmlNames)
        {
            return string.Empty;
        }

        XmlRootAttribute? rootAttribute = type.GetCustomAttribute<XmlRootAttribute>();
        return string.IsNullOrWhiteSpace(rootAttribute?.ElementName) ? type.Name : rootAttribute.ElementName;
    }

    private static string GetSerializedPropertyName(PropertyInfo property, bool preferXmlNames)
    {
        if (preferXmlNames)
        {
            if (property.GetCustomAttribute<XmlAttributeAttribute>() is not null)
            {
                return string.Empty;
            }

            XmlArrayAttribute? xmlArray = property.GetCustomAttribute<XmlArrayAttribute>();
            if (!string.IsNullOrWhiteSpace(xmlArray?.ElementName))
            {
                return xmlArray.ElementName;
            }

            XmlElementAttribute? xmlElement = property.GetCustomAttribute<XmlElementAttribute>();
            if (!string.IsNullOrWhiteSpace(xmlElement?.ElementName))
            {
                return xmlElement.ElementName;
            }
        }

        string? jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
        return string.IsNullOrWhiteSpace(jsonName) ? property.Name : jsonName;
    }

    private static string GetCollectionItemPath(string path, PropertyInfo property, bool preferXmlNames)
    {
        if (preferXmlNames)
        {
            XmlArrayItemAttribute? xmlItem = property.GetCustomAttribute<XmlArrayItemAttribute>();
            if (!string.IsNullOrWhiteSpace(xmlItem?.ElementName))
            {
                return $"{path}.{xmlItem.ElementName}[*]";
            }
        }

        return $"{path}[*]";
    }
}

internal sealed record PropertyPathEntry(string Path, Type Type, string TypeName, bool IsCollection, bool IsPrimitive);
