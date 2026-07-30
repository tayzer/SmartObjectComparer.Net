using System.Text.Json;
using System.Text.Json.Serialization;

namespace ParityBench.PluginSdk.Plugin;

/// <summary>
/// The <c>parity-plugin.json</c> shipped in a plugin package. The host reads this
/// without loading any plugin assembly, so an incompatible or malformed package is
/// rejected before its code can run.
/// </summary>
public sealed record PluginManifest
{
    public const string FileName = "parity-plugin.json";

    /// <summary>
    /// The SDK contract version this manifest format belongs to. A package
    /// declaring a different major version is refused by the loader.
    /// </summary>
    public const int CurrentSdkVersion = 1;

    [JsonConstructor]
    public PluginManifest(
        string id,
        string version,
        string entryAssembly,
        int sdkVersion = CurrentSdkVersion,
        string? displayName = null,
        string? description = null,
        string? publisher = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Plugin id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Plugin version must not be empty.", nameof(version));
        }

        if (string.IsNullOrWhiteSpace(entryAssembly))
        {
            throw new ArgumentException("Plugin entry assembly must not be empty.", nameof(entryAssembly));
        }

        Id = id.Trim();
        Version = version.Trim();
        EntryAssembly = entryAssembly.Trim();
        SdkVersion = sdkVersion;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher.Trim();
    }

    public string Id { get; }

    public string Version { get; }

    /// <summary>Gets the file name of the assembly containing the plugin entry point.</summary>
    public string EntryAssembly { get; }

    public int SdkVersion { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    public string? Publisher { get; }

    public static JsonSerializerOptions JsonOptions { get; } = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
