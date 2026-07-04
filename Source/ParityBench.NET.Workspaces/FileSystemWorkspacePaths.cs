using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Workspaces;

internal static class FileSystemWorkspacePaths
{
    public static string NormalizeRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root must not be empty.", nameof(workspaceRoot));
        }

        return Path.GetFullPath(workspaceRoot);
    }

    public static string GetSafePath(string root, string relativePath)
    {
        string normalizedRelativePath = new RequestItem(relativePath).RelativePath;
        string fullPath = Path.GetFullPath(Path.Combine(
            root,
            normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsPathInsideDirectory(fullPath, root))
        {
            throw new InvalidOperationException($"Workspace path '{relativePath}' resolves outside the workspace root.");
        }

        return fullPath;
    }

    public static string ToLogicalPath(params string[] parts) =>
        string.Join('/', parts.SelectMany(part => part.Split(
            new[] { '/', '\\' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

    public static bool IsPathInsideDirectory(string path, string directory)
    {
        string normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        string normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
