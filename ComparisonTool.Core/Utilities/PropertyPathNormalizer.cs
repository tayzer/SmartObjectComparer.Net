// <copyright file="PropertyPathNormalizer.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Utilities;

using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Centralized utility for normalizing property paths across the comparison tool
/// Handles array indices, backing fields, System.Collections paths, and XML namespaces.
/// </summary>
public static class PropertyPathNormalizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Normalize a property path by replacing array indices with wildcards,
    /// removing backing field notation, and normalizing System.Collections paths.
    /// </summary>
    /// <param name="propertyPath">The property path to normalize.</param>
    /// <param name="logger">Optional logger for debug information.</param>
    /// <returns>Normalized property path.</returns>
    public static string NormalizePropertyPath(string propertyPath, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(propertyPath))
        {
            return propertyPath;
        }

        // Replace array indices with [*]
        var normalized = Regex.Replace(propertyPath, @"\[\d+\]", "[*]", RegexOptions.None, RegexTimeout);

        // Remove backing fields (e.g., <PropertyName>k__BackingField)
        normalized = Regex.Replace(normalized, @"<(\w+)>k__BackingField", "$1", RegexOptions.None, RegexTimeout);

        // Remove XML namespace prefixes (e.g., soap:)
        normalized = Regex.Replace(normalized, @"\w+:", string.Empty, RegexOptions.None, RegexTimeout);

        // Normalize System.Collections paths to standard array notation
        // Convert System.Collections.IList.Item[*] to [*]
        var beforeSystemCollections = normalized;
        normalized = Regex.Replace(normalized, @"\.System\.Collections\.IList\.Item\[", "[", RegexOptions.None, RegexTimeout);
        normalized = Regex.Replace(normalized, @"\.System\.Collections\.Generic\.IList`1\.Item\[", "[", RegexOptions.None, RegexTimeout);

        // Handle any other System.Collections variations
        normalized = Regex.Replace(normalized, @"\.System\.Collections\.[^\.]+\.Item\[", "[", RegexOptions.None, RegexTimeout);

        // Log normalization if it occurred and logger is provided
        if (logger != null && !string.Equals(beforeSystemCollections, normalized, StringComparison.Ordinal))
        {
            logger.LogDebug("Normalized property path: '{Original}' -> '{Normalized}'", propertyPath, normalized);
        }

        return normalized;
    }

    /// <summary>
    /// Basic normalization that only handles array indices (for backward compatibility).
    /// </summary>
    /// <param name="propertyPath">The property path to normalize.</param>
    /// <returns>Normalized property path with array indices replaced by [*].</returns>
    public static string NormalizeArrayIndices(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
        {
            return propertyPath;
        }

        return Regex.Replace(propertyPath, @"\[\d+\]", "[*]", RegexOptions.None, RegexTimeout);
    }

    /// <summary>
    /// Check if a property path contains System.Collections notation.
    /// </summary>
    /// <param name="propertyPath">The property path to check.</param>
    /// <returns>True if the path contains System.Collections notation.</returns>
    public static bool ContainsSystemCollections(string propertyPath)
    {
        var result = !string.IsNullOrEmpty(propertyPath) &&
                    (propertyPath.Contains("System.Collections.IList.Item") ||
                     propertyPath.Contains("System.Collections.Generic.IList`1.Item") ||
                     propertyPath.Contains("System.Collections."));

        return result;
    }
}
