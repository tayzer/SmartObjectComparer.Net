using System.Text.Json;
using System.Text.Json.Serialization;
using ComparisonTool.Core.Abstractions;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ComparisonTool.Core.AcceptedDifferences;

/// <summary>
/// JSON-backed repository and matcher for persisted difference profiles.
/// </summary>
public sealed class AcceptedDifferenceService : IAcceptedDifferenceService
{
    private const int LockRetryDelayMilliseconds = 100;
    private static readonly TimeSpan MaxLockWaitTime = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ILogger<AcceptedDifferenceService> logger;
    private readonly AcceptedDifferenceFingerprintBuilder fingerprintBuilder;
    private readonly string storePath;

    private Dictionary<string, AcceptedDifferenceProfile> profilesByFingerprint = new(StringComparer.Ordinal);
    private DateTime? lastStoreWriteTimeUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcceptedDifferenceService"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="fingerprintBuilder">Fingerprint builder.</param>
    /// <param name="options">Configured options.</param>
    public AcceptedDifferenceService(
        ILogger<AcceptedDifferenceService> logger,
        AcceptedDifferenceFingerprintBuilder fingerprintBuilder,
        IOptions<AcceptedDifferencesOptions> options)
    {
        this.logger = logger;
        this.fingerprintBuilder = fingerprintBuilder;

        var configuredOptions = options.Value ?? new AcceptedDifferencesOptions();
        IsEnabled = configuredOptions.Enabled;
        storePath = ResolveStorePath(configuredOptions.StorePath);
    }

    /// <inheritdoc/>
    public bool IsEnabled { get; }

    /// <inheritdoc/>
    public AcceptedDifferenceFingerprint CreateFingerprint(Difference difference) => fingerprintBuilder.Create(difference);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AcceptedDifferenceProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Array.Empty<AcceptedDifferenceProfile>();
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            await LoadProfilesIfChangedAsync(cancellationToken).ConfigureAwait(false);

            return profilesByFingerprint.Values
                .OrderByDescending(profile => profile.UpdatedUtc)
                .ThenBy(profile => profile.NormalizedPropertyPath, StringComparer.Ordinal)
                .Select(CloneProfile)
                .ToList();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, AcceptedDifferenceProfile>> GetMatchesAsync(
        IEnumerable<Difference> differences,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
        }

        ArgumentNullException.ThrowIfNull(differences);

        var differenceList = differences.Where(difference => difference != null).ToList();
        if (differenceList.Count == 0)
        {
            return new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            await LoadProfilesIfChangedAsync(cancellationToken).ConfigureAwait(false);

            var matches = new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
            foreach (var difference in differenceList)
            {
                var fingerprint = fingerprintBuilder.Create(difference).Fingerprint;
                if (profilesByFingerprint.TryGetValue(fingerprint, out var profile))
                {
                    matches[fingerprint] = CloneProfile(profile);
                }
            }

            return matches;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<AcceptedDifferenceProfile> SaveAsync(
        Difference difference,
        AcceptedDifferenceStatus status,
        string? notes = null,
        string? ticketId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Accepted-difference tracking is disabled.");
        }

        ArgumentNullException.ThrowIfNull(difference);
        if (status == AcceptedDifferenceStatus.KnownBug && string.IsNullOrWhiteSpace(ticketId))
        {
            throw new ArgumentException("A ticket ID is required when tracking a known bug.", nameof(ticketId));
        }

        var fingerprint = fingerprintBuilder.Create(difference);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            await LoadProfilesIfChangedAsync(cancellationToken, forceReload: true).ConfigureAwait(false);

            var now = DateTime.UtcNow;
            if (!profilesByFingerprint.TryGetValue(fingerprint.Fingerprint, out var profile))
            {
                profile = new AcceptedDifferenceProfile
                {
                    Id = Guid.NewGuid(),
                    Fingerprint = fingerprint.Fingerprint,
                    CreatedUtc = now,
                };
                profilesByFingerprint[fingerprint.Fingerprint] = profile;
            }

            profile.NormalizedPropertyPath = fingerprint.NormalizedPropertyPath;
            profile.Category = fingerprint.Category;
            profile.ExpectedValuePattern = fingerprint.ExpectedValuePattern;
            profile.ActualValuePattern = fingerprint.ActualValuePattern;
            profile.SamplePropertyPath = difference.PropertyName ?? string.Empty;
            profile.SampleExpectedValue = FormatSampleValue(difference.Object1Value);
            profile.SampleActualValue = FormatSampleValue(difference.Object2Value);
            profile.Status = status;
            profile.TicketId = Normalize(ticketId);
            profile.Notes = Normalize(notes);
            profile.UpdatedUtc = now;

            await PersistProfilesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Saved accepted-difference profile {Fingerprint} with status {Status}",
                profile.Fingerprint,
                profile.Status);

            return CloneProfile(profile);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<int> ImportAsync(
        IEnumerable<AcceptedDifferenceProfile> profiles,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Accepted-difference tracking is disabled.");
        }

        ArgumentNullException.ThrowIfNull(profiles);

        var importedProfiles = profiles
            .Where(profile => profile != null && !string.IsNullOrWhiteSpace(profile.Fingerprint))
            .Select(CloneProfile)
            .ToList();

        if (importedProfiles.Count == 0)
        {
            return 0;
        }

        if (importedProfiles.Any(profile =>
                profile.Status == AcceptedDifferenceStatus.KnownBug &&
                string.IsNullOrWhiteSpace(Normalize(profile.TicketId))))
        {
            throw new ArgumentException("Known Bug profiles require a ticket ID.", nameof(profiles));
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            await LoadProfilesIfChangedAsync(cancellationToken, forceReload: true).ConfigureAwait(false);

            if (replaceExisting)
            {
                profilesByFingerprint.Clear();
            }

            foreach (var profile in importedProfiles)
            {
                profilesByFingerprint[profile.Fingerprint] = SanitizeImportedProfile(
                    profile,
                    profilesByFingerprint.GetValueOrDefault(profile.Fingerprint));
            }

            await PersistProfilesAsync(cancellationToken).ConfigureAwait(false);
            return importedProfiles.Select(profile => profile.Fingerprint).Distinct(StringComparer.Ordinal).Count();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(Difference difference, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(difference);
        var fingerprint = fingerprintBuilder.Create(difference).Fingerprint;
        return await RemoveByFingerprintAsync(fingerprint, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            await LoadProfilesIfChangedAsync(cancellationToken, forceReload: true).ConfigureAwait(false);
            if (!profilesByFingerprint.Remove(fingerprint))
            {
                return false;
            }

            await PersistProfilesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Removed accepted-difference profile {Fingerprint}", fingerprint);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var storeLock = await AcquireStoreLockAsync(cancellationToken).ConfigureAwait(false);
            profilesByFingerprint.Clear();
            await PersistProfilesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string ResolveStorePath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? "Data/accepted-differences.json"
            : configuredPath;

        var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var sharedBaseDirectory = string.IsNullOrWhiteSpace(commonApplicationData)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComparisonTool")
            : Path.Combine(commonApplicationData, "ComparisonTool");

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(sharedBaseDirectory, path));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AcceptedDifferenceProfile CloneProfile(AcceptedDifferenceProfile profile) => new()
    {
        Id = profile.Id,
        Fingerprint = profile.Fingerprint,
        NormalizedPropertyPath = profile.NormalizedPropertyPath,
        Category = profile.Category,
        ExpectedValuePattern = profile.ExpectedValuePattern,
        ActualValuePattern = profile.ActualValuePattern,
        SamplePropertyPath = profile.SamplePropertyPath,
        SampleExpectedValue = profile.SampleExpectedValue,
        SampleActualValue = profile.SampleActualValue,
        Status = profile.Status,
        TicketId = profile.TicketId,
        Notes = profile.Notes,
        CreatedUtc = profile.CreatedUtc,
        UpdatedUtc = profile.UpdatedUtc,
    };

    private static string FormatSampleValue(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        var text = value switch
        {
            DateTime dateTime => dateTime.ToString("O"),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
            _ => value.ToString() ?? string.Empty,
        };

        return text.Length > 256 ? text[..256] : text;
    }

    private static AcceptedDifferenceProfile SanitizeImportedProfile(
        AcceptedDifferenceProfile profile,
        AcceptedDifferenceProfile? existingProfile)
    {
        var now = DateTime.UtcNow;

        return new AcceptedDifferenceProfile
        {
            Id = profile.Id == Guid.Empty ? existingProfile?.Id ?? Guid.NewGuid() : profile.Id,
            Fingerprint = profile.Fingerprint.Trim(),
            NormalizedPropertyPath = profile.NormalizedPropertyPath ?? string.Empty,
            Category = profile.Category,
            ExpectedValuePattern = profile.ExpectedValuePattern ?? string.Empty,
            ActualValuePattern = profile.ActualValuePattern ?? string.Empty,
            SamplePropertyPath = profile.SamplePropertyPath ?? string.Empty,
            SampleExpectedValue = profile.SampleExpectedValue ?? string.Empty,
            SampleActualValue = profile.SampleActualValue ?? string.Empty,
            Status = profile.Status,
            TicketId = Normalize(profile.TicketId),
            Notes = Normalize(profile.Notes),
            CreatedUtc = profile.CreatedUtc == default ? existingProfile?.CreatedUtc ?? now : profile.CreatedUtc,
            UpdatedUtc = profile.UpdatedUtc == default ? now : profile.UpdatedUtc,
        };
    }

    private async Task LoadProfilesIfChangedAsync(CancellationToken cancellationToken, bool forceReload = false)
    {
        if (!File.Exists(storePath))
        {
            profilesByFingerprint = new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
            lastStoreWriteTimeUtc = null;
            return;
        }

        var currentWriteTimeUtc = File.GetLastWriteTimeUtc(storePath);
        if (!forceReload && lastStoreWriteTimeUtc.HasValue && currentWriteTimeUtc == lastStoreWriteTimeUtc.Value)
        {
            return;
        }

        try
        {
            await using var stream = new FileStream(storePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var store = await JsonSerializer.DeserializeAsync<AcceptedDifferenceProfileStore>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
                ?? new AcceptedDifferenceProfileStore();

            var validProfiles = store.Profiles?
                .Where(profile => profile != null && !string.IsNullOrWhiteSpace(profile.Fingerprint))
                .Select(profile => profile!)
                ?? Enumerable.Empty<AcceptedDifferenceProfile>();

            profilesByFingerprint = validProfiles
                .GroupBy(profile => profile.Fingerprint, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

            lastStoreWriteTimeUtc = currentWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning(ex, "Failed to load accepted-difference store from {StorePath}. Continuing with cached or empty data.", storePath);
            profilesByFingerprint ??= new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
            lastStoreWriteTimeUtc = currentWriteTimeUtc;
        }
    }

    private async Task PersistProfilesAsync(CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(storePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var store = new AcceptedDifferenceProfileStore
        {
            Profiles = profilesByFingerprint.Values
                .OrderBy(profile => profile.NormalizedPropertyPath, StringComparer.Ordinal)
                .ThenBy(profile => profile.Fingerprint, StringComparer.Ordinal)
                .ToList(),
        };

        var tempFilePath = storePath + ".tmp";
        await File.WriteAllTextAsync(
            tempFilePath,
            JsonSerializer.Serialize(store, SerializerOptions),
            cancellationToken).ConfigureAwait(false);

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
        var directoryPath = Path.GetDirectoryName(storePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var lockFilePath = storePath + ".lock";
        var startedAt = DateTime.UtcNow;
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
}