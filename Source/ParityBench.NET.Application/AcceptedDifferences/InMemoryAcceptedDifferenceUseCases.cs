using ParityBench.NET.Domain.AcceptedDifferences;
using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Application.AcceptedDifferences;

public sealed class InMemoryAcceptedDifferenceUseCases : IAcceptedDifferenceUseCases
{
    private readonly Dictionary<string, AcceptedDifferenceProfile> profilesByFingerprint;

    public InMemoryAcceptedDifferenceUseCases(IEnumerable<AcceptedDifferenceProfile>? profiles = null, bool isReadOnly = true)
    {
        profilesByFingerprint = (profiles ?? Array.Empty<AcceptedDifferenceProfile>())
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Fingerprint))
            .GroupBy(profile => profile.Fingerprint, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        IsReadOnly = isReadOnly;
    }

    public bool IsReadOnly { get; }

    public AcceptedDifferenceFingerprint CreateFingerprint(ComparisonDifference difference) =>
        AcceptedDifferenceFingerprintBuilder.Create(difference);

    public Task<IReadOnlyList<AcceptedDifferenceProfile>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AcceptedDifferenceProfile>>(profilesByFingerprint.Values.OrderBy(profile => profile.NormalizedPropertyPath, StringComparer.OrdinalIgnoreCase).ToList());

    public Task<IReadOnlyDictionary<string, AcceptedDifferenceProfile>> MatchAsync(
        IEnumerable<ComparisonDifference> differences,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, AcceptedDifferenceProfile> matches = new Dictionary<string, AcceptedDifferenceProfile>(StringComparer.Ordinal);
        foreach (ComparisonDifference difference in differences)
        {
            string fingerprint = CreateFingerprint(difference).Fingerprint;
            if (profilesByFingerprint.TryGetValue(fingerprint, out AcceptedDifferenceProfile? profile))
            {
                matches[fingerprint] = profile;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, AcceptedDifferenceProfile>>(matches);
    }

    public Task<AcceptedDifferenceProfile> SaveAsync(
        ComparisonDifference difference,
        AcceptedDifferenceStatus status,
        string? notes = null,
        string? ticketId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();
        AcceptedDifferenceProfile profile = CreateProfile(difference, status, notes, ticketId, profilesByFingerprint.GetValueOrDefault(CreateFingerprint(difference).Fingerprint));
        profilesByFingerprint[profile.Fingerprint] = profile;
        return Task.FromResult(profile);
    }

    public Task<int> ImportAsync(
        IEnumerable<AcceptedDifferenceProfile> profiles,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
    {
        if (IsReadOnly)
        {
            if (replaceExisting)
            {
                profilesByFingerprint.Clear();
            }

            int imported = 0;
            foreach (AcceptedDifferenceProfile profile in profiles.Where(profile => !string.IsNullOrWhiteSpace(profile.Fingerprint)))
            {
                profilesByFingerprint[profile.Fingerprint] = profile;
                imported++;
            }

            return Task.FromResult(imported);
        }

        if (replaceExisting)
        {
            profilesByFingerprint.Clear();
        }

        int count = 0;
        foreach (AcceptedDifferenceProfile profile in profiles.Where(profile => !string.IsNullOrWhiteSpace(profile.Fingerprint)))
        {
            profilesByFingerprint[profile.Fingerprint] = profile;
            count++;
        }

        return Task.FromResult(count);
    }

    public Task<bool> RemoveAsync(ComparisonDifference difference, CancellationToken cancellationToken = default) =>
        RemoveByFingerprintAsync(CreateFingerprint(difference).Fingerprint, cancellationToken);

    public Task<bool> RemoveByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();
        return Task.FromResult(profilesByFingerprint.Remove(fingerprint));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();
        profilesByFingerprint.Clear();
        return Task.CompletedTask;
    }

    private static AcceptedDifferenceProfile CreateProfile(
        ComparisonDifference difference,
        AcceptedDifferenceStatus status,
        string? notes,
        string? ticketId,
        AcceptedDifferenceProfile? existingProfile)
    {
        if (status == AcceptedDifferenceStatus.KnownBug && string.IsNullOrWhiteSpace(ticketId))
        {
            throw new ArgumentException("A ticket ID is required when tracking a known bug.", nameof(ticketId));
        }

        AcceptedDifferenceFingerprint fingerprint = AcceptedDifferenceFingerprintBuilder.Create(difference);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new AcceptedDifferenceProfile
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
    }

    private void ThrowIfReadOnly()
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("Accepted-difference profiles are read-only in this context.");
        }
    }

    private static string FormatSampleValue(string? value) => (value ?? "null").Length > 256 ? (value ?? "null")[..256] : value ?? "null";

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
