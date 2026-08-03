using System.Text.Json;

using ParityBench.PluginSdk.Plugin;

namespace ParityBench.NET.Plugins.Tests;

/// <summary>Which build of the fixture plugin to install.</summary>
public enum FixtureVariant
{
    /// <summary>The original build, whose comparison is named "Test comparison".</summary>
    Original,

    /// <summary>A second build of the same source, named "Test comparison (rebuilt)".</summary>
    Rebuilt,
}

/// <summary>
/// Installs the built fixture plugin package into a throwaway plugins root, so
/// tests exercise real on-disk discovery and loading.
/// </summary>
/// <remarks>
/// Installing is an instance operation so one root can hold several packages — two
/// versions side by side, or the same package overwritten to stand in for a client
/// rebuilding their plugin.
/// </remarks>
public sealed class PluginTestPackage : IDisposable
{
    private static readonly string OriginalBuildDirectory = ResolveFixtureBuildDirectory("ParityBench.TestPlugin");
    private static readonly string RebuiltBuildDirectory = ResolveFixtureBuildDirectory("ParityBench.TestPlugin.Rebuilt");

    private PluginTestPackage(string rootPath, string shadowRootPath)
    {
        RootPath = rootPath;
        ShadowRootPath = shadowRootPath;
    }

    /// <summary>Gets the plugins root to hand to a <see cref="PluginCatalog"/>.</summary>
    public string RootPath { get; }

    /// <summary>Gets a shadow-copy root to hand to a <see cref="PluginLoader"/>, kept out of the plugins root.</summary>
    public string ShadowRootPath { get; }

    public static PluginTestPackage CreateEmptyRoot()
    {
        string baseDirectory = Path.Combine(Path.GetTempPath(), "paritybench-plugin-tests", Guid.NewGuid().ToString("n"));

        // The shadow root sits beside the plugins root, never inside it, or the copies
        // would be discovered as packages on the next scan.
        string root = Path.Combine(baseDirectory, "plugins");
        Directory.CreateDirectory(root);
        return new PluginTestPackage(root, Path.Combine(baseDirectory, "shadow"));
    }

    /// <summary>Creates a root with one package already installed.</summary>
    public static PluginTestPackage WithFixture(string pluginId, string version, int sdkVersion = PluginManifest.CurrentSdkVersion)
    {
        PluginTestPackage package = CreateEmptyRoot();
        package.InstallFixture(pluginId, version, sdkVersion);
        return package;
    }

    /// <summary>
    /// Copies the fixture package into the root under a given id and version, so two
    /// "different" plugins can be installed from one built fixture. Returns the package
    /// directory.
    /// </summary>
    public string InstallFixture(
        string pluginId,
        string version,
        int sdkVersion = PluginManifest.CurrentSdkVersion,
        FixtureVariant variant = FixtureVariant.Original)
    {
        string packageDirectory = PackageDirectory(pluginId, version);
        Directory.CreateDirectory(packageDirectory);

        string buildDirectory = variant == FixtureVariant.Rebuilt ? RebuiltBuildDirectory : OriginalBuildDirectory;
        foreach (string file in Directory.EnumerateFiles(buildDirectory))
        {
            File.Copy(file, Path.Combine(packageDirectory, Path.GetFileName(file)), overwrite: true);
        }

        WriteManifest(packageDirectory, pluginId, version, sdkVersion);
        return packageDirectory;
    }

    /// <summary>
    /// Overwrites an installed package in place with another build, leaving its id and
    /// version alone — what a client does when they rebuild a plugin without bumping it.
    /// </summary>
    public void ReinstallFixture(string pluginId, string version, FixtureVariant variant) =>
        InstallFixture(pluginId, version, PluginManifest.CurrentSdkVersion, variant);

    /// <summary>Rewrites just the manifest, for the case of a client correcting a rejected package.</summary>
    public void RewriteManifest(string pluginId, string version, int sdkVersion) =>
        WriteManifest(PackageDirectory(pluginId, version), pluginId, version, sdkVersion);

    /// <summary>Deletes an installed package. Only possible while it is loaded because the loader shadow-copies.</summary>
    public void UninstallPackage(string pluginId, string version) =>
        Directory.Delete(PackageDirectory(pluginId, version), recursive: true);

    public string EntryAssemblyPath(string pluginId, string version) =>
        Path.Combine(PackageDirectory(pluginId, version), "ParityBench.TestPlugin.dll");

    public void Dispose()
    {
        // Best effort: a loader that was not disposed may still hold a shadow copy
        // mapped, and a leftover temp directory is not worth failing a test over.
        Delete(Path.GetDirectoryName(RootPath)!);
    }

    private string PackageDirectory(string pluginId, string version) => Path.Combine(RootPath, $"{pluginId}.{version}");

    private static void WriteManifest(string packageDirectory, string pluginId, string version, int sdkVersion)
    {
        PluginManifest manifest = new PluginManifest(pluginId, version, "ParityBench.TestPlugin.dll", sdkVersion, displayName: pluginId);
        File.WriteAllText(
            Path.Combine(packageDirectory, PluginManifest.FileName),
            JsonSerializer.Serialize(manifest, PluginManifest.JsonOptions));
    }

    private static void Delete(string directory)
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

    private static string ResolveFixtureBuildDirectory(string fixtureProjectName)
    {
        // The fixture packages are built alongside these tests; walk up to the repo
        // root rather than hard-coding a relative depth from the test binary.
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ComparisonTool.sln")))
        {
            directory = directory.Parent;
        }

        string repositoryRoot = directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
        string configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;
        return Path.Combine(repositoryRoot, "Tests", "Fixtures", fixtureProjectName, "bin", configuration, "net10.0");
    }
}
