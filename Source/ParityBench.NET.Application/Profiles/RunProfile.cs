using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Application.Profiles;

/// <summary>
/// A saved run configuration: which plugin comparison to run, against which
/// endpoints, with which steps, rules and inputs.
/// </summary>
/// <remarks>
/// A profile only ever references stable logical ids — plugin id, comparison id,
/// step ids, secret references — never assembly-qualified type names, so it keeps
/// working when a plugin is rebuilt or upgraded.
/// </remarks>
public sealed record RunProfile
{
    public const int CurrentSchemaVersion = 1;

    public RunProfile(
        string id,
        string displayName,
        string pluginId,
        string comparisonId,
        Uri endpointA,
        Uri endpointB,
        string? pluginVersion = null,
        string? environmentName = null,
        IEnumerable<string>? enabledStepIds = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? stepConfiguration = null,
        ComparisonOptions? comparison = null,
        string? requestDirectory = null,
        int schemaVersion = CurrentSchemaVersion,
        LargeRunOptions? largeRun = null,
        RetentionMode? retentionModeOverride = null,
        IReadOnlyDictionary<string, string>? endpointAHeaders = null,
        IReadOnlyDictionary<string, string>? endpointBHeaders = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Profile id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new ArgumentException("Profile plugin id must not be empty.", nameof(pluginId));
        }

        if (string.IsNullOrWhiteSpace(comparisonId))
        {
            throw new ArgumentException("Profile comparison id must not be empty.", nameof(comparisonId));
        }

        ArgumentNullException.ThrowIfNull(endpointA);
        ArgumentNullException.ThrowIfNull(endpointB);

        Id = id.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
        PluginId = pluginId.Trim();
        ComparisonId = comparisonId.Trim();
        EndpointA = endpointA;
        EndpointB = endpointB;
        PluginVersion = string.IsNullOrWhiteSpace(pluginVersion) ? null : pluginVersion.Trim();
        EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? null : environmentName.Trim();
        EnabledStepIds = (enabledStepIds ?? Array.Empty<string>()).ToArray();
        StepConfiguration = stepConfiguration ?? new Dictionary<string, IReadOnlyDictionary<string, string>>();
        Comparison = comparison ?? new ComparisonOptions();
        RequestDirectory = string.IsNullOrWhiteSpace(requestDirectory) ? null : requestDirectory;
        SchemaVersion = schemaVersion <= 0 ? CurrentSchemaVersion : schemaVersion;
        LargeRun = largeRun ?? new LargeRunOptions();
        RetentionModeOverride = retentionModeOverride;
        EndpointAHeaders = new Dictionary<string, string>(endpointAHeaders ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        EndpointBHeaders = new Dictionary<string, string>(endpointBHeaders ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
    }

    public int SchemaVersion { get; }

    public string Id { get; }

    public string DisplayName { get; }

    public string PluginId { get; }

    public string? PluginVersion { get; }

    public string ComparisonId { get; }

    public string? EnvironmentName { get; }

    public Uri EndpointA { get; }

    public Uri EndpointB { get; }

    public IReadOnlyList<string> EnabledStepIds { get; }

    /// <summary>
    /// Gets per-step configuration. Values may be <c>secret://</c> references,
    /// which are resolved only when a run is started.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> StepConfiguration { get; }

    public ComparisonOptions Comparison { get; }

    public string? RequestDirectory { get; }

    public LargeRunOptions LargeRun { get; }

    public RetentionMode? RetentionModeOverride { get; }

    public IReadOnlyDictionary<string, string> EndpointAHeaders { get; }

    public IReadOnlyDictionary<string, string> EndpointBHeaders { get; }

    /// <summary>
    /// Builds the run selection this profile describes. Secret references are
    /// passed through unchanged; the caller resolves them.
    /// </summary>
    public PluginComparisonSelection ToSelection() =>
        new PluginComparisonSelection(
            PluginId,
            ComparisonId,
            PluginVersion,
            EnabledStepIds,
            StepConfiguration,
            EnvironmentName);
}

/// <summary>
/// Stores saved run profiles.
/// </summary>
public interface IRunProfileStore
{
    Task<IReadOnlyList<RunProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<RunProfile?> GetAsync(string profileId, CancellationToken cancellationToken = default);

    Task SaveAsync(RunProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(string profileId, CancellationToken cancellationToken = default);
}
