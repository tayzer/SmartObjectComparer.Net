using ParityBench.NET.Application.Secrets;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Profiles;

/// <summary>
/// A profile turned into the pieces a run needs, with secret references resolved.
/// </summary>
public sealed record ResolvedRunProfile(
    RunProfile Profile,
    PluginComparisonSelection Selection,
    EndpointDefinition EndpointA,
    EndpointDefinition EndpointB);

/// <summary>
/// Prepares a saved profile for execution.
/// </summary>
/// <remarks>
/// This is the only place secret references become values, and the result is
/// handed straight to the run — it is never written back to the profile.
/// </remarks>
public sealed class RunProfileResolver
{
    private readonly IRunProfileStore profileStore;
    private readonly SecretResolver secretResolver;

    public RunProfileResolver(IRunProfileStore profileStore, SecretResolver secretResolver)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(secretResolver);

        this.profileStore = profileStore;
        this.secretResolver = secretResolver;
    }

    public async Task<ResolvedRunProfile> ResolveAsync(string profileId, CancellationToken cancellationToken = default)
    {
        RunProfile profile = await profileStore.GetAsync(profileId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Run profile '{profileId}' was not found.");

        return await ResolveAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ResolvedRunProfile> ResolveAsync(RunProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resolvedConfiguration =
            await secretResolver.ResolveAsync(profile.StepConfiguration, cancellationToken).ConfigureAwait(false);

        PluginComparisonSelection selection = new PluginComparisonSelection(
            profile.PluginId,
            profile.ComparisonId,
            profile.PluginVersion,
            profile.EnabledStepIds,
            resolvedConfiguration,
            profile.EnvironmentName);

        return new ResolvedRunProfile(
            profile,
            selection,
            new EndpointDefinition(profile.EndpointA, $"{profile.DisplayName} (A)", profile.EndpointAHeaders),
            new EndpointDefinition(profile.EndpointB, $"{profile.DisplayName} (B)", profile.EndpointBHeaders));
    }
}
