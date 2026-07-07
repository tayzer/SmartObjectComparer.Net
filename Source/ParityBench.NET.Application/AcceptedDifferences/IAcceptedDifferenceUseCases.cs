using ParityBench.NET.Domain.AcceptedDifferences;
using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Application.AcceptedDifferences;

public interface IAcceptedDifferenceUseCases
{
    bool IsReadOnly { get; }

    AcceptedDifferenceFingerprint CreateFingerprint(ComparisonDifference difference);

    Task<IReadOnlyList<AcceptedDifferenceProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, AcceptedDifferenceProfile>> MatchAsync(
        IEnumerable<ComparisonDifference> differences,
        CancellationToken cancellationToken = default);

    Task<AcceptedDifferenceProfile> SaveAsync(
        ComparisonDifference difference,
        AcceptedDifferenceStatus status,
        string? notes = null,
        string? ticketId = null,
        CancellationToken cancellationToken = default);

    Task<int> ImportAsync(
        IEnumerable<AcceptedDifferenceProfile> profiles,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(ComparisonDifference difference, CancellationToken cancellationToken = default);

    Task<bool> RemoveByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
