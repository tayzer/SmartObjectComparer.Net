using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Workspaces;

public sealed class FileSystemRunDetailStore : IRunDetailStore
{
    private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string workspaceRoot;

    public FileSystemRunDetailStore(string workspaceRoot)
    {
        this.workspaceRoot = FileSystemWorkspacePaths.NormalizeRoot(workspaceRoot);
    }

    public async Task<RunDetailReference> SaveDetailsAsync(
        RunId runId,
        IReadOnlyList<RequestPairResult> results,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);

        string detailId = FileSystemWorkspacePaths.ToLogicalPath("runs", runId.Value, "details", "index.json");
        string detailPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, detailId);
        Directory.CreateDirectory(Path.GetDirectoryName(detailPath) ?? workspaceRoot);

        await using FileStream stream = File.Create(detailPath);
        await using Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
        });

        writer.WriteStartArray();
        foreach (RequestPairResult result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonSerializer.Serialize(writer, ToDto(result), jsonOptions);
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        return new RunDetailReference(detailId, new ArtifactReference(detailId, "application/json"));
    }

    public async Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(
        RunDetailReference detailReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detailReference);

        string detailPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, detailReference.DetailId);
        await using FileStream stream = File.OpenRead(detailPath);
        List<RequestPairResult> results = new List<RequestPairResult>();

        await foreach (RequestPairResultDto? dto in JsonSerializer
            .DeserializeAsyncEnumerable<RequestPairResultDto>(stream, jsonOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dto is not null)
            {
                results.Add(FromDto(dto));
            }
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

        string detailPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, detailReference.DetailId);
        await using FileStream stream = File.OpenRead(detailPath);
        List<RequestPairResult> pageItems = new List<RequestPairResult>();
        int matchingCount = 0;

        await foreach (RequestPairResultDto? dto in JsonSerializer
            .DeserializeAsyncEnumerable<RequestPairResultDto>(stream, jsonOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dto is null || !Matches(dto, query))
            {
                continue;
            }

            if (matchingCount >= query.Offset && pageItems.Count < query.Limit)
            {
                pageItems.Add(FromDto(dto));
            }

            matchingCount++;
        }

        return new RunDetailPage(pageItems, matchingCount, query.Offset, query.Limit);
    }

    private static bool Matches(RequestPairResultDto dto, RunDetailQuery query)
    {
        if (query.Outcome is not null && dto.Outcome != query.Outcome.Value)
        {
            return false;
        }

        return query.RelativePathSearch is null
            || dto.RelativePath.Contains(query.RelativePathSearch, StringComparison.OrdinalIgnoreCase);
    }

    private RequestPairResultDto ToDto(RequestPairResult result) =>
        new RequestPairResultDto
        {
            RelativePath = result.RelativePath,
            Outcome = result.Outcome,
            ResponseA = result.ResponseA is null ? null : ToDto(result.ResponseA),
            ResponseB = result.ResponseB is null ? null : ToDto(result.ResponseB),
            ErrorMessage = result.ErrorMessage,
            OutcomeMessage = result.OutcomeMessage,
            AreEqual = result.AreEqual,
            DifferenceCount = result.DifferenceCount,
            Differences = result.Differences.Select(ToDto).ToList(),
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
            dto.OutcomeMessage);

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

    private sealed class RequestPairResultDto
    {
        public string RelativePath { get; init; } = string.Empty;

        public RequestPairOutcome Outcome { get; init; }

        public ResponseArtifactMetadataDto? ResponseA { get; init; }

        public ResponseArtifactMetadataDto? ResponseB { get; init; }

        public string? ErrorMessage { get; init; }

        public string? OutcomeMessage { get; init; }

        public bool? AreEqual { get; init; }

        public int? DifferenceCount { get; init; }

        public List<ComparisonDifferenceDto> Differences { get; init; } = new List<ComparisonDifferenceDto>();
    }

    private sealed class ResponseArtifactMetadataDto
    {
        public EndpointSlot Endpoint { get; init; }

        public string ArtifactId { get; init; } = string.Empty;

        public string? ArtifactContentType { get; init; }

        public int StatusCode { get; init; }

        public string? ContentType { get; init; }

        public long ContentLength { get; init; }

        public string Sha256 { get; init; } = string.Empty;
    }

    private sealed class ComparisonDifferenceDto
    {
        public string PropertyPath { get; init; } = string.Empty;

        public string? ValueA { get; init; }

        public string? ValueB { get; init; }

        public string? Message { get; init; }
    }
}