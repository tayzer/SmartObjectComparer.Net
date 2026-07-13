using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

public sealed class FileSystemRunDetailStore : IRunDetailStore
{
    internal const int CurrentSchemaVersion = 2;
    private const int DefaultPageSize = 250;
    private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal readonly string workspaceRoot;

    public FileSystemRunDetailStore(string workspaceRoot)
    {
        this.workspaceRoot = FileSystemWorkspacePaths.NormalizeRoot(workspaceRoot);
    }

    public Task<IRunDetailWriter> CreateWriterAsync(
        RunId runId,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be greater than zero.");
        }

        string detailRootId = FileSystemWorkspacePaths.ToLogicalPath("runs", runId.Value, "details");
        string detailRootPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, detailRootId);
        Directory.CreateDirectory(detailRootPath);
        ClearGeneratedDetailFiles(detailRootPath);
        return Task.FromResult<IRunDetailWriter>(new PagedRunDetailWriter(this, runId, detailRootId, detailRootPath, pageSize));
    }

    public async Task<RunDetailReference> SaveDetailsAsync(
        RunId runId,
        IReadOnlyList<RequestPairResult> results,
        CancellationToken cancellationToken = default)
    {
        await using IRunDetailWriter writer = await CreateWriterAsync(runId, DefaultPageSize, cancellationToken).ConfigureAwait(false);
        await writer.AppendAsync(results, cancellationToken).ConfigureAwait(false);
        return await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(
        RunDetailReference detailReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detailReference);

        DetailManifestDto manifest = await LoadManifestAsync(detailReference, cancellationToken).ConfigureAwait(false);
        List<RequestPairResult> results = new List<RequestPairResult>(manifest.TotalCount);
        foreach (DetailPageInfoDto page in manifest.Pages.OrderBy(page => page.PageIndex))
        {
            results.AddRange(await LoadPageItemsAsync(page.Path, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public async Task<RunDetailPage> LoadPageAsync(
        RunDetailReference detailReference,
        RunDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detailReference);
        ArgumentNullException.ThrowIfNull(query);

        DetailManifestDto manifest = await LoadManifestAsync(detailReference, cancellationToken).ConfigureAwait(false);
        if (query.Outcome is null && query.RelativePathSearch is null)
        {
            return await LoadUnfilteredPageAsync(manifest, query, cancellationToken).ConfigureAwait(false);
        }

        return await LoadFilteredPageAsync(manifest, query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StaticReportAnalysisSnapshot?> LoadAnalysisAsync(
        RunDetailReference detailReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detailReference);
        ArtifactReference? artifact = detailReference.AnalysisArtifact;
        if (artifact is null)
        {
            DetailManifestDto manifest = await LoadManifestAsync(detailReference, cancellationToken).ConfigureAwait(false);
            artifact = string.IsNullOrWhiteSpace(manifest.AnalysisPath) ? null : new ArtifactReference(manifest.AnalysisPath, "application/json");
        }

        return artifact is null
            ? null
            : await ReadJsonArtifactAsync<StaticReportAnalysisSnapshot>(artifact.ArtifactId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StaticReportDifferenceIndex?> LoadDifferenceIndexAsync(
        RunDetailReference detailReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detailReference);
        ArtifactReference? artifact = detailReference.DifferenceIndexArtifact;
        if (artifact is null)
        {
            DetailManifestDto manifest = await LoadManifestAsync(detailReference, cancellationToken).ConfigureAwait(false);
            artifact = string.IsNullOrWhiteSpace(manifest.DifferenceIndexPath) ? null : new ArtifactReference(manifest.DifferenceIndexPath, "application/json");
        }

        return artifact is null
            ? null
            : await ReadJsonArtifactAsync<StaticReportDifferenceIndex>(artifact.ArtifactId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RunDetailPage> LoadUnfilteredPageAsync(
        DetailManifestDto manifest,
        RunDetailQuery query,
        CancellationToken cancellationToken)
    {
        List<RequestPairResult> items = new List<RequestPairResult>(query.Limit);
        foreach (DetailPageInfoDto pageInfo in manifest.Pages.OrderBy(page => page.PageIndex))
        {
            if (pageInfo.Offset + pageInfo.ItemCount <= query.Offset)
            {
                continue;
            }

            if (items.Count >= query.Limit)
            {
                break;
            }

            IReadOnlyList<RequestPairResult> pageItems = await LoadPageItemsAsync(pageInfo.Path, cancellationToken).ConfigureAwait(false);
            int skip = Math.Max(0, query.Offset - pageInfo.Offset);
            items.AddRange(pageItems.Skip(skip).Take(query.Limit - items.Count));
        }

        return new RunDetailPage(items, manifest.TotalCount, query.Offset, query.Limit);
    }

    private async Task<RunDetailPage> LoadFilteredPageAsync(
        DetailManifestDto manifest,
        RunDetailQuery query,
        CancellationToken cancellationToken)
    {
        List<RequestPairResult> pageItems = new List<RequestPairResult>(query.Limit);
        int matchingCount = 0;
        foreach (DetailPageInfoDto pageInfo in manifest.Pages.OrderBy(page => page.PageIndex))
        {
            IReadOnlyList<RequestPairResult> items = await LoadPageItemsAsync(pageInfo.Path, cancellationToken).ConfigureAwait(false);
            foreach (RequestPairResult item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Matches(item, query))
                {
                    continue;
                }

                if (matchingCount >= query.Offset && pageItems.Count < query.Limit)
                {
                    pageItems.Add(item);
                }

                matchingCount++;
            }
        }

        return new RunDetailPage(pageItems, matchingCount, query.Offset, query.Limit);
    }

    private async Task<DetailManifestDto> LoadManifestAsync(
        RunDetailReference detailReference,
        CancellationToken cancellationToken)
    {
        string detailPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, detailReference.DetailId);
        if (Path.GetFileName(detailPath).Equals("index.json", StringComparison.OrdinalIgnoreCase))
        {
            return await LoadLegacyIndexAsManifestAsync(detailReference, cancellationToken).ConfigureAwait(false);
        }

        await using FileStream stream = File.OpenRead(detailPath);
        DetailManifestDto? manifest = await JsonSerializer.DeserializeAsync<DetailManifestDto>(stream, jsonOptions, cancellationToken).ConfigureAwait(false);
        return manifest ?? throw new InvalidOperationException($"Run detail manifest '{detailPath}' could not be read.");
    }

    private async Task<DetailManifestDto> LoadLegacyIndexAsManifestAsync(
        RunDetailReference detailReference,
        CancellationToken cancellationToken)
    {
        string detailPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, detailReference.DetailId);
        await using FileStream stream = File.OpenRead(detailPath);
        List<RequestPairResultDto> dtos = await JsonSerializer.DeserializeAsync<List<RequestPairResultDto>>(stream, jsonOptions, cancellationToken).ConfigureAwait(false)
            ?? new List<RequestPairResultDto>();
        return new DetailManifestDto
        {
            SchemaVersion = 1,
            PageSize = Math.Max(1, dtos.Count),
            TotalCount = dtos.Count,
            Pages = new List<DetailPageInfoDto>
            {
                new DetailPageInfoDto
                {
                    PageIndex = 0,
                    Offset = 0,
                    ItemCount = dtos.Count,
                    Path = detailReference.DetailId,
                },
            },
        };
    }

    private async Task<IReadOnlyList<RequestPairResult>> LoadPageItemsAsync(
        string pageArtifactId,
        CancellationToken cancellationToken)
    {
        string pagePath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, pageArtifactId);
        await using FileStream stream = File.OpenRead(pagePath);
        List<RequestPairResultDto>? dtos = await JsonSerializer.DeserializeAsync<List<RequestPairResultDto>>(stream, jsonOptions, cancellationToken).ConfigureAwait(false);
        return (dtos ?? new List<RequestPairResultDto>()).Select(FromDto).ToList();
    }

    private async Task<T?> ReadJsonArtifactAsync<T>(
        string artifactId,
        CancellationToken cancellationToken)
    {
        string path = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, artifactId);
        if (!File.Exists(path))
        {
            return default;
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    internal async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? workspaceRoot);
        await using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, value, jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static bool Matches(RequestPairResult item, RunDetailQuery query) =>
        (query.Outcome is null || item.Outcome == query.Outcome.Value)
        && (query.RelativePathSearch is null || item.RelativePath.Contains(query.RelativePathSearch, StringComparison.OrdinalIgnoreCase));

    private static void ClearGeneratedDetailFiles(string detailRootPath)
    {
        foreach (string file in Directory.EnumerateFiles(detailRootPath, "*.json", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }
    }

    internal RequestPairResultDto ToDto(RequestPairResult result) =>
        new RequestPairResultDto
        {
            RelativePath = result.RelativePath,
            Outcome = result.Outcome,
            ResponseA = result.ResponseA is null ? null : ToDto(result.ResponseA),
            ResponseB = result.ResponseB is null ? null : ToDto(result.ResponseB),
            FocusedResponseA = result.FocusedResponseA is null ? null : ToDto(result.FocusedResponseA),
            FocusedResponseB = result.FocusedResponseB is null ? null : ToDto(result.FocusedResponseB),
            FocusedRawContentIgnorePaths = result.FocusedRawContentIgnorePaths.ToList(),
            ErrorMessage = result.ErrorMessage,
            OutcomeMessage = result.OutcomeMessage,
            AreEqual = result.AreEqual,
            DifferenceCount = result.DifferenceCount,
            Differences = result.Differences.Select(ToDto).ToList(),
            PairRetentionClass = result.PairRetentionClass,
            ArtifactRetentionState = ToDto(result.ArtifactRetentionState),
            RetentionAppliedAt = result.RetentionAppliedAt,
        };

    private PairArtifactRetentionStateDto ToDto(PairArtifactRetentionState state) =>
        new PairArtifactRetentionStateDto
        {
            RawResponseA = state.RawResponseA,
            RawResponseB = state.RawResponseB,
            CanonicalResponseA = state.CanonicalResponseA,
            CanonicalResponseB = state.CanonicalResponseB,
            FocusedResponseA = state.FocusedResponseA,
            FocusedResponseB = state.FocusedResponseB,
        };

    private ResponseArtifactMetadataDto ToDto(ResponseArtifactMetadata metadata) =>
        new ResponseArtifactMetadataDto
        {
            Endpoint = metadata.Endpoint,
            ArtifactId = metadata.Artifact.ArtifactId,
            ArtifactContentType = metadata.Artifact.ContentType,
            StatusCode = metadata.StatusCode,
            ContentType = metadata.ContentType,
            ContentLength = metadata.ContentLength,
            Sha256 = metadata.Sha256,
        };

    private ComparisonDifferenceDto ToDto(ComparisonDifference difference) =>
        new ComparisonDifferenceDto
        {
            PropertyPath = difference.PropertyPath,
            ValueA = difference.ValueA,
            ValueB = difference.ValueB,
            Message = difference.Message,
        };

    private RequestPairResult FromDto(RequestPairResultDto dto) =>
        new RequestPairResult(
            dto.RelativePath,
            dto.Outcome,
            dto.ResponseA is null ? null : FromDto(dto.ResponseA),
            dto.ResponseB is null ? null : FromDto(dto.ResponseB),
            dto.ErrorMessage,
            dto.AreEqual,
            dto.DifferenceCount,
            dto.Differences.Select(FromDto),
            dto.OutcomeMessage,
            focusedResponseA: dto.FocusedResponseA is null ? null : FromDto(dto.FocusedResponseA),
            focusedResponseB: dto.FocusedResponseB is null ? null : FromDto(dto.FocusedResponseB),
            focusedRawContentIgnorePaths: dto.FocusedRawContentIgnorePaths,
            pairRetentionClass: dto.PairRetentionClass,
            artifactRetentionState: dto.ArtifactRetentionState is null ? null : FromDto(dto.ArtifactRetentionState),
            retentionAppliedAt: dto.RetentionAppliedAt);

    private PairArtifactRetentionState FromDto(PairArtifactRetentionStateDto dto) =>
        new PairArtifactRetentionState(
            dto.RawResponseA,
            dto.RawResponseB,
            dto.CanonicalResponseA,
            dto.CanonicalResponseB,
            dto.FocusedResponseA,
            dto.FocusedResponseB);

    private ResponseArtifactMetadata FromDto(ResponseArtifactMetadataDto dto) =>
        new ResponseArtifactMetadata(
            dto.Endpoint,
            new ArtifactReference(dto.ArtifactId, dto.ArtifactContentType),
            dto.StatusCode,
            dto.ContentType,
            dto.ContentLength,
            dto.Sha256);

    private ComparisonDifference FromDto(ComparisonDifferenceDto dto) =>
        new ComparisonDifference(
            dto.PropertyPath,
            dto.ValueA,
            dto.ValueB,
            dto.Message);
}
