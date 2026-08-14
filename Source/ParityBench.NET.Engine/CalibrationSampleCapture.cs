using System.Text.Json;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine.Pipeline;

namespace ParityBench.NET.Engine;

internal static class CalibrationSampleCapture
{
    internal const string ManifestFileName = ".paritybench-calibration-sample.json";
    private const int MaximumPairs = 1000;

    public static async Task<string?> CaptureAsync(
        ComparisonRun run,
        IReadOnlyList<ComparedExecutionRecord> records,
        IRunArtifactStore artifactStore,
        string? configuredOutputDirectory,
        CancellationToken cancellationToken)
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string baseDirectory = string.IsNullOrWhiteSpace(configuredOutputDirectory)
            ? Path.Combine(
                string.IsNullOrWhiteSpace(localData) ? Path.GetTempPath() : localData,
                "ParityBench.NET",
                "CalibrationSamples")
            : Path.GetFullPath(configuredOutputDirectory);
        string sampleDirectory = Path.Combine(baseDirectory, run.Id.Value);
        if (Directory.Exists(sampleDirectory))
        {
            throw new InvalidOperationException(
                $"Calibration sample '{sampleDirectory}' already exists. Calibrate or remove that sample before capturing this run again.");
        }

        string temporaryDirectory = $"{sampleDirectory}.capturing-{Guid.NewGuid():N}";
        Directory.CreateDirectory(temporaryDirectory);
        int capturedPairs = 0;
        try
        {
            foreach (ComparedExecutionRecord record in records.OrderBy(item => item.ManifestOrdinal))
            {
                if (capturedPairs == MaximumPairs)
                {
                    break;
                }

                RequestPairResult result = record.Result;
                if (result.ResponseA is null || result.ResponseB is null)
                {
                    continue;
                }

                ArtifactReference rawA = RawArtifact(run.Id, EndpointSlot.A, result);
                ArtifactReference rawB = RawArtifact(run.Id, EndpointSlot.B, result);
                if (!await artifactStore.ExistsAsync(rawA, cancellationToken).ConfigureAwait(false)
                    || !await artifactStore.ExistsAsync(rawB, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                await CopyAsync(artifactStore, rawA, CapturedArtifactPath(temporaryDirectory, EndpointSlot.A, result.RelativePath), cancellationToken).ConfigureAwait(false);
                await CopyAsync(artifactStore, rawB, CapturedArtifactPath(temporaryDirectory, EndpointSlot.B, result.RelativePath), cancellationToken).ConfigureAwait(false);
                capturedPairs++;
            }

            CalibrationSampleManifest manifest = new(
                run.Id.Value,
                DateTimeOffset.UtcNow,
                capturedPairs,
                MaximumPairs);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, ManifestFileName),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(baseDirectory);
            Directory.Move(temporaryDirectory, sampleDirectory);
            return sampleDirectory;
        }
        catch
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }

            throw;
        }
    }

    internal static string CapturedArtifactPath(
        string sampleDirectory,
        EndpointSlot endpoint,
        string relativePath)
    {
        string normalized = new RequestItem(relativePath).RelativePath.Replace('/', Path.DirectorySeparatorChar);
        string endpointRoot = Path.GetFullPath(Path.Combine(sampleDirectory, endpoint.ToString()));
        string path = Path.GetFullPath(Path.Combine(endpointRoot, normalized));
        if (!path.StartsWith(endpointRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Calibration artifact path escaped its endpoint directory.");
        }

        return path;
    }

    private static ArtifactReference RawArtifact(RunId runId, EndpointSlot endpoint, RequestPairResult result) =>
        new($"runs/{runId.Value}/artifacts/{endpoint}/{result.RelativePath}",
            endpoint == EndpointSlot.A ? result.ResponseA!.ContentType : result.ResponseB!.ContentType);

    private static async Task CopyAsync(
        IRunArtifactStore store,
        ArtifactReference source,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using Stream input = await store.OpenReadAsync(source, cancellationToken).ConfigureAwait(false);
        await using FileStream output = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record CalibrationSampleManifest(
        string RunId,
        DateTimeOffset CreatedAt,
        int PairCount,
        int MaximumPairCount);
}
