using System.Reflection;

namespace ParityBench.NET.Domain;

/// <summary>
/// The version of ParityBench itself, recorded in artefacts that outlive the build
/// that produced them — a baseline captured a year ago should say what wrote it.
/// </summary>
public static class ToolVersion
{
    private static readonly Lazy<string> CurrentVersion = new Lazy<string>(Resolve);

    public static string Current => CurrentVersion.Value;

    private static string Resolve()
    {
        Assembly assembly = typeof(ToolVersion).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            // SourceLink appends "+<commit sha>"; the sha is useful provenance, so it
            // is kept as-is rather than trimmed.
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
