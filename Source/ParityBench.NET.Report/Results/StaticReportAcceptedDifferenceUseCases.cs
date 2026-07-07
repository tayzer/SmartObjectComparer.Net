using System.Text.Json;

using ParityBench.NET.Application.AcceptedDifferences;
using ParityBench.NET.Domain.AcceptedDifferences;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;

namespace ParityBench.NET.Report.Results;

public sealed class StaticReportAcceptedDifferenceUseCases : IAcceptedDifferenceUseCases
{
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions jsonOptions;
    private InMemoryAcceptedDifferenceUseCases? inner;

    public StaticReportAcceptedDifferenceUseCases(HttpClient httpClient)
    {
        this.httpClient = httpClient;
        jsonOptions = StaticReportJsonOptions.Create();
    }

    public bool IsReadOnly => true;

    public AcceptedDifferenceFingerprint CreateFingerprint(ComparisonDifference difference) =>
        AcceptedDifferenceFingerprintBuilder.Create(difference);

    public async Task<IReadOnlyList<AcceptedDifferenceProfile>> ListAsync(CancellationToken cancellationToken = default) =>
        await (await GetInnerAsync(cancellationToken).ConfigureAwait(false)).ListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<string, AcceptedDifferenceProfile>> MatchAsync(
        IEnumerable<ComparisonDifference> differences,
        CancellationToken cancellationToken = default) =>
        await (await GetInnerAsync(cancellationToken).ConfigureAwait(false)).MatchAsync(differences, cancellationToken).ConfigureAwait(false);

    public async Task<AcceptedDifferenceProfile> SaveAsync(
        ComparisonDifference difference,
        AcceptedDifferenceStatus status,
        string? notes = null,
        string? ticketId = null,
        CancellationToken cancellationToken = default) =>
        await (await GetInnerAsync(cancellationToken).ConfigureAwait(false)).SaveAsync(difference, status, notes, ticketId, cancellationToken).ConfigureAwait(false);

    public async Task<int> ImportAsync(
        IEnumerable<AcceptedDifferenceProfile> profiles,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default) =>
        await (await GetInnerAsync(cancellationToken).ConfigureAwait(false)).ImportAsync(profiles, replaceExisting, cancellationToken).ConfigureAwait(false);

    public async Task<bool> RemoveAsync(ComparisonDifference difference, CancellationToken cancellationToken = default) =>
        await (await GetInnerAsync(cancellationToken).ConfigureAwait(false)).RemoveAsync(difference, cancellationToken).ConfigureAwait(false);

    public async Task<bool> RemoveByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default) =>
        await (await GetInnerAsync(cancellationToken).ConfigureAwait(false)).RemoveByFingerprintAsync(fingerprint, cancellationToken).ConfigureAwait(false);

    public async Task ClearAsync(CancellationToken cancellationToken = default) =>
        await (await GetInnerAsync(cancellationToken).ConfigureAwait(false)).ClearAsync(cancellationToken).ConfigureAwait(false);

    private async Task<InMemoryAcceptedDifferenceUseCases> GetInnerAsync(CancellationToken cancellationToken)
    {
        if (inner is not null)
        {
            return inner;
        }

        await using Stream stream = await httpClient.GetStreamAsync("report.data.json", cancellationToken).ConfigureAwait(false);
        StaticReportManifest? manifest = await JsonSerializer.DeserializeAsync<StaticReportManifest>(stream, jsonOptions, cancellationToken).ConfigureAwait(false);
        inner = new InMemoryAcceptedDifferenceUseCases(manifest?.AcceptedDifferences?.Profiles, isReadOnly: false);
        return inner;
    }
}