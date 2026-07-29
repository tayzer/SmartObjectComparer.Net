using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

/// <summary>
/// Stores run profiles as one JSON file per profile under
/// <c>&lt;workspace&gt;/config/profiles</c>, so they can be edited by hand, diffed
/// and checked into a client's own repository.
/// </summary>
public sealed class FileSystemRunProfileStore : IRunProfileStore
{
    private const int FileOperationRetryCount = 5;
    private static readonly TimeSpan FileOperationRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    private readonly string profilesDirectory;

    public FileSystemRunProfileStore(string workspaceRoot)
    {
        string normalizedWorkspaceRoot = FileSystemWorkspacePaths.NormalizeRoot(workspaceRoot);
        profilesDirectory = FileSystemWorkspacePaths.GetSafePath(
            normalizedWorkspaceRoot,
            FileSystemWorkspacePaths.ToLogicalPath("config", "profiles"));
    }

    public async Task<IReadOnlyList<RunProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(profilesDirectory))
            {
                return Array.Empty<RunProfile>();
            }

            List<RunProfile> profiles = new List<RunProfile>();
            foreach (string path in Directory.EnumerateFiles(profilesDirectory, "*.json"))
            {
                RunProfile? profile = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
                if (profile is not null)
                {
                    profiles.Add(profile);
                }
            }

            return profiles
                .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RunProfile?> GetAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = GetProfilePath(profileId);
            return File.Exists(path) ? await ReadAsync(path, cancellationToken).ConfigureAwait(false) : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(RunProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(profilesDirectory);
            string path = GetProfilePath(profile.Id);
            string tempPath = path + ".tmp";
            string json = JsonSerializer.Serialize(RunProfileDto.FromProfile(profile), JsonOptions);

            await ExecuteWithRetryAsync(
                async () =>
                {
                    // Written through a temp file so a crash mid-write cannot leave a
                    // half-serialized profile behind.
                    await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
                    if (File.Exists(path))
                    {
                        File.Replace(tempPath, path, destinationBackupFileName: null);
                    }
                    else
                    {
                        File.Move(tempPath, path);
                    }

                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = GetProfilePath(profileId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetProfilePath(string profileId) =>
        FileSystemWorkspacePaths.GetSafePath(
            profilesDirectory,
            FileSystemWorkspacePaths.ToLogicalPath($"{profileId}.json"));

    private static async Task<RunProfile?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<RunProfileDto>(json, JsonOptions)?.ToProfile();
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or InvalidOperationException)
        {
            // A profile a client hand-edited into an invalid state should not stop
            // the rest of their profiles from loading.
            return null;
        }
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (IOException) when (attempt < FileOperationRetryCount)
            {
                await Task.Delay(FileOperationRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < FileOperationRetryCount)
            {
                await Task.Delay(FileOperationRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class RunProfileDto
    {
        public int SchemaVersion { get; set; } = RunProfile.CurrentSchemaVersion;

        public string Id { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public PluginSelectionDto Plugin { get; set; } = new PluginSelectionDto();

        public string ComparisonId { get; set; } = string.Empty;

        public string? Environment { get; set; }

        public EndpointsDto Endpoints { get; set; } = new EndpointsDto();

        public Dictionary<string, string> EndpointAHeaders { get; set; } = new Dictionary<string, string>();

        public Dictionary<string, string> EndpointBHeaders { get; set; } = new Dictionary<string, string>();

        public List<string> EnabledSteps { get; set; } = new List<string>();

        public Dictionary<string, Dictionary<string, string>> StepConfiguration { get; set; } = new Dictionary<string, Dictionary<string, string>>();

        public ComparisonOptionsDto? Comparison { get; set; }

        public InputDto? Input { get; set; }

        public ReportDto? Report { get; set; }

        public static RunProfileDto FromProfile(RunProfile profile) => new RunProfileDto
        {
            SchemaVersion = profile.SchemaVersion,
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            Plugin = new PluginSelectionDto { Id = profile.PluginId, Version = profile.PluginVersion },
            ComparisonId = profile.ComparisonId,
            Environment = profile.EnvironmentName,
            Endpoints = new EndpointsDto { A = profile.EndpointA.ToString(), B = profile.EndpointB.ToString() },
            EndpointAHeaders = new Dictionary<string, string>(profile.EndpointAHeaders, StringComparer.OrdinalIgnoreCase),
            EndpointBHeaders = new Dictionary<string, string>(profile.EndpointBHeaders, StringComparer.OrdinalIgnoreCase),
            EnabledSteps = profile.EnabledStepIds.ToList(),
            StepConfiguration = profile.StepConfiguration.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ToDictionary(value => value.Key, value => value.Value)),
            Comparison = ComparisonOptionsDto.FromOptions(profile.Comparison),
            Input = new InputDto { RequestDirectory = profile.RequestDirectory },
            Report = new ReportDto
            {
                ChunkSize = profile.LargeRun.ChunkSize,
                DetailPageSize = profile.LargeRun.DetailPageSize,
                RetentionMode = profile.RetentionModeOverride,
            },
        };

        public RunProfile ToProfile() => new RunProfile(
            Id,
            DisplayName,
            Plugin.Id,
            ComparisonId,
            new Uri(Endpoints.A, UriKind.Absolute),
            new Uri(Endpoints.B, UriKind.Absolute),
            Plugin.Version,
            Environment,
            EnabledSteps,
            StepConfiguration.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyDictionary<string, string>)entry.Value,
                StringComparer.OrdinalIgnoreCase),
            Comparison?.ToOptions(),
            Input?.RequestDirectory,
            SchemaVersion,
            Report is null ? null : new LargeRunOptions(Report.ChunkSize, Report.DetailPageSize),
            Report?.RetentionMode,
            EndpointAHeaders,
            EndpointBHeaders);
    }

    private sealed class PluginSelectionDto
    {
        public string Id { get; set; } = string.Empty;

        public string? Version { get; set; }
    }

    private sealed class EndpointsDto
    {
        public string A { get; set; } = string.Empty;

        public string B { get; set; } = string.Empty;
    }

    private sealed class InputDto
    {
        public string? RequestDirectory { get; set; }
    }

    private sealed class ReportDto
    {
        public int ChunkSize { get; set; } = new LargeRunOptions().ChunkSize;

        public int DetailPageSize { get; set; } = new LargeRunOptions().DetailPageSize;

        public RetentionMode? RetentionMode { get; set; }
    }

    private sealed class ComparisonOptionsDto
    {
        public bool IgnoreCollectionOrder { get; set; }

        public bool IgnoreStringCase { get; set; }

        public bool IgnoreTrailingWhitespaceAtEnd { get; set; }

        public bool TreatNullAndEmptyCollectionsAsEqual { get; set; }

        public bool IgnoreXmlNamespaces { get; set; } = true;

        public int MaxDifferences { get; set; } = 100;

        public List<IgnoreRuleDefinition> IgnoreRules { get; set; } = new List<IgnoreRuleDefinition>();

        public List<SmartIgnoreRuleDefinition> SmartIgnoreRules { get; set; } = new List<SmartIgnoreRuleDefinition>();

        public List<MaskRuleDefinition> MaskRules { get; set; } = new List<MaskRuleDefinition>();

        public static ComparisonOptionsDto FromOptions(ComparisonOptions options) => new ComparisonOptionsDto
        {
            IgnoreCollectionOrder = options.IgnoreCollectionOrder,
            IgnoreStringCase = options.IgnoreStringCase,
            IgnoreTrailingWhitespaceAtEnd = options.IgnoreTrailingWhitespaceAtEnd,
            TreatNullAndEmptyCollectionsAsEqual = options.TreatNullAndEmptyCollectionsAsEqual,
            IgnoreXmlNamespaces = options.IgnoreXmlNamespaces,
            MaxDifferences = options.MaxDifferences,
            IgnoreRules = options.IgnoreRules.ToList(),
            SmartIgnoreRules = options.SmartIgnoreRules.ToList(),
            MaskRules = options.MaskRules.ToList(),
        };

        public ComparisonOptions ToOptions() => new ComparisonOptions(
            IgnoreCollectionOrder,
            IgnoreStringCase,
            IgnoreTrailingWhitespaceAtEnd,
            TreatNullAndEmptyCollectionsAsEqual,
            IgnoreXmlNamespaces,
            MaxDifferences,
            IgnoreRules,
            SmartIgnoreRules,
            MaskRules);
    }
}
