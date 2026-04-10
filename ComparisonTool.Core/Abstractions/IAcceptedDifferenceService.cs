using ComparisonTool.Core.AcceptedDifferences;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Abstractions;

/// <summary>
/// Provides persistence and matching for tester-tracked differences across runs.
/// </summary>
public interface IAcceptedDifferenceService
{
    /// <summary>
    /// Gets a value indicating whether accepted-difference tracking is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Creates a stable fingerprint for a difference that tolerates dynamic values.
    /// </summary>
    /// <param name="difference">The difference to fingerprint.</param>
    /// <returns>A stable fingerprint descriptor.</returns>
    AcceptedDifferenceFingerprint CreateFingerprint(Difference difference);

    /// <summary>
    /// Returns all persisted profiles.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All persisted profiles.</returns>
    Task<IReadOnlyList<AcceptedDifferenceProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves persisted profiles that match the supplied differences.
    /// </summary>
    /// <param name="differences">The differences to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary keyed by fingerprint.</returns>
    Task<IReadOnlyDictionary<string, AcceptedDifferenceProfile>> GetMatchesAsync(
        IEnumerable<Difference> differences,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates a persisted profile for the supplied difference fingerprint.
    /// </summary>
    /// <param name="difference">The source difference.</param>
    /// <param name="status">The persisted classification.</param>
    /// <param name="notes">Optional tester notes.</param>
    /// <param name="ticketId">Optional ticket ID. Required for known bugs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved profile.</returns>
    Task<AcceptedDifferenceProfile> SaveAsync(
        Difference difference,
        AcceptedDifferenceStatus status,
        string? notes = null,
        string? ticketId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports accepted-difference profiles into the store.
    /// </summary>
    /// <param name="profiles">Profiles to import.</param>
    /// <param name="replaceExisting">True to replace existing profiles before import.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of imported profiles.</returns>
    Task<int> ImportAsync(
        IEnumerable<AcceptedDifferenceProfile> profiles,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a persisted profile for the supplied difference fingerprint.
    /// </summary>
    /// <param name="difference">The source difference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when a profile was removed.</returns>
    Task<bool> RemoveAsync(Difference difference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a persisted profile by fingerprint.
    /// </summary>
    /// <param name="fingerprint">Fingerprint to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when a profile was removed.</returns>
    Task<bool> RemoveByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all persisted accepted-difference profiles.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the clear operation.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);
}