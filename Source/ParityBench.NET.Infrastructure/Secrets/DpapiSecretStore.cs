using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ParityBench.NET.Application.Secrets;

namespace ParityBench.NET.Infrastructure.Secrets;

/// <summary>
/// Persists secrets in the workspace, encrypted with Windows DPAPI under the
/// current user.
/// </summary>
/// <remarks>
/// Only the user who saved a secret can read it back, and only on that machine —
/// copying the workspace elsewhere yields an unreadable file rather than exposed
/// credentials. On non-Windows hosts this store reports itself unavailable and the
/// chain falls through to environment variables.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ParityBench.SecretStore.v1");

    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    private readonly string storePath;

    public DpapiSecretStore(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root must not be empty.", nameof(workspaceRoot));
        }

        storePath = Path.Combine(workspaceRoot, "config", "secrets.dat");
    }

    public bool CanWrite => OperatingSystem.IsWindows();

    public async Task<string?> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, string> secrets = Load();
            return secrets.TryGetValue(reference.ToString(), out string? value) ? value : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetAsync(SecretReference reference, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!OperatingSystem.IsWindows())
        {
            throw new NotSupportedException("DPAPI-protected secrets are only available on Windows.");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, string> secrets = Load();
            secrets[reference.ToString()] = value;
            Persist(secrets);
        }
        finally
        {
            gate.Release();
        }
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(storePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            byte[] plaintext = ProtectedData.Unprotect(
                File.ReadAllBytes(storePath),
                Entropy,
                DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // Written by another user, or on another machine. Treat it as empty
            // rather than failing every run that touches a secret.
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Persist(Dictionary<string, string> secrets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);

        byte[] ciphertext = ProtectedData.Protect(
            JsonSerializer.SerializeToUtf8Bytes(secrets),
            Entropy,
            DataProtectionScope.CurrentUser);

        string tempPath = storePath + ".tmp";
        File.WriteAllBytes(tempPath, ciphertext);
        if (File.Exists(storePath))
        {
            File.Replace(tempPath, storePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, storePath);
        }
    }
}
