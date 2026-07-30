using System.Text.Json;

using ParityBench.PluginSdk.Plugin;

namespace ParityBench.NET.Plugins.Tests;

/// <summary>
/// Installs the built fixture plugin package into a throwaway plugins root, so
/// tests exercise real on-disk discovery and loading.
/// </summary>
public sealed class PluginTestPackage : IDisposable
{
    private static readonly string FixtureBuildDirectory = ResolveFixtureBuildDirectory();

    private PluginTestPackage(string rootPath) => RootPath = rootPath;

    public string RootPath { get; }

    public static PluginTestPackage CreateEmptyRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "paritybench-plugin-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        return new PluginTestPackage(root);
    }

    public static PluginTestPackage InstallFixture(string pluginId, string version, int sdkVersion = PluginManifest.CurrentSdkVersion)
    {
        PluginTestPackage package = CreateEmptyRoot();
        string packageDirectory = Path.Combine(package.RootPath, $"{pluginId}.{version}");
        Directory.CreateDirectory(packageDirectory);

        foreach (string file in Directory.EnumerateFiles(FixtureBuildDirectory))
        {
            File.Copy(file, Path.Combine(packageDirectory, Path.GetFileName(file)), overwrite: true);
        }

        PluginManifest manifest = new PluginManifest(pluginId, version, "ParityBench.TestPlugin.dll", sdkVersion, displayName: pluginId);
        File.WriteAllText(
            Path.Combine(packageDirectory, PluginManifest.FileName),
            JsonSerializer.Serialize(manifest, PluginManifest.JsonOptions));

        return package;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Loaded plugin assemblies stay locked by their load context.
        }
    }

    private static string ResolveFixtureBuildDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ComparisonTool.sln")))
        {
            directory = directory.Parent;
        }

        string repositoryRoot = directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
        string configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;
        return Path.Combine(repositoryRoot, "Tests", "Fixtures", "ParityBench.TestPlugin", "bin", configuration, "net10.0");
    }
}
