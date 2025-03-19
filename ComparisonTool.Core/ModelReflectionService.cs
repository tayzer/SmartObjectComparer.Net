using System.Reflection;

namespace ComparisonTool.Core;

/// <summary>
/// Service to discover properties in domain models using reflection
/// </summary>
public static class ModelReflectionService
{
    /// <summary>
    /// Get all property paths for a given type
    /// </summary>
    public static List<string> GetPropertyPaths(Type type, int maxDepth = 5)
    {
        var paths = new List<string>();
        GetPropertyPathsRecursive(type, string.Empty, paths, 0, maxDepth);
        return paths;
    }

    private static void GetPropertyPathsRecursive(
        Type type,
        string currentPath,
        List<string> paths,
        int currentDepth,
        int maxDepth)
    {
        // Stop if we've reached maximum depth
        if (currentDepth >= maxDepth)
            return;

        // Skip primitives and strings
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(DateTime) || type == typeof(Guid))
            return;

        // Get all properties
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            // Build the current property path
            var propertyPath = string.IsNullOrEmpty(currentPath)
                ? property.Name
                : $"{currentPath}.{property.Name}";

            // Add this property to the list
            paths.Add(propertyPath);

            // For collection properties
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType) &&
                property.PropertyType != typeof(string))
            {
                // Get the collection element type
                Type elementType = null;
                if (property.PropertyType.IsGenericType)
                {
                    var genericArgs = property.PropertyType.GetGenericArguments();
                    if (genericArgs.Length > 0)
                        elementType = genericArgs[0];
                }
                else if (property.PropertyType.IsArray)
                {
                    elementType = property.PropertyType.GetElementType();
                }

                if (elementType != null && !elementType.IsPrimitive && elementType != typeof(string))
                {
                    // Mark collection ordering
                    paths.Add($"{propertyPath}:Order"); // Special marker for collection ordering

                    // Recurse into collection elements
                    GetPropertyPathsRecursive(
                        elementType,
                        $"{propertyPath}[*]", // [*] indicates collection element
                        paths,
                        currentDepth + 1,
                        maxDepth);
                }
            }
            // Recurse into complex properties
            else if (!property.PropertyType.IsPrimitive &&
                     property.PropertyType != typeof(string) &&
                     property.PropertyType != typeof(decimal) &&
                     property.PropertyType != typeof(DateTime) &&
                     property.PropertyType != typeof(Guid))
            {
                GetPropertyPathsRecursive(
                    property.PropertyType,
                    propertyPath,
                    paths,
                    currentDepth + 1,
                    maxDepth);
            }
        }
    }

    /// <summary>
    /// Get property info from a path
    /// </summary>
    public static PropertyInfo GetPropertyFromPath(Type type, string propertyPath)
    {
        var parts = propertyPath.Split('.');
        Type currentType = type;
        PropertyInfo property = null;

        foreach (var part in parts)
        {
            // Handle collection indexers like [*]
            if (part.Contains("["))
            {
                var baseName = part.Substring(0, part.IndexOf('['));
                property = currentType.GetProperty(baseName);

                // Get collection element type
                if (property != null)
                {
                    if (property.PropertyType.IsGenericType)
                    {
                        var genericArgs = property.PropertyType.GetGenericArguments();
                        if (genericArgs.Length > 0)
                            currentType = genericArgs[0];
                    }
                    else if (property.PropertyType.IsArray)
                    {
                        currentType = property.PropertyType.GetElementType();
                    }
                }
            }
            else
            {
                property = currentType.GetProperty(part);
                if (property != null)
                    currentType = property.PropertyType;
            }

            if (property == null)
                return null;
        }

        return property;
    }
}