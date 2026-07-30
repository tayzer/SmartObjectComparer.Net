namespace ParityBench.NET.Application.Secrets;

/// <summary>
/// Replaces <c>secret://</c> references in step configuration with their values,
/// immediately before a run is handed to the pipeline.
/// </summary>
/// <remarks>
/// Resolution happens as late as possible and the result is never written back to
/// disk: profiles, run snapshots and reports keep the reference, so a secret's
/// value only ever exists in the memory of the process executing the run.
/// </remarks>
public sealed class SecretResolver
{
    private readonly ISecretStore secretStore;

    public SecretResolver(ISecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(secretStore);
        this.secretStore = secretStore;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> ResolveAsync(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? stepConfiguration,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> resolved =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        if (stepConfiguration is null)
        {
            return resolved;
        }

        foreach ((string stepId, IReadOnlyDictionary<string, string> values) in stepConfiguration)
        {
            Dictionary<string, string> resolvedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, string value) in values)
            {
                resolvedValues[key] = await ResolveValueAsync(stepId, key, value, cancellationToken).ConfigureAwait(false);
            }

            resolved[stepId] = resolvedValues;
        }

        return resolved;
    }

    private async Task<string> ResolveValueAsync(
        string stepId,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        if (!SecretReference.TryParse(value, out SecretReference? reference))
        {
            return value;
        }

        return await secretStore.GetAsync(reference!, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Secret '{reference}' referenced by '{stepId}.{key}' could not be resolved. " +
                $"Store it in the app, or set {EnvironmentVariableSecretStore.ToVariableName(reference!)}.");
    }
}
