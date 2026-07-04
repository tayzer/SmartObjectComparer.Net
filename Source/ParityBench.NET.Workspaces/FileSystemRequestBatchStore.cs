using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Workspaces;

public sealed class FileSystemRequestBatchStore : IRequestBatchStore
{
    private readonly HashSet<string> eligibleExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".json",
        ".xml",
        ".txt",
    };

    private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string workspaceRoot;

    public FileSystemRequestBatchStore(string workspaceRoot)
    {
        this.workspaceRoot = FileSystemWorkspacePaths.NormalizeRoot(workspaceRoot);
    }

    public async Task<RequestBatchManifest> StageDirectoryAsync(
        string sourceDirectory,
        RequestBatchReference batchReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException("Source directory must not be empty.", nameof(sourceDirectory));
        }

        string sourceRoot = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"Request directory was not found: {sourceRoot}");
        }

        string batchRoot = GetBatchRoot(batchReference);
        string requestRoot = Path.Combine(batchRoot, "requests");
        Directory.CreateDirectory(requestRoot);

        List<RequestItem> requests = new List<RequestItem>();
        foreach (string sourceFilePath in EnumerateEligibleFiles(sourceRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = GetSafeSourceRelativePath(sourceRoot, sourceFilePath);
            string destinationPath = FileSystemWorkspacePaths.GetSafePath(requestRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? requestRoot);

            await using (FileStream source = new FileStream(
                sourceFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            FileInfo fileInfo = new FileInfo(sourceFilePath);
            requests.Add(new RequestItem(relativePath, GetContentType(fileInfo.Extension), fileInfo.Length));
        }

        RequestBatchManifest manifest = new RequestBatchManifest(batchReference, requests);
        await SaveManifestAsync(batchRoot, manifest, cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    public async Task<RequestBatchManifest> LoadManifestAsync(
        RequestBatchReference batchReference,
        CancellationToken cancellationToken = default)
    {
        string manifestPath = GetManifestPath(batchReference);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Request batch manifest was not found: {manifestPath}", manifestPath);
        }

        await using FileStream stream = File.OpenRead(manifestPath);
        RequestBatchManifestDto? dto = await JsonSerializer
            .DeserializeAsync<RequestBatchManifestDto>(stream, jsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (dto is null)
        {
            throw new InvalidOperationException($"Request batch manifest '{manifestPath}' could not be read.");
        }

        return FromDto(dto);
    }

    public Task<Stream> OpenRequestBodyAsync(
        RequestBatchReference batchReference,
        RequestItem request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string requestRoot = Path.Combine(GetBatchRoot(batchReference), "requests");
        string requestPath = FileSystemWorkspacePaths.GetSafePath(requestRoot, request.RelativePath);
        Stream stream = new FileStream(
            requestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult(stream);
    }

    private IReadOnlyList<string> EnumerateEligibleFiles(string sourceRoot) =>
        Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".headers.json", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).StartsWith("_", StringComparison.Ordinal))
            .Where(path => eligibleExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetRelativePath(sourceRoot, path), StringComparer.OrdinalIgnoreCase)
            .ToList();

    private string GetSafeSourceRelativePath(string sourceRoot, string sourceFilePath)
    {
        string fullSourceRoot = Path.GetFullPath(sourceRoot);
        string fullSourceFilePath = Path.GetFullPath(sourceFilePath);
        if (!FileSystemWorkspacePaths.IsPathInsideDirectory(fullSourceFilePath, fullSourceRoot))
        {
            throw new InvalidOperationException($"Request file '{sourceFilePath}' resolves outside the source directory.");
        }

        return new RequestItem(Path.GetRelativePath(fullSourceRoot, fullSourceFilePath)).RelativePath;
    }

    private string GetContentType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".xml" => "application/xml",
            _ => "text/plain",
        };

    private async Task SaveManifestAsync(
        string batchRoot,
        RequestBatchManifest manifest,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(batchRoot);
        await using FileStream stream = File.Create(Path.Combine(batchRoot, "manifest.json"));
        await JsonSerializer
            .SerializeAsync(stream, ToDto(manifest), jsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private string GetBatchRoot(RequestBatchReference batchReference) =>
        FileSystemWorkspacePaths.GetSafePath(
            workspaceRoot,
            FileSystemWorkspacePaths.ToLogicalPath("request-batches", batchReference.Value));

    private string GetManifestPath(RequestBatchReference batchReference) =>
        Path.Combine(GetBatchRoot(batchReference), "manifest.json");

    private RequestBatchManifestDto ToDto(RequestBatchManifest manifest) =>
        new RequestBatchManifestDto
        {
            BatchReference = manifest.BatchReference.Value,
            CreatedAt = manifest.CreatedAt,
            Requests = manifest.Requests.Select(ToDto).ToList(),
        };

    private RequestItemDto ToDto(RequestItem request) =>
        new RequestItemDto
        {
            RelativePath = request.RelativePath,
            ContentType = request.ContentType,
            ContentLength = request.ContentLength,
            Headers = request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            HeadersA = request.HeadersA.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            HeadersB = request.HeadersB.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
        };

    private RequestBatchManifest FromDto(RequestBatchManifestDto dto) =>
        new RequestBatchManifest(
            new RequestBatchReference(dto.BatchReference),
            dto.Requests.Select(FromDto),
            dto.CreatedAt);

    private RequestItem FromDto(RequestItemDto dto) =>
        new RequestItem(
            dto.RelativePath,
            dto.ContentType,
            dto.ContentLength,
            dto.Headers,
            dto.HeadersA,
            dto.HeadersB);

    private sealed class RequestBatchManifestDto
    {
        public string BatchReference { get; init; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }

        public List<RequestItemDto> Requests { get; init; } = new List<RequestItemDto>();
    }

    private sealed class RequestItemDto
    {
        public string RelativePath { get; init; } = string.Empty;

        public string ContentType { get; init; } = "text/plain";

        public long ContentLength { get; init; }

        public Dictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> HeadersA { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> HeadersB { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
