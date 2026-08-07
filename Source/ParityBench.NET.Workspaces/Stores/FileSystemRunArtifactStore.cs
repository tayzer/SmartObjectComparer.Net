using System.Security.Cryptography;
using System.Threading;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Workspaces;

public sealed class FileSystemRunArtifactStore : IRunArtifactStore
{
    private readonly string workspaceRoot;
    private readonly WorkspaceArtifactUsageTracker usageTracker;

    public FileSystemRunArtifactStore(string workspaceRoot, WorkspaceArtifactUsageTracker? usageTracker = null)
    {
        this.workspaceRoot = FileSystemWorkspacePaths.NormalizeRoot(workspaceRoot);
        this.usageTracker = usageTracker ?? new WorkspaceArtifactUsageTracker();
    }

    public async Task<ResponseArtifactMetadata> SaveResponseAsync(
        RunId runId,
        EndpointSlot endpoint,
        RequestItem request,
        int statusCode,
        string? contentType,
        Stream body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(body);

        string artifactId = FileSystemWorkspacePaths.ToLogicalPath(
            "runs",
            runId.Value,
            "artifacts",
            endpoint.ToString(),
            request.RelativePath);
        string artifactPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, artifactId);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath) ?? workspaceRoot);

        long contentLength = 0;
        byte[] buffer = new byte[81920];

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (FileStream output = new FileStream(
            artifactPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            while (true)
            {
                int bytesRead = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer.AsSpan(0, bytesRead));
                contentLength += bytesRead;
            }
        }

        string sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        usageTracker.RecordDelta(contentLength);
        return new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference(artifactId, contentType),
            statusCode,
            contentType,
            contentLength,
            sha256);
    }

    public Task<Stream> OpenReadAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        string artifactPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, artifact.ArtifactId);
        Stream stream = new FileStream(
            artifactPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();

        string artifactPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, artifact.ArtifactId);
        return Task.FromResult(File.Exists(artifactPath));
    }

    public Task<long> GetArtifactSizeAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();

        string artifactPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, artifact.ArtifactId);
        if (!File.Exists(artifactPath))
        {
            return Task.FromResult(0L);
        }

        return Task.FromResult(new FileInfo(artifactPath).Length);
    }

    public Task<long> GetTotalArtifactBytesAsync(CancellationToken cancellationToken = default)
        => usageTracker.GetTotalAsync(ReconcileTotalArtifactBytesAsync, cancellationToken);

    private Task<long> ReconcileTotalArtifactBytesAsync(CancellationToken cancellationToken)
    {
        string artifactsRoot = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, FileSystemWorkspacePaths.ToLogicalPath("runs"));
        if (!Directory.Exists(artifactsRoot))
        {
            return Task.FromResult(0L);
        }

        // Runs off the caller's thread and stats files in parallel: this walks every run
        // in the workspace on every cleanup pass, so on a large workspace a sequential scan
        // blocks the calling thread for the whole run count, not just the current run.
        return Task.Run(
            () =>
            {
                long total = 0;
                Parallel.ForEach(
                    Directory.EnumerateFiles(artifactsRoot, "*", SearchOption.AllDirectories),
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = Environment.ProcessorCount * 4,
                    },
                    filePath =>
                    {
                        if (filePath.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                            || filePath.EndsWith(Path.DirectorySeparatorChar + "artifacts", StringComparison.OrdinalIgnoreCase))
                        {
                            Interlocked.Add(ref total, new FileInfo(filePath).Length);
                        }
                    });
                return total;
            },
            cancellationToken);
    }

    public Task<bool> DeleteIfExistsAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();

        string artifactPath = FileSystemWorkspacePaths.GetSafePath(workspaceRoot, artifact.ArtifactId);
        if (!File.Exists(artifactPath))
        {
            return Task.FromResult(false);
        }

        long length = new FileInfo(artifactPath).Length;
        File.Delete(artifactPath);
        usageTracker.RecordDelta(-length);
        return Task.FromResult(true);
    }
}
