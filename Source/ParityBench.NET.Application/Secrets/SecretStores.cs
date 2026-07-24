using System.Collections.Concurrent;

namespace ParityBench.NET.Application.Secrets;

/// <summary>
/// Reads secrets from environment variables (<c>PB_SECRET_&lt;SCOPE&gt;_&lt;NAME&gt;</c>).
/// This is the path tests and CI use, so neither needs an OS credential vault or
/// any interactive setup.
/// </summary>
public sealed class EnvironmentVariableSecretStore : ISecretStore
{
    public const string Prefix = "PB_SECRET_";

    public bool CanWrite => false;

    public Task<string?> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        string? value = Environment.GetEnvironmentVariable(ToVariableName(reference));
        return Task.FromResult(string.IsNullOrEmpty(value) ? null : value);
    }

    public Task SetAsync(SecretReference reference, string value, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Environment-variable secrets are read-only; set the variable in the environment instead.");

    public static string ToVariableName(SecretReference reference) =>
        $"{Prefix}{Normalize(reference.Scope)}_{Normalize(reference.Name)}";

    private static string Normalize(string value) =>
        value.Replace('-', '_').Replace('.', '_').ToUpperInvariant();
}

/// <summary>
/// Holds secrets for the lifetime of the process only. Used by unit tests and by
/// manual runs against the local fixture endpoints, where persisting a throwaway
/// token would be worse than losing it.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> secrets = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool CanWrite => true;

    public Task<string?> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return Task.FromResult(secrets.TryGetValue(reference.ToString(), out string? value) ? value : null);
    }

    public Task SetAsync(SecretReference reference, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        secrets[reference.ToString()] = value;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Tries each store in order and returns the first hit; writes go to the first
/// store that accepts them.
/// </summary>
/// <remarks>
/// Order is the policy: an environment variable overrides the persisted store, so
/// a developer or a CI job can run any profile without touching what an operator
/// saved on their machine.
/// </remarks>
public sealed class ChainedSecretStore : ISecretStore
{
    private readonly IReadOnlyList<ISecretStore> stores;

    public ChainedSecretStore(params ISecretStore[] stores)
        : this((IReadOnlyList<ISecretStore>)stores)
    {
    }

    public ChainedSecretStore(IReadOnlyList<ISecretStore> stores)
    {
        ArgumentNullException.ThrowIfNull(stores);

        if (stores.Count == 0)
        {
            throw new ArgumentException("At least one secret store is required.", nameof(stores));
        }

        this.stores = stores;
    }

    public bool CanWrite => stores.Any(store => store.CanWrite);

    public async Task<string?> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        foreach (ISecretStore store in stores)
        {
            string? value = await store.GetAsync(reference, cancellationToken).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    public Task SetAsync(SecretReference reference, string value, CancellationToken cancellationToken = default)
    {
        ISecretStore? writableStore = stores.FirstOrDefault(store => store.CanWrite);
        return writableStore is null
            ? throw new NotSupportedException("No writable secret store is configured.")
            : writableStore.SetAsync(reference, value, cancellationToken);
    }
}
