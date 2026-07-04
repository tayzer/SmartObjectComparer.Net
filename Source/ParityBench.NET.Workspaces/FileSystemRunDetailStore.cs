using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Requests;
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
        await JsonSerializer
            .SerializeAsync(stream, results.Select(ToDto).ToList(), jsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return new RunDetailReference(detailId, new ArtifactReference(detailId, "application/json"));
    }

    public async Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(
        RunDetailReference detailReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detailReference);

        string detailPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, detailReference.DetailId);
        await using FileStream stream = File.OpenRead(detailPath);
        List<RequestPairResultDto>? dtos = await JsonSerializer
            .DeserializeAsync<List<RequestPairResultDto>>(stream, jsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return (dtos ?? new List<RequestPairResultDto>())
            .Select(FromDto)
            .ToList();
    }

    private RequestPairResultDto ToDto(RequestPairResult result) =>
        new RequestPairResultDto
        {
            RelativePath = result.RelativePath,
            Outcome = result.Outcome,
            ResponseA = result.ResponseA is null ? null : ToDto(result.ResponseA),
            ResponseB = result.ResponseB is null ? null : ToDto(result.ResponseB),
            ErrorMessage = result.ErrorMessage,
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

    private RequestPairResult FromDto(RequestPairResultDto dto) =>
        new RequestPairResult(
            dto.RelativePath,
            dto.Outcome,
            dto.ResponseA is null ? null : FromDto(dto.ResponseA),
            dto.ResponseB is null ? null : FromDto(dto.ResponseB),
            dto.ErrorMessage);

    private ResponseArtifactMetadata FromDto(ResponseArtifactMetadataDto dto) =>
        new ResponseArtifactMetadata(
            dto.Endpoint,
            new ArtifactReference(dto.ArtifactId, dto.ArtifactContentType),
            dto.StatusCode,
            dto.ContentType,
            dto.ContentLength,
            dto.Sha256);

    private sealed class RequestPairResultDto
    {
        public string RelativePath { get; init; } = string.Empty;

        public RequestPairOutcome Outcome { get; init; }

        public ResponseArtifactMetadataDto? ResponseA { get; init; }

        public ResponseArtifactMetadataDto? ResponseB { get; init; }

        public string? ErrorMessage { get; init; }
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
}
