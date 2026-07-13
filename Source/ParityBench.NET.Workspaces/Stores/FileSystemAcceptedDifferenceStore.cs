using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Application.AcceptedDifferences;
using ParityBench.NET.Domain.AcceptedDifferences;
using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Workspaces;

public sealed class FileSystemAcceptedDifferenceStore : IAcceptedDifferenceUseCases
{
    private const int LockRetryDelayMilliseconds = 100;
    private static readonly TimeSpan MaxLockWaitTime = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    private readonly string storePath;
    private Dictionary<string, AcceptedDifferenceProfile> profilesByFingerprint = new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
    private DateTime? lastStoreWriteTimeUtc;

    public FileSystemAcceptedDifferenceStore(string workspaceRoot, string? configuredStorePath = null)
    {
        string normalizedWorkspaceRoot = FileSystemWorkspacePaths.NormalizeRoot(workspaceRoot);
        storePath = string.IsNullOrWhiteSpace(configuredStorePath)
            ? FileSystemWorkspacePaths.GetSafePath(normalizedWorkspaceRoot, FileSystemWorkspacePaths.ToLogicalPath("config", "accepted-differences.json"))
            : Path.GetFullPath(configuredStorePath);
    }

    public bool IsReadOnly => false;

    public AcceptedDifferenceFingerprint CreateFingerprint(ComparisonDifference difference) =>
        AcceptedDifferenceFingerprintBuilder.Create(difference);

    public async Task<IReadOnlyList<AcceptedDifferenceProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            await LoadProfilesIfChangedAsync(cancellationToken).ConfigureAwait(false);
            return profilesByFingerprint.Values
                .OrderByDescending(profile => profile.UpdatedAt)
                .ThenBy(profile => profile.NormalizedPropertyPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, AcceptedDifferenceProfile>> MatchAsync(
        IEnumerable<ComparisonDifference> differences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(differences);
        List<ComparisonDifference> differenceList = differences.ToList();
        if (differenceList.Count == 0)
        {
            return new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            await LoadProfilesIfChangedAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<string, AcceptedDifferenceProfile> matches = new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
            foreach (ComparisonDifference difference in differenceList)
            {
                string fingerprint = CreateFingerprint(difference).Fingerprint;
                if (profilesByFingerprint.TryGetValue(fingerprint, out AcceptedDifferenceProfile? profile))
                {
                    matches[fingerprint] = profile;
                }
            }

            return matches;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AcceptedDifferenceProfile> SaveAsync(
        ComparisonDifference difference,
        AcceptedDifferenceStatus status,
        string? notes = null,
        string? ticketId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(difference);
        if (status == AcceptedDifferenceStatus.KnownBug && string.IsNullOrWhiteSpace(ticketId))
        {
            throw new ArgumentException("A ticket ID is required when tracking a known bug.", nameof(ticketId));
        }

        AcceptedDifferenceFingerprint fingerprint = CreateFingerprint(difference);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            await LoadProfilesIfChangedAsync(cancellationToken, forceReload: true).ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            profilesByFingerprint.TryGetValue(fingerprint.Fingerprint, out AcceptedDifferenceProfile? existingProfile);
            AcceptedDifferenceProfile profile = new AcceptedDifferenceProfile
            {
                Id = existingProfile?.Id ?? Guid.NewGuid(),
                Fingerprint = fingerprint.Fingerprint,
                NormalizedPropertyPath = fingerprint.NormalizedPropertyPath,
                Category = fingerprint.Category,
                ValueAPattern = fingerprint.ValueAPattern,
                ValueBPattern = fingerprint.ValueBPattern,
                SamplePropertyPath = difference.PropertyPath,
                SampleValueA = FormatSampleValue(difference.ValueA),
                SampleValueB = FormatSampleValue(difference.ValueB),
                Status = status,
                TicketId = Normalize(ticketId),
                Notes = Normalize(notes),
                CreatedAt = existingProfile?.CreatedAt ?? now,
                UpdatedAt = now,
            };
            profilesByFingerprint[profile.Fingerprint] = profile;
            await PersistProfilesAsync(cancellationToken).ConfigureAwait(false);
            return profile;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> ImportAsync(
        IEnumerable<AcceptedDifferenceProfile> profiles,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
    {
        List<AcceptedDifferenceProfile> importedProfiles = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Fingerprint))
            .Select(SanitizeProfile)
            .ToList();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            await LoadProfilesIfChangedAsync(cancellationToken, forceReload: true).ConfigureAwait(false);
            if (replaceExisting)
            {
                profilesByFingerprint.Clear();
            }

            foreach (AcceptedDifferenceProfile profile in importedProfiles)
            {
                profilesByFingerprint[profile.Fingerprint] = profile;
            }

            await PersistProfilesAsync(cancellationToken).ConfigureAwait(false);
            return importedProfiles.Select(profile => profile.Fingerprint).Distinct(StringComparer.Ordinal).Count();
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<bool> RemoveAsync(ComparisonDifference difference, CancellationToken cancellationToken = default) =>
        RemoveByFingerprintAsync(CreateFingerprint(difference).Fingerprint, cancellationToken);

    public async Task<bool> RemoveByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            await LoadProfilesIfChangedAsync(cancellationToken, forceReload: true).ConfigureAwait(false);
            bool removed = profilesByFingerprint.Remove(fingerprint);
            if (removed)
            {
                await PersistProfilesAsync(cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            profilesByFingerprint.Clear();
            await PersistProfilesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task LoadProfilesIfChangedAsync(CancellationToken cancellationToken, bool forceReload = false)
    {
        if (!File.Exists(storePath))
        {
            profilesByFingerprint = new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
            lastStoreWriteTimeUtc = null;
            return;
        }

        DateTime currentWriteTimeUtc = File.GetLastWriteTimeUtc(storePath);
        if (!forceReload && lastStoreWriteTimeUtc.HasValue && currentWriteTimeUtc == lastStoreWriteTimeUtc.Value)
        {
            return;
        }

        try
        {
            await using FileStream stream = new FileStream(storePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
            AcceptedDifferenceProfileStore? store = await JsonSerializer.DeserializeAsync<AcceptedDifferenceProfileStore>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            profilesByFingerprint = (store?.Profiles ?? new List<AcceptedDifferenceProfile>())
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Fingerprint))
                .GroupBy(profile => profile.Fingerprint, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            lastStoreWriteTimeUtc = currentWriteTimeUtc;
        }
        catch (JsonException)
        {
            profilesByFingerprint = new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
            lastStoreWriteTimeUtc = currentWriteTimeUtc;
        }
    }

    private async Task PersistProfilesAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath) ?? ".");
        AcceptedDifferenceProfileStore store = new AcceptedDifferenceProfileStore
        {
            Profiles = profilesByFingerprint.Values
                .OrderBy(profile => profile.NormalizedPropertyPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.Fingerprint, StringComparer.Ordinal)
                .ToList(),
        };
        string tempFilePath = storePath + ".tmp";
        await File.WriteAllTextAsync(tempFilePath, JsonSerializer.Serialize(store, JsonOptions), cancellationToken).ConfigureAwait(false);
        if (File.Exists(storePath))
        {
            File.Replace(tempFilePath, storePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempFilePath, storePath);
        }

        lastStoreWriteTimeUtc = File.GetLastWriteTimeUtc(storePath);
    }

    private async Task<FileStream> AcquireStoreLockAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath) ?? ".");
        string lockFilePath = storePath + ".lock";
        DateTime startedAt = DateTime.UtcNow;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow - startedAt < MaxLockWaitTime)
            {
                await Task.Delay(LockRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static AcceptedDifferenceProfile SanitizeProfile(AcceptedDifferenceProfile profile)
    {
        if (profile.Status == AcceptedDifferenceStatus.KnownBug && string.IsNullOrWhiteSpace(profile.TicketId))
        {
            throw new ArgumentException("Known bug accepted-difference profiles require a ticket ID.", nameof(profile));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        return profile with
        {
            Id = profile.Id == Guid.Empty ? Guid.NewGuid() : profile.Id,
            Fingerprint = profile.Fingerprint.Trim(),
            NormalizedPropertyPath = profile.NormalizedPropertyPath ?? string.Empty,
            Category = profile.Category ?? string.Empty,
            ValueAPattern = profile.ValueAPattern ?? string.Empty,
            ValueBPattern = profile.ValueBPattern ?? string.Empty,
            SamplePropertyPath = profile.SamplePropertyPath ?? string.Empty,
            SampleValueA = profile.SampleValueA ?? string.Empty,
            SampleValueB = profile.SampleValueB ?? string.Empty,
            TicketId = Normalize(profile.TicketId),
            Notes = Normalize(profile.Notes),
            CreatedAt = profile.CreatedAt == default ? now : profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt == default ? now : profile.UpdatedAt,
        };
    }

    private static string FormatSampleValue(string? value) => (value ?? "null").Length > 256 ? (value ?? "null")[..256] : value ?? "null";

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
