using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Workspaces;

public sealed class FileSystemRunStore : IRunStore
{
    private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string workspaceRoot;

    public FileSystemRunStore(string workspaceRoot)
    {
        this.workspaceRoot = FileSystemWorkspacePaths.NormalizeRoot(workspaceRoot);
    }

    public async Task SaveAsync(ComparisonRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        string runPath = GetRunPath(run.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(runPath) ?? workspaceRoot);

        await using FileStream stream = File.Create(runPath);
        await JsonSerializer
            .SerializeAsync(stream, ToDto(run), jsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ComparisonRun?> LoadAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        string runPath = GetRunPath(runId);
        if (!File.Exists(runPath))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(runPath);
        RunSnapshotDto? dto = await JsonSerializer
            .DeserializeAsync<RunSnapshotDto>(stream, jsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return dto is null ? null : FromDto(dto);
    }

    public async Task<IReadOnlyList<RunListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        string runsRoot = Path.Combine(workspaceRoot, "runs");
        if (!Directory.Exists(runsRoot))
        {
            return Array.Empty<RunListItem>();
        }

        List<RunListItem> runs = new List<RunListItem>();
        foreach (string runPath in Directory.EnumerateFiles(runsRoot, "run.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using FileStream stream = File.OpenRead(runPath);
            RunSnapshotDto? dto = await JsonSerializer
                .DeserializeAsync<RunSnapshotDto>(stream, jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (dto is not null)
            {
                runs.Add(RunListItem.FromRun(FromDto(dto)));
            }
        }

        return runs
            .OrderBy(run => run.CreatedAt)
            .ThenBy(run => run.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<RunResultSummary?> LoadSummaryAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun? run = await LoadAsync(runId, cancellationToken).ConfigureAwait(false);
        return run?.Summary;
    }

    private string GetRunPath(RunId runId) =>
        FileSystemWorkspacePaths.GetSafePath(
            workspaceRoot,
            FileSystemWorkspacePaths.ToLogicalPath("runs", runId.Value, "run.json"));

    private RunSnapshotDto ToDto(ComparisonRun run) =>
        new RunSnapshotDto
        {
            Id = run.Id.Value,
            Options = ToDto(run.Options),
            Status = run.Status,
            Progress = ToDto(run.Progress),
            CreatedAt = run.CreatedAt,
            UpdatedAt = run.UpdatedAt,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            Summary = run.Summary is null ? null : ToDto(run.Summary),
            ErrorMessage = run.ErrorMessage,
        };

    private RunOptionsDto ToDto(RunOptions options) =>
        new RunOptionsDto
        {
            RequestBatch = options.RequestBatch.Value,
            EndpointA = ToDto(options.EndpointA),
            EndpointB = ToDto(options.EndpointB),
            TimeoutMilliseconds = options.Timeout.TotalMilliseconds,
            MaxConcurrency = options.MaxConcurrency,
            ModelName = options.ModelName,
        };

    private EndpointDefinitionDto ToDto(EndpointDefinition endpoint) =>
        new EndpointDefinitionDto
        {
            Uri = endpoint.Uri.ToString(),
            Label = endpoint.Label,
            Headers = endpoint.Headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
        };

    private RunProgressDto ToDto(RunProgress progress) =>
        new RunProgressDto
        {
            PercentComplete = progress.PercentComplete,
            Message = progress.Message,
            CompletedItems = progress.CompletedItems,
            TotalItems = progress.TotalItems,
        };

    private RunResultSummaryDto ToDto(RunResultSummary summary) =>
        new RunResultSummaryDto
        {
            TotalPairs = summary.TotalPairs,
            EqualPairs = summary.EqualPairs,
            DifferentPairs = summary.DifferentPairs,
            ErrorPairs = summary.ErrorPairs,
            StatusCodeMismatchPairs = summary.StatusCodeMismatchPairs,
            BothNonSuccessPairs = summary.BothNonSuccessPairs,
            DetailIndexReference = summary.DetailIndexReference is null ? null : ToDto(summary.DetailIndexReference),
        };

    private RunDetailReferenceDto ToDto(RunDetailReference reference) =>
        new RunDetailReferenceDto
        {
            DetailId = reference.DetailId,
            Artifact = reference.Artifact is null ? null : ToDto(reference.Artifact),
        };

    private ArtifactReferenceDto ToDto(ArtifactReference reference) =>
        new ArtifactReferenceDto
        {
            ArtifactId = reference.ArtifactId,
            ContentType = reference.ContentType,
        };

    private ComparisonRun FromDto(RunSnapshotDto dto) =>
        ComparisonRun.Rehydrate(
            new RunId(dto.Id),
            FromDto(dto.Options),
            dto.Status,
            FromDto(dto.Progress),
            dto.CreatedAt,
            dto.UpdatedAt,
            dto.StartedAt,
            dto.CompletedAt,
            dto.Summary is null ? null : FromDto(dto.Summary),
            dto.ErrorMessage);

    private RunOptions FromDto(RunOptionsDto dto) =>
        new RunOptions(
            new RequestBatchReference(dto.RequestBatch),
            FromDto(dto.EndpointA),
            FromDto(dto.EndpointB),
            TimeSpan.FromMilliseconds(dto.TimeoutMilliseconds),
            dto.MaxConcurrency,
            dto.ModelName);

    private EndpointDefinition FromDto(EndpointDefinitionDto dto) =>
        new EndpointDefinition(
            new Uri(dto.Uri, UriKind.Absolute),
            dto.Label,
            dto.Headers);

    private RunProgress FromDto(RunProgressDto dto) =>
        new RunProgress(
            dto.PercentComplete,
            dto.Message,
            dto.CompletedItems,
            dto.TotalItems);

    private RunResultSummary FromDto(RunResultSummaryDto dto) =>
        new RunResultSummary(
            dto.TotalPairs,
            dto.EqualPairs,
            dto.DifferentPairs,
            dto.ErrorPairs,
            dto.StatusCodeMismatchPairs,
            dto.BothNonSuccessPairs,
            dto.DetailIndexReference is null ? null : FromDto(dto.DetailIndexReference));

    private RunDetailReference FromDto(RunDetailReferenceDto dto) =>
        new RunDetailReference(
            dto.DetailId,
            dto.Artifact is null ? null : FromDto(dto.Artifact));

    private ArtifactReference FromDto(ArtifactReferenceDto dto) =>
        new ArtifactReference(dto.ArtifactId, dto.ContentType);

    private sealed class RunSnapshotDto
    {
        public string Id { get; init; } = string.Empty;

        public RunOptionsDto Options { get; init; } = new RunOptionsDto();

        public RunStatus Status { get; init; }

        public RunProgressDto Progress { get; init; } = new RunProgressDto();

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public DateTimeOffset? StartedAt { get; init; }

        public DateTimeOffset? CompletedAt { get; init; }

        public RunResultSummaryDto? Summary { get; init; }

        public string? ErrorMessage { get; init; }
    }

    private sealed class RunOptionsDto
    {
        public string RequestBatch { get; init; } = string.Empty;

        public EndpointDefinitionDto EndpointA { get; init; } = new EndpointDefinitionDto();

        public EndpointDefinitionDto EndpointB { get; init; } = new EndpointDefinitionDto();

        public double TimeoutMilliseconds { get; init; }

        public int MaxConcurrency { get; init; }

        public string ModelName { get; init; } = "Auto";
    }

    private sealed class EndpointDefinitionDto
    {
        public string Uri { get; init; } = "https://example.test";

        public string? Label { get; init; }

        public Dictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RunProgressDto
    {
        public int PercentComplete { get; init; }

        public string Message { get; init; } = string.Empty;

        public int? CompletedItems { get; init; }

        public int? TotalItems { get; init; }
    }

    private sealed class RunResultSummaryDto
    {
        public int TotalPairs { get; init; }

        public int EqualPairs { get; init; }

        public int DifferentPairs { get; init; }

        public int ErrorPairs { get; init; }

        public int StatusCodeMismatchPairs { get; init; }

        public int BothNonSuccessPairs { get; init; }

        public RunDetailReferenceDto? DetailIndexReference { get; init; }
    }

    private sealed class RunDetailReferenceDto
    {
        public string DetailId { get; init; } = string.Empty;

        public ArtifactReferenceDto? Artifact { get; init; }
    }

    private sealed class ArtifactReferenceDto
    {
        public string ArtifactId { get; init; } = string.Empty;

        public string? ContentType { get; init; }
    }
}
