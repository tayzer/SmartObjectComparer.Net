namespace ParityBench.NET.Application.Secrets;

/// <summary>
/// A <c>secret://scope/name</c> reference. Saved profiles carry these; they never
/// carry the value behind them.
/// </summary>
public sealed record SecretReference
{
    public const string UriScheme = "secret";

    public SecretReference(string scope, string name)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("Secret scope must not be empty.", nameof(scope));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Secret name must not be empty.", nameof(name));
        }

        Scope = scope.Trim();
        Name = name.Trim();
    }

    public string Scope { get; }

    public string Name { get; }

    public override string ToString() => $"{UriScheme}://{Scope}/{Name}";

    /// <summary>
    /// Parses a reference. Anything that is not a <c>secret://</c> reference is a
    /// literal configuration value and is left alone.
    /// </summary>
    public static bool TryParse(string? value, out SecretReference? reference)
    {
        reference = null;

        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith($"{UriScheme}://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = value[(UriScheme.Length + 3)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        reference = new SecretReference(segments[0], segments[1]);
        return true;
    }
}

/// <summary>
/// Resolves and stores secret values referenced by run profiles.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Gets the value behind a reference, or null when this store does not hold it.
    /// </summary>
    Task<string?> GetAsync(SecretReference reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a value. Read-only stores throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task SetAsync(SecretReference reference, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a value indicating whether this store accepts writes.
    /// </summary>
    bool CanWrite { get; }
}
