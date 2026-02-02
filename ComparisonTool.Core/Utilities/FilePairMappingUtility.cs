using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Utility class for creating file pair mappings for comparison operations.
/// </summary>
public static class FilePairMappingUtility
{
    /// <summary>
    /// Creates file pair mappings for side-by-side comparison of two folders.
    /// </summary>
    /// <param name="folder1Files">List of file paths from the first folder.</param>
    /// <param name="folder2Files">List of file paths from the second folder.</param>
    /// <returns>List of file pair mappings with relative paths.</returns>
    public static List<(string file1Path, string file2Path, string relativePath)> CreateFilePairMappings(
        List<string> folder1Files,
        List<string> folder2Files)
    {
        if (folder1Files == null)
        {
            throw new ArgumentNullException(nameof(folder1Files));
        }

        if (folder2Files == null)
        {
            throw new ArgumentNullException(nameof(folder2Files));
        }

        var result = new List<(string file1Path, string file2Path, string relativePath)>();

        // Sort files by name for consistent ordering
        var sortedFolder1 = folder1Files.OrderBy(f => Path.GetFileName(f)).ToList();
        var sortedFolder2 = folder2Files.OrderBy(f => Path.GetFileName(f)).ToList();

        // Use the minimum count between the two folders
        var pairCount = Math.Min(sortedFolder1.Count, sortedFolder2.Count);

        // Create pairs by index (side-by-side comparison)
        for (var i = 0; i < pairCount; i++)
        {
            var file1Path = sortedFolder1[i];
            var file2Path = sortedFolder2[i];
            var relativePath = Path.GetFileName(file1Path);

            result.Add((file1Path, file2Path, relativePath));
        }

        return result;
    }
}
