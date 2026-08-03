using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ParityBench.NET.Plugins;

/// <summary>
/// A fingerprint of a plugin package directory's contents, used to tell a rebuilt
/// package apart from the one already loaded even when its manifest version did not
/// change.
/// </summary>
/// <remarks>
/// The stamp is computed from file metadata alone — no file bytes are read — because
/// discovery runs on every catalog refresh and a package carrying native
/// <c>runtimes/</c> assets would otherwise cost real I/O each time. A compiler always
/// moves the entry assembly's write time, so this detects the case that matters: a
/// client rebuilding a plugin in place. It does <em>not</em> detect a replacement that
/// preserves both length and write time (<c>robocopy /COPY:DAT</c>); bumping the
/// manifest version covers that.
/// </remarks>
internal static class PluginPackageStamp
{
    /// <summary>The stamp reported for a directory that no longer exists.</summary>
    public const string None = "";

    public static string Compute(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        if (!Directory.Exists(packageDirectory))
        {
            return None;
        }

        // Ordering makes the stamp independent of enumeration order, which is not
        // guaranteed to be stable across file systems.
        string[] files = Directory.GetFiles(packageDirectory, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            FileInfo info = new FileInfo(file);
            if (!info.Exists)
            {
                // Deleted between enumeration and stat. Skipping keeps the scan
                // going; the next refresh sees the settled directory.
                continue;
            }

            string relativePath = Path.GetRelativePath(packageDirectory, file)
                .Replace('\\', '/')
                .ToLowerInvariant();

            hash.AppendData(Encoding.UTF8.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"{relativePath}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}\n")));
        }

        return Convert.ToHexStringLower(hash.GetCurrentHash().AsSpan(0, 16));
    }
}
