using System.Globalization;

namespace ParityBench.NET.Plugins;

/// <summary>
/// Copies a plugin package to a private directory before it is loaded, so the load
/// context never memory-maps the installed package itself.
/// </summary>
/// <remarks>
/// Without this a client cannot rebuild their plugin while the app is running: Windows
/// keeps the mapped assembly locked, and the build fails with a file-in-use error. The
/// copy also makes eviction possible, since a superseded package's files can be deleted
/// while its (still-mapped) copy lingers.
/// </remarks>
internal static class PluginShadowCopy
{
    /// <summary>Sessions older than this belonged to a process that did not clean up after itself.</summary>
    private static readonly TimeSpan StaleSessionAge = TimeSpan.FromHours(24);

    public static string DefaultRoot => Path.Combine(Path.GetTempPath(), "ParityBench.NET", "plugin-shadow");

    /// <summary>Creates a directory private to this loader, so concurrent processes never share copies.</summary>
    public static string CreateSessionDirectory(string root)
    {
        string sessionDirectory = Path.Combine(
            root,
            string.Create(
                CultureInfo.InvariantCulture,
                $"s{Environment.ProcessId}-{Guid.NewGuid().ToString("n")[..8]}"));

        Directory.CreateDirectory(sessionDirectory);
        return sessionDirectory;
    }

    /// <summary>
    /// Copies <paramref name="package"/> into <paramref name="sessionDirectory"/> and
    /// returns the copy's path. The content stamp is part of the directory name, so an
    /// existing target holds identical content and is reused.
    /// </summary>
    public static string CopyPackage(PluginPackage package, string sessionDirectory)
    {
        string targetPath = Path.Combine(
            sessionDirectory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Sanitize(package.Manifest.Id, 40)}_{Sanitize(package.Manifest.Version, 24)}_{Stamp(package)}"));

        if (Directory.Exists(targetPath))
        {
            return targetPath;
        }

        // Build the copy under a temporary name and move it into place, so a copy
        // interrupted part-way can never be mistaken for a complete package.
        string stagingPath = string.Create(CultureInfo.InvariantCulture, $"{targetPath}.tmp-{Guid.NewGuid():n}");
        try
        {
            CopyDirectory(package.DirectoryPath, stagingPath);
            MoveIntoPlace(stagingPath, targetPath);
        }
        catch (IOException) when (Directory.Exists(targetPath))
        {
            // Another thread finished the same copy first. Its content is identical.
            TryDelete(stagingPath);
        }
        catch
        {
            TryDelete(stagingPath);
            throw;
        }

        return targetPath;
    }

    /// <summary>
    /// Publishes the staged copy, retrying briefly because a scanner or indexer that
    /// opened the files we just wrote holds the directory for a moment and makes the
    /// move fail with "access is denied".
    /// </summary>
    private static void MoveIntoPlace(string stagingPath, string targetPath)
    {
        const int attempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(stagingPath, targetPath);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                && attempt < attempts
                && !Directory.Exists(targetPath))
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    /// <summary>Best-effort delete. A directory holding a mapped assembly stays until the process exits.</summary>
    public static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes session directories left behind by processes that crashed. A session
    /// belonging to a process that is still running holds locked assemblies, so its
    /// delete fails and it is left alone — which is the wanted outcome.
    /// </summary>
    public static void TryPurgeStaleSessions(string root, string ownSessionDirectory)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            DateTime cutoff = DateTime.UtcNow - StaleSessionAge;
            foreach (string sessionDirectory in Directory.EnumerateDirectories(root))
            {
                if (!string.Equals(sessionDirectory, ownSessionDirectory, StringComparison.OrdinalIgnoreCase)
                    && Directory.GetLastWriteTimeUtc(sessionDirectory) < cutoff)
                {
                    TryDelete(sessionDirectory);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        // Everything, recursively: AssemblyDependencyResolver reads the package's
        // .deps.json and resolves native dependencies out of runtimes/<rid>/native, so
        // a top-level-only copy would silently break any plugin carrying one. The .pdb
        // files come along to keep line numbers in plugin stack traces.
        foreach (string sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string targetPath = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, sourcePath));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    /// <summary>
    /// Keeps the path segment short and legal. Deep <c>runtimes/&lt;rid&gt;/native</c>
    /// subtrees eat the path budget on machines without long paths enabled.
    /// </summary>
    private static string Sanitize(string value, int maximumLength)
    {
        Span<char> sanitized = stackalloc char[Math.Min(value.Length, maximumLength)];
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int index = 0; index < sanitized.Length; index++)
        {
            sanitized[index] = Array.IndexOf(invalid, value[index]) >= 0 ? '-' : value[index];
        }

        return sanitized.IsEmpty ? "package" : new string(sanitized);
    }

    private static string Stamp(PluginPackage package) =>
        string.IsNullOrEmpty(package.ContentStamp) ? Guid.NewGuid().ToString("n")[..16] : package.ContentStamp[..16];
}
