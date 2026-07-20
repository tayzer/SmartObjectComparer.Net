using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace ComparisonTool.Web.Components.Comparison;

internal static class PropertyPathExplorer
{
    private const int MaxDepth = 8;

    public static Dictionary<string, Type> GetAllPropertyPaths(
        Type? type,
        string basePath = "",
        int depth = 0,
        HashSet<Type>? visitedTypes = null)
    {
        var result = new Dictionary<string, Type>(StringComparer.Ordinal);

        if (type == null)
        {
            return result;
        }

        visitedTypes ??= new HashSet<Type>();
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        var preferXmlNames = HasXmlMetadata(effectiveType);

        if (depth > MaxDepth || visitedTypes.Contains(effectiveType))
        {
            return result;
        }

        visitedTypes.Add(effectiveType);

        foreach (var property in effectiveType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || ShouldIgnore(property))
            {
                continue;
            }

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var segmentName = GetSerializedPropertyName(property, preferXmlNames);
            if (string.IsNullOrWhiteSpace(segmentName))
            {
                continue;
            }

            var prefix = basePath;
            if (depth == 0 && string.IsNullOrEmpty(prefix))
            {
                prefix = GetRootPathPrefix(effectiveType, preferXmlNames);
            }

            var path = string.IsNullOrEmpty(prefix)
                ? segmentName
                : $"{prefix}.{segmentName}";

            result[path] = propertyType;

            if (IsCollectionType(propertyType))
            {
                var itemType = GetCollectionElementType(propertyType);
                var itemPath = GetCollectionItemPath(path, property, itemType, preferXmlNames);
                if (itemType != null)
                {
                    result[itemPath] = itemType;

                    if (!IsPrimitiveType(itemType))
                    {
                        var childPaths = GetAllPropertyPaths(itemType, itemPath, depth + 1, new HashSet<Type>(visitedTypes));
                        foreach (var child in childPaths)
                        {
                            result[child.Key] = child.Value;
                        }
                    }
                }

                continue;
            }

            if (!IsPrimitiveType(propertyType) && propertyType != typeof(string))
            {
                var childPaths = GetAllPropertyPaths(propertyType, path, depth + 1, new HashSet<Type>(visitedTypes));
                foreach (var child in childPaths)
                {
                    result[child.Key] = child.Value;
                }
            }
        }

        return result;
    }

    public static bool IsPrimitiveType(Type? type)
    {
        if (type == null)
        {
            return false;
        }

        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

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
        if (type == null || type == typeof(string))
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

        var enumerableInterface = type
            .GetInterfaces()
            .FirstOrDefault(interfaceType => interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments().FirstOrDefault();
    }

    public static string GetFriendlyTypeName(Type? type)
    {
        if (type == null)
        {
            return "Unknown";
        }

        var effectiveType = Nullable.GetUnderlyingType(type);
        if (effectiveType != null)
        {
            return $"{GetFriendlyTypeName(effectiveType)}?";
        }

        if (type.IsArray)
        {
            return $"{GetFriendlyTypeName(type.GetElementType())}[]";
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            return $"List<{GetFriendlyTypeName(type.GetGenericArguments()[0])}>";
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
            nameof(DateTime) => "DateTime",
            nameof(DateTimeOffset) => "DateTimeOffset",
            _ => type.Name
        };
    }

    private static bool HasXmlMetadata(MemberInfo member)
    {
        return member.GetCustomAttribute<XmlRootAttribute>() != null
            || member.GetCustomAttribute<XmlElementAttribute>() != null
            || member.GetCustomAttribute<XmlArrayAttribute>() != null
            || member.GetCustomAttribute<XmlArrayItemAttribute>() != null
            || member.GetCustomAttribute<XmlAttributeAttribute>() != null;
    }

    private static bool ShouldIgnore(PropertyInfo property)
    {
        return property.GetCustomAttribute<JsonIgnoreAttribute>() != null
            || property.GetCustomAttribute<XmlIgnoreAttribute>() != null;
    }

    private static string GetRootPathPrefix(Type type, bool preferXmlNames)
    {
        if (!preferXmlNames)
        {
            return string.Empty;
        }

        var rootAttribute = type.GetCustomAttribute<XmlRootAttribute>();
        return string.IsNullOrWhiteSpace(rootAttribute?.ElementName)
            ? type.Name
            : rootAttribute.ElementName;
    }

    private static string GetSerializedPropertyName(PropertyInfo property, bool preferXmlNames)
    {
        if (preferXmlNames)
        {
            var xmlAttribute = property.GetCustomAttribute<XmlAttributeAttribute>();
            if (xmlAttribute != null)
            {
                return string.Empty;
            }

            var xmlArray = property.GetCustomAttribute<XmlArrayAttribute>();
            if (!string.IsNullOrWhiteSpace(xmlArray?.ElementName))
            {
                return xmlArray.ElementName;
            }

            var xmlElement = property.GetCustomAttribute<XmlElementAttribute>();
            if (!string.IsNullOrWhiteSpace(xmlElement?.ElementName))
            {
                return xmlElement.ElementName;
            }
        }

        var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
        return string.IsNullOrWhiteSpace(jsonName) ? property.Name : jsonName;
    }

    private static string GetCollectionItemPath(string path, PropertyInfo property, Type? itemType, bool preferXmlNames)
    {
        if (preferXmlNames)
        {
            var xmlItem = property.GetCustomAttribute<XmlArrayItemAttribute>();
            if (!string.IsNullOrWhiteSpace(xmlItem?.ElementName))
            {
                return $"{path}.{xmlItem.ElementName}[*]";
            }
        }

        return $"{path}[*]";
    }
}