// <copyright file="FileTypeDetector.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using ComparisonTool.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Utility for detecting file types and serialization formats.
/// </summary>
public static class FileTypeDetector
{
    /// <summary>
    /// Detect serialization format based on file path extension.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>Detected serialization format.</returns>
    /// <exception cref="NotSupportedException">Thrown when file extension is not supported.</exception>
    public static SerializationFormat DetectFormat(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch {
            ".xml" => SerializationFormat.Xml,
            ".json" => SerializationFormat.Json,
            _ => throw new NotSupportedException($"File extension '{extension}' is not supported. Supported extensions: .xml, .json")
        };
    }

    /// <summary>
    /// Detect serialization format from stream content by examining the first few bytes.
    /// </summary>
    /// <param name="stream">Stream to examine.</param>
    /// <param name="logger">Optional logger for debug information.</param>
    /// <returns>Detected serialization format or null if cannot be determined.</returns>
    public static SerializationFormat? DetectFormatFromContent(Stream stream, ILogger logger = null)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanSeek || !stream.CanRead)
        {
            logger?.LogWarning("Stream is not seekable or readable, cannot detect format from content");
            return null;
        }

        var originalPosition = stream.Position;

        try
        {
            stream.Position = 0;

            // Read the first 1024 bytes to examine content
            var buffer = new byte[Math.Min(1024, stream.Length)];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);

            if (bytesRead == 0)
            {
                logger?.LogWarning("Stream is empty, cannot detect format");
                return null;
            }

            // Convert to string for analysis (assuming UTF-8)
            var content = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            var trimmedContent = content.TrimStart();

            // Detect JSON by looking for opening brace or bracket
            if (trimmedContent.StartsWith("{") || trimmedContent.StartsWith("["))
            {
                logger?.LogDebug("Detected JSON format from content (starts with {{ or [)");
                return SerializationFormat.Json;
            }

            // Detect XML by looking for XML declaration or opening tag
            if (trimmedContent.StartsWith("<?xml") ||
                trimmedContent.StartsWith("<") ||
                trimmedContent.Contains("xmlns"))
                {
                logger?.LogDebug("Detected XML format from content (starts with <?xml or < or contains xmlns)");
                return SerializationFormat.Xml;
            }

            logger?.LogWarning(
                "Could not determine format from content. Content preview: {ContentPreview}",
                trimmedContent.Length > 50 ? trimmedContent[..50] + "..." : trimmedContent);

            return null;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error detecting format from stream content");
            return null;
        }
        finally
        {
            // Restore original position
            stream.Position = originalPosition;
        }
    }

    /// <summary>
    /// Get file extension for a given serialization format.
    /// </summary>
    /// <param name="format">Serialization format.</param>
    /// <returns>File extension including the dot (e.g., ".xml").</returns>
    public static string GetFileExtension(SerializationFormat format)
    {
        return format switch {
            SerializationFormat.Xml => ".xml",
            SerializationFormat.Json => ".json",
            _ => throw new ArgumentException($"Unknown format: {format}")
        };
    }

    /// <summary>
    /// Check if a file path has a supported file extension.
    /// </summary>
    /// <param name="filePath">Path to check.</param>
    /// <returns>True if the file extension is supported.</returns>
    public static bool IsSupportedFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        try
        {
            DetectFormat(filePath);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Get all supported file extensions.
    /// </summary>
    /// <returns>Array of supported extensions including the dot.</returns>
    public static string[] GetSupportedExtensions()
    {
        return new[] { ".xml", ".json" };
    }

    /// <summary>
    /// Get file filter string for file dialogs.
    /// </summary>
    /// <returns>File filter string suitable for file dialogs.</returns>
    public static string GetFileFilter()
    {
        return "Supported Files (*.xml;*.json)|*.xml;*.json|XML Files (*.xml)|*.xml|JSON Files (*.json)|*.json|All Files (*.*)|*.*";
    }
}
