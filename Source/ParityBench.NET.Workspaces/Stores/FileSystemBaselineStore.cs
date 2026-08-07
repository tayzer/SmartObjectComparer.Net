using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Domain;
using ParityBench.NET.Domain.Baselines;

namespace ParityBench.NET.Workspaces;

/// <summary>
/// Stores baseline packages under <c>&lt;workspace&gt;/baselines/&lt;id&gt;/v&lt;n&gt;</c>,
/// one directory per captured version.
/// </summary>
/// <remarks>
/// A completed version is never rewritten. Capturing again under the same name
/// reserves the next version number, so an expected result someone signed off on
/// cannot be silently replaced by a later capture against different data.
/// A version is only complete once <c>baseline.json</c> exists; everything before
/// that is an in-progress capture and is invisible to the library.
/// </remarks>
public sealed class FileSystemBaselineStore : IBaselineStore
{
    private const string ManifestFileName = "baseline.json";
    private const string CaptureFileName = "capture.json";
    private const string ScenarioLogFileName = "scenarios.ndjson";
    private const string RequestsFolder = "requests";
    private const string RawResponsesFolder = "responses/raw";
    private const string CanonicalResponsesFolder = "responses/canonical";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // The scenario log is one object per line, so it stays appendable.
    private static readonly JsonSerializerOptions ScenarioLogOptions = new JsonSerializerOptions(JsonOptions)
    {
        WriteIndented = false,
    };

    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    private readonly string baselinesRoot;

    public FileSystemBaselineStore(string workspaceRoot)
    {
        string normalizedWorkspaceRoot = FileSystemWorkspacePaths.NormalizeRoot(workspaceRoot);
        baselinesRoot = FileSystemWorkspacePaths.GetSafePath(
            normalizedWorkspaceRoot,
            FileSystemWorkspacePaths.ToLogicalPath("baselines"));
    }

    public async Task<BaselinePackageManifest> BeginCaptureAsync(
        BaselineCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        BaselineId id = BaselineId.FromName(request.Name);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string packageRoot = GetPackageRoot(id);
            Directory.CreateDirectory(packageRoot);

            int version = GetNextVersion(packageRoot);
            string versionRoot = GetVersionRoot(id, version);
            Directory.CreateDirectory(versionRoot);

            BaselinePackageManifest manifest = new BaselinePackageManifest(
                id,
                request.Name,
                version,
                request.CapturedAt,
                request.CapturedFromRunId,
                request.CaptureEndpoint,
                request.PluginId,
                request.ComparisonId,
                request.PluginVersion,
                request.EnvironmentName,
                request.CaptureEndpointLabel,
                request.ComparisonRulesSnapshotHash,
                request.ComparisonOptions,
                ToolVersion.Current,
                Environment.UserName,
                Environment.MachineName);

            await WriteJsonAsync(Path.Combine(versionRoot, CaptureFileName), BaselineManifestDto.FromManifest(manifest), cancellationToken)
                .ConfigureAwait(false);

            return manifest;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<BaselineScenarioEntry> AppendScenarioAsync(
        BaselineId id,
        int version,
        BaselineScenarioCapture scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        string versionRoot = GetVersionRoot(id, version);
        if (!Directory.Exists(versionRoot))
        {
            throw new InvalidOperationException($"Baseline '{id.Value}' v{version} has no capture in progress.");
        }

        // The bodies are copied outside the gate: they are the slow part, they write to
        // per-scenario paths, and only the shared scenario log needs serializing.
        (string canonicalSha, long canonicalLength) = await CopyAndHashAsync(
            scenario.OpenCanonicalBodyAsync,
            GetPayloadPath(versionRoot, CanonicalResponsesFolder, ToCanonicalRelativePath(scenario.RelativePath)),
            cancellationToken).ConfigureAwait(false);

        (string requestSha, long requestLength) = await CopyAndHashAsync(
            scenario.OpenRequestBodyAsync,
            GetPayloadPath(versionRoot, RequestsFolder, scenario.RelativePath),
            cancellationToken).ConfigureAwait(false);
        _ = requestSha;

        string? rawSha = null;
        long rawLength = 0;
        if (scenario.OpenRawBodyAsync is not null)
        {
            (rawSha, rawLength) = await CopyAndHashAsync(
                scenario.OpenRawBodyAsync,
                GetPayloadPath(versionRoot, RawResponsesFolder, scenario.RelativePath),
                cancellationToken).ConfigureAwait(false);
        }

        BaselineScenarioEntry entry = new BaselineScenarioEntry(
            scenario.RelativePath,
            scenario.RequestContentType,
            requestLength,
            scenario.StatusCode,
            scenario.ResponseContentType,
            canonicalSha,
            canonicalLength,
            rawSha,
            rawLength,
            scenario.RequestHeaders);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string line = JsonSerializer.Serialize(BaselineScenarioEntryDto.FromEntry(entry), ScenarioLogOptions);
            await File.AppendAllTextAsync(
                Path.Combine(versionRoot, ScenarioLogFileName),
                line + Environment.NewLine,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }

        return entry;
    }

    public async Task<BaselinePackageManifest> CompleteCaptureAsync(
        BaselineId id,
        int version,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string versionRoot = GetVersionRoot(id, version);
            string capturePath = Path.Combine(versionRoot, CaptureFileName);
            if (!File.Exists(capturePath))
            {
                throw new InvalidOperationException($"Baseline '{id.Value}' v{version} has no capture in progress.");
            }

            BaselineManifestDto captureDto = await ReadJsonAsync<BaselineManifestDto>(capturePath, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Baseline '{id.Value}' v{version} has an unreadable capture manifest.");

            captureDto.Scenarios = await ReadScenarioLogAsync(versionRoot, cancellationToken).ConfigureAwait(false);

            // The manifest is written last: its presence is what makes the version
            // visible to the library, so a crashed capture is never listed as usable.
            await WriteJsonAsync(Path.Combine(versionRoot, ManifestFileName), captureDto, cancellationToken).ConfigureAwait(false);

            File.Delete(capturePath);
            TryDeleteFile(Path.Combine(versionRoot, ScenarioLogFileName));

            return captureDto.ToManifest();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AbandonCaptureAsync(
        BaselineId id,
        int version,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string versionRoot = GetVersionRoot(id, version);
            if (!Directory.Exists(versionRoot) || File.Exists(Path.Combine(versionRoot, ManifestFileName)))
            {
                // Never touch a completed version.
                return;
            }

            TryDeleteDirectory(versionRoot);
            TryDeletePackageRootIfEmpty(id);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<BaselineSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(baselinesRoot))
            {
                return Array.Empty<BaselineSummary>();
            }

            List<BaselineSummary> summaries = new List<BaselineSummary>();
            foreach (string packageRoot in Directory.EnumerateDirectories(baselinesRoot))
            {
                foreach (string versionRoot in Directory.EnumerateDirectories(packageRoot))
                {
                    string manifestPath = Path.Combine(versionRoot, ManifestFileName);
                    if (!File.Exists(manifestPath))
                    {
                        continue;
                    }

                    BaselineManifestDto? dto = await TryReadJsonAsync<BaselineManifestDto>(manifestPath, cancellationToken)
                        .ConfigureAwait(false);
                    if (dto is null)
                    {
                        // One hand-edited or truncated package should not hide the rest
                        // of the library.
                        continue;
                    }

                    try
                    {
                        summaries.Add(BaselineSummary.FromManifest(dto.ToManifest(), GetDirectorySize(versionRoot)));
                    }
                    catch (Exception ex) when (ex is ArgumentException or UriFormatException or InvalidOperationException)
                    {
                    }
                }
            }

            return summaries
                .OrderByDescending(summary => summary.CapturedAt)
                .ThenBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<BaselinePackageManifest?> LoadManifestAsync(
        BaselineId id,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int? resolvedVersion = version ?? GetLatestCompletedVersion(id);
            if (resolvedVersion is null)
            {
                return null;
            }

            string manifestPath = Path.Combine(GetVersionRoot(id, resolvedVersion.Value), ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            BaselineManifestDto? dto = await TryReadJsonAsync<BaselineManifestDto>(manifestPath, cancellationToken)
                .ConfigureAwait(false);
            return dto?.ToManifest();
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<Stream> OpenCanonicalAsync(
        BaselineId id,
        int version,
        string relativePath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OpenRead(
            GetPayloadPath(GetVersionRoot(id, version), CanonicalResponsesFolder, ToCanonicalRelativePath(relativePath))));

    public Task<Stream> OpenRawAsync(
        BaselineId id,
        int version,
        string relativePath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OpenRead(GetPayloadPath(GetVersionRoot(id, version), RawResponsesFolder, relativePath)));

    public async Task<int> ExportRequestsToDirectoryAsync(
        BaselineId id,
        int version,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("Target directory must not be empty.", nameof(targetDirectory));
        }

        string requestsRoot = Path.Combine(GetVersionRoot(id, version), ToPlatformPath(RequestsFolder));
        if (!Directory.Exists(requestsRoot))
        {
            throw new InvalidOperationException($"Baseline '{id.Value}' v{version} has no stored requests.");
        }

        string destinationRoot = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(destinationRoot);

        int copied = 0;
        foreach (string sourcePath in Directory.EnumerateFiles(requestsRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = Path.GetRelativePath(requestsRoot, sourcePath);
            string destinationPath = FileSystemWorkspacePaths.GetSafePath(
                destinationRoot,
                FileSystemWorkspacePaths.ToLogicalPath(relativePath));

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationRoot);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            copied++;
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return copied;
    }

    public async Task ExportAsync(
        BaselineId id,
        int version,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Archive path must not be empty.", nameof(archivePath));
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string versionRoot = GetVersionRoot(id, version);
            if (!File.Exists(Path.Combine(versionRoot, ManifestFileName)))
            {
                throw new InvalidOperationException($"Baseline '{id.Value}' v{version} was not found.");
            }

            string fullArchivePath = Path.GetFullPath(archivePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullArchivePath) ?? ".");
            if (File.Exists(fullArchivePath))
            {
                File.Delete(fullArchivePath);
            }

            ZipFile.CreateFromDirectory(versionRoot, fullArchivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<BaselinePackageManifest> ImportAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Archive path must not be empty.", nameof(archivePath));
        }

        string fullArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullArchivePath))
        {
            throw new FileNotFoundException($"Baseline archive was not found: {fullArchivePath}", fullArchivePath);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? versionRoot = null;
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(fullArchivePath);
            ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestFileName)
                ?? throw new InvalidOperationException($"'{fullArchivePath}' is not a baseline package: it has no {ManifestFileName}.");

            BaselineManifestDto dto;
            await using (Stream manifestStream = manifestEntry.Open())
            {
                dto = await JsonSerializer.DeserializeAsync<BaselineManifestDto>(manifestStream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"'{fullArchivePath}' has an unreadable {ManifestFileName}.");
            }

            if (dto.SchemaVersion > BaselinePackageManifest.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"'{fullArchivePath}' was written by a newer version of ParityBench (package schema {dto.SchemaVersion}).");
            }

            BaselineId id = string.IsNullOrWhiteSpace(dto.Id) ? BaselineId.FromName(dto.Name) : new BaselineId(dto.Id);
            string packageRoot = GetPackageRoot(id);
            Directory.CreateDirectory(packageRoot);

            // Imports always land on a fresh version, so bringing a package back from
            // another machine can never overwrite one already here.
            int version = GetNextVersion(packageRoot);
            versionRoot = GetVersionRoot(id, version);
            Directory.CreateDirectory(versionRoot);
            string fullVersionRoot = Path.GetFullPath(versionRoot + Path.DirectorySeparatorChar);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue;
                }

                if (string.Equals(entry.FullName, ManifestFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.FullName, CaptureFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.FullName, ScenarioLogFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Archives are untrusted input: an entry naming ..\..\ must not be able
                // to write outside the package directory.
                //
                // Defence-in-depth:
                //   1. Reject the entry name up-front if it is rooted or contains any
                //      component that is or normalises to ".." (including Windows variants
                //      such as ".. " or "...") or is empty (double-slash).
                //   2. Resolve the full destination path and confirm it remains inside the
                //      expected directory, guarding against any OS-level normalisation that
                //      slipped past the name check.
                //   3. Extract via a FileStream to the pre-validated, fully-resolved path
                //      so that no tainted entry name is passed to a file-system API.
                string entryName = entry.FullName;
                if (Path.IsPathRooted(entryName)
                    || entryName.Split('/', '\\').Any(
                        part => string.IsNullOrEmpty(part)
                            || part.TrimEnd('.', ' ') is "" or "."))
                {
                    throw new InvalidOperationException(
                        $"'{fullArchivePath}' contains an entry with an invalid path: '{entryName}'.");
                }

                string destinationPath = Path.GetFullPath(Path.Combine(versionRoot, entryName));
                if (!destinationPath.StartsWith(fullVersionRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"'{fullArchivePath}' contains an entry that resolves outside the package: '{entryName}'.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? versionRoot);

                // Use a FileStream with the pre-validated path rather than
                // entry.ExtractToFile so that no tainted entry name reaches a
                // file-system API after the guards above.
                using (Stream source = entry.Open())
                using (FileStream destination = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                }
            }

            dto.Id = id.Value;
            dto.Version = version;
            await WriteJsonAsync(Path.Combine(versionRoot, ManifestFileName), dto, cancellationToken).ConfigureAwait(false);

            return dto.ToManifest();
        }
        catch
        {
            if (versionRoot is not null)
            {
                TryDeleteDirectory(versionRoot);
            }

            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(
        BaselineId id,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (version is null)
            {
                TryDeleteDirectory(GetPackageRoot(id));
                return;
            }

            TryDeleteDirectory(GetVersionRoot(id, version.Value));
            TryDeletePackageRootIfEmpty(id);
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetPackageRoot(BaselineId id) =>
        FileSystemWorkspacePaths.GetSafePath(baselinesRoot, FileSystemWorkspacePaths.ToLogicalPath(id.Value));

    private string GetVersionRoot(BaselineId id, int version) =>
        FileSystemWorkspacePaths.GetSafePath(
            baselinesRoot,
            FileSystemWorkspacePaths.ToLogicalPath(id.Value, $"v{version}"));

    private int? GetLatestCompletedVersion(BaselineId id)
    {
        string packageRoot = GetPackageRoot(id);
        if (!Directory.Exists(packageRoot))
        {
            return null;
        }

        int? latest = null;
        foreach (string versionRoot in Directory.EnumerateDirectories(packageRoot))
        {
            if (!File.Exists(Path.Combine(versionRoot, ManifestFileName)))
            {
                continue;
            }

            if (TryParseVersion(Path.GetFileName(versionRoot), out int version) && (latest is null || version > latest))
            {
                latest = version;
            }
        }

        return latest;
    }

    // Counts in-progress versions too, so two captures started back to back never
    // collide on the same directory.
    private static int GetNextVersion(string packageRoot)
    {
        int highest = 0;
        foreach (string versionRoot in Directory.EnumerateDirectories(packageRoot))
        {
            if (TryParseVersion(Path.GetFileName(versionRoot), out int version) && version > highest)
            {
                highest = version;
            }
        }

        return highest + 1;
    }

    private static bool TryParseVersion(string directoryName, out int version)
    {
        version = 0;
        return directoryName.StartsWith('v')
            && int.TryParse(directoryName.AsSpan(1), out version)
            && version > 0;
    }

    private static string GetPayloadPath(string versionRoot, string folder, string relativePath) =>
        FileSystemWorkspacePaths.GetSafePath(
            versionRoot,
            FileSystemWorkspacePaths.ToLogicalPath(folder, relativePath));

    // The stored comparison model is JSON whatever the original response was, so the
    // suffix keeps a browsed package honest about what each file holds.
    private static string ToCanonicalRelativePath(string relativePath) => relativePath + ".json";

    private static string ToPlatformPath(string logicalPath) =>
        logicalPath.Replace('/', Path.DirectorySeparatorChar);

    private static Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task<(string Sha256, long Length)> CopyAndHashAsync(
        Func<CancellationToken, Task<Stream>> openSourceAsync,
        string targetPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ".");

        await using Stream source = await openSourceAsync(cancellationToken).ConfigureAwait(false);

        byte[] buffer = new byte[81920];
        long length = 0;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (FileStream target = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            while (true)
            {
                int bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer.AsSpan(0, bytesRead));
                length += bytesRead;
            }
        }

        return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), length);
    }

    private static async Task<List<BaselineScenarioEntryDto>> ReadScenarioLogAsync(
        string versionRoot,
        CancellationToken cancellationToken)
    {
        string logPath = Path.Combine(versionRoot, ScenarioLogFileName);
        if (!File.Exists(logPath))
        {
            return new List<BaselineScenarioEntryDto>();
        }

        List<BaselineScenarioEntryDto> scenarios = new List<BaselineScenarioEntryDto>();
        foreach (string line in await File.ReadAllLinesAsync(logPath, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            BaselineScenarioEntryDto? scenario = JsonSerializer.Deserialize<BaselineScenarioEntryDto>(line, ScenarioLogOptions);
            if (scenario is not null)
            {
                scenarios.Add(scenario);
            }
        }

        return scenarios
            .OrderBy(scenario => scenario.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        string tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(value, JsonOptions), cancellationToken).ConfigureAwait(false);
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> TryReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadJsonAsync<T>(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException)
        {
            return default;
        }
    }

    private static long GetDirectorySize(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);

    private void TryDeletePackageRootIfEmpty(BaselineId id)
    {
        string packageRoot = GetPackageRoot(id);
        if (Directory.Exists(packageRoot) && !Directory.EnumerateFileSystemEntries(packageRoot).Any())
        {
            TryDeleteDirectory(packageRoot);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
