namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Stages a request directory into the temp batch layout consumed by request parsing.
/// </summary>
public static class RequestBatchDirectoryStager
{
    private static readonly HashSet<string> EligibleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml",
        ".json",
        ".txt",
    };

    public static RequestDirectoryStageResult StageDirectory(string sourceDirectory, string batchDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException("Request directory path cannot be empty.", nameof(sourceDirectory));
        }

        var sourceRoot = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"Request directory was not found: {sourceRoot}");
        }

        var batchRoot = Path.GetFullPath(batchDirectory);
        Directory.CreateDirectory(batchRoot);

        var requestFiles = EnumerateEligibleRequestFiles(sourceRoot);
        var copiedSidecars = 0;

        foreach (var requestFile in requestFiles)
        {
            CopyIntoBatch(sourceRoot, batchRoot, requestFile);

            var sidecarPath = requestFile + ".headers.json";
            if (File.Exists(sidecarPath))
            {
                CopyIntoBatch(sourceRoot, batchRoot, sidecarPath);
                copiedSidecars++;
            }
        }

        return new RequestDirectoryStageResult(requestFiles.Count, copiedSidecars);
    }

    public static IReadOnlyList<string> EnumerateEligibleRequestFiles(string sourceDirectory)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        return Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".headers.json", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).StartsWith("_", StringComparison.Ordinal))
            .Where(path => EligibleExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetRelativePath(sourceRoot, path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetSafeRelativePath(string sourceDirectory, string filePath)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var fullFilePath = Path.GetFullPath(filePath);

        if (!IsPathInsideDirectory(fullFilePath, sourceRoot))
        {
            throw new InvalidOperationException($"File '{filePath}' resolves outside the selected request directory.");
        }

        var relativePath = Path.GetRelativePath(sourceRoot, fullFilePath);
        return NormalizeRelativeRequestPath(relativePath);
    }

    private static void CopyIntoBatch(string sourceRoot, string batchRoot, string sourceFilePath)
    {
        var relativePath = GetSafeRelativePath(sourceRoot, sourceFilePath);
        var destinationPath = GetSafeDestinationPath(batchRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? batchRoot);
        File.Copy(sourceFilePath, destinationPath, overwrite: false);
    }

    private static string GetSafeDestinationPath(string batchRoot, string relativePath)
    {
        var normalizedPath = NormalizeRelativeRequestPath(relativePath);
        var destinationPath = Path.GetFullPath(Path.Combine(batchRoot, normalizedPath));

        if (!IsPathInsideDirectory(destinationPath, batchRoot))
        {
            throw new InvalidOperationException($"Request file path '{relativePath}' resolves outside the staging folder.");
        }

        return destinationPath;
    }

    private static string NormalizeRelativeRequestPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("A selected request file did not include a file name.");
        }

        var normalized = fileName.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized) || !string.IsNullOrWhiteSpace(Path.GetPathRoot(normalized)))
        {
            normalized = Path.GetFileName(normalized);
        }

        var parts = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part != ".")
            .ToArray();

        if (parts.Length == 0)
        {
            throw new InvalidOperationException("A selected request file did not include a valid file name.");
        }

        foreach (var part in parts)
        {
            if (part == "..")
            {
                throw new InvalidOperationException($"Request file name '{fileName}' contains an unsupported parent-directory segment.");
            }

            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException($"Request file name '{fileName}' contains unsupported characters.");
            }
        }

        return Path.Combine(parts);
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var directoryRoot = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(directoryRoot, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record RequestDirectoryStageResult(
    int RequestFileCount,
    int SidecarFileCount);
