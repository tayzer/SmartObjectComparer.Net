using System.Text;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;

namespace ParityBench.NET.Engine.Comparers;

public sealed class RawTextResponseComparer : IResponseComparer
{
    private const int MaxPreviewBytes = 5 * 1024;
    private readonly IRunArtifactStore artifactStore;
    private readonly IResponseComparer innerComparer;

    public RawTextResponseComparer(
        IRunArtifactStore artifactStore,
        IResponseComparer innerComparer)
    {
        this.artifactStore = artifactStore;
        this.innerComparer = innerComparer;
    }

    public async Task<RequestPairResult> CompareAsync(
        RequestItem request,
        RunOptions options,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage,
        CancellationToken cancellationToken = default)
        => await CompareAsync(request, options, responseA, responseB, errorMessage, null, cancellationToken).ConfigureAwait(false);

    internal async Task<RequestPairResult> CompareAsync(
        RequestItem request,
        RunOptions options,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage,
        DetailedCompareMetricsCollector? timing,
        CancellationToken cancellationToken = default)
    {
        if (!CanRawTextCompare(responseA, responseB, errorMessage))
        {
            return await CompareInnerAsync(request, options, responseA, responseB, errorMessage, timing, cancellationToken).ConfigureAwait(false);
        }

        ResponseArtifactMetadata leftResponse = responseA!;
        ResponseArtifactMetadata rightResponse = responseB!;
        if (IsSuccessStatusCode(leftResponse.StatusCode) && IsSuccessStatusCode(rightResponse.StatusCode))
        {
            return await CompareInnerAsync(request, options, leftResponse, rightResponse, errorMessage, timing, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            RawPreview previewA = await LoadPreviewAsync(leftResponse.Artifact, cancellationToken).ConfigureAwait(false);
            RawPreview previewB = await LoadPreviewAsync(rightResponse.Artifact, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ComparisonDifference> differences = BuildDifferences(leftResponse, rightResponse, previewA, previewB, options.Comparison.MaxDifferences);

            return RequestPairResult.FromRawTextComparison(
                request,
                leftResponse,
                rightResponse,
                differences,
                BuildOutcomeMessage(leftResponse.StatusCode, rightResponse.StatusCode));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RequestPairResult(
                request.RelativePath,
                RequestPairOutcome.ExecutionFailed,
                responseA,
                responseB,
                ex.Message);
        }
    }

    private async Task<RawPreview> LoadPreviewAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await artifactStore.OpenReadAsync(artifact, cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[MaxPreviewBytes + 1];
        int offset = 0;
        while (offset < buffer.Length)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            offset += bytesRead;
        }

        bool isTruncated = offset > MaxPreviewBytes;
        int previewLength = Math.Min(offset, MaxPreviewBytes);
        using MemoryStream previewStream = new MemoryStream(buffer, 0, previewLength, writable: false);
        using StreamReader reader = new StreamReader(previewStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        return new RawPreview(NormalizeLineEndings(text), isTruncated);
    }

    private Task<RequestPairResult> CompareInnerAsync(
        RequestItem request,
        RunOptions options,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage,
        DetailedCompareMetricsCollector? timing,
        CancellationToken cancellationToken) =>
        innerComparer is CompareNetObjectsResponseComparer structured
            ? structured.CompareAsync(request, options, responseA, responseB, errorMessage, timing, cancellationToken)
            : innerComparer.CompareAsync(request, options, responseA, responseB, errorMessage, cancellationToken);

    private static IReadOnlyList<ComparisonDifference> BuildDifferences(
        ResponseArtifactMetadata responseA,
        ResponseArtifactMetadata responseB,
        RawPreview previewA,
        RawPreview previewB,
        int maxDifferences)
    {
        List<ComparisonDifference> differences = new List<ComparisonDifference>();
        if (IsSuccessStatusCode(responseA.StatusCode) != IsSuccessStatusCode(responseB.StatusCode))
        {
            differences.Add(new ComparisonDifference(
                "HttpStatus",
                responseA.StatusCode.ToString(),
                responseB.StatusCode.ToString(),
                $"Endpoint A returned {responseA.StatusCode}; endpoint B returned {responseB.StatusCode}."));
        }

        AddLineDifferences(differences, previewA.Text, previewB.Text, maxDifferences);
        if ((previewA.IsTruncated || previewB.IsTruncated) && differences.Count < maxDifferences)
        {
            differences.Add(new ComparisonDifference(
                "BodyPreview",
                previewA.IsTruncated ? "Truncated" : "Complete",
                previewB.IsTruncated ? "Truncated" : "Complete",
                $"Raw body preview was truncated to {MaxPreviewBytes} bytes per response."));
        }

        return differences.Take(maxDifferences).ToList();
    }

    private static void AddLineDifferences(
        List<ComparisonDifference> differences,
        string textA,
        string textB,
        int maxDifferences)
    {
        if (differences.Count >= maxDifferences)
        {
            return;
        }

        string[] linesA = textA.Split('\n');
        string[] linesB = textB.Split('\n');
        int lineCount = Math.Max(linesA.Length, linesB.Length);
        for (int index = 0; index < lineCount && differences.Count < maxDifferences; index++)
        {
            string? lineA = index < linesA.Length ? linesA[index] : null;
            string? lineB = index < linesB.Length ? linesB[index] : null;
            if (string.Equals(lineA, lineB, StringComparison.Ordinal))
            {
                continue;
            }

            differences.Add(new ComparisonDifference(
                $"Body.Line[{index + 1}]",
                lineA,
                lineB,
                $"Raw response body line {index + 1} differs."));
        }
    }

    private static bool CanRawTextCompare(
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage) =>
        string.IsNullOrWhiteSpace(errorMessage)
        && responseA is not null
        && responseB is not null;

    private static bool IsSuccessStatusCode(int statusCode) => statusCode is >= 200 and <= 299;

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string BuildOutcomeMessage(int statusCodeA, int statusCodeB) =>
        IsSuccessStatusCode(statusCodeA) != IsSuccessStatusCode(statusCodeB)
            ? $"Endpoint A returned {statusCodeA} and endpoint B returned {statusCodeB}."
            : $"Both endpoints returned non-success status codes: A={statusCodeA}, B={statusCodeB}.";

    private sealed record RawPreview(string Text, bool IsTruncated);
}

