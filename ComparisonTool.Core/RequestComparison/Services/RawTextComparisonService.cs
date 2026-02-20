using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Service for comparing HTTP response bodies as raw text when domain-model comparison
/// is not appropriate (non-success status codes, content-type mismatches, etc.).
/// </summary>
public class RawTextComparisonService
{
    private readonly ILogger<RawTextComparisonService> logger;

    /// <summary>
    /// Maximum number of bytes to read from each response body for text comparison.
    /// Bodies larger than this are truncated with a notice.
    /// </summary>
    private const int MaxBodyBytes = 5 * 1024; // 5 KB

    /// <summary>
    /// Maximum number of diff lines to report per file pair.
    /// </summary>
    private const int MaxDiffLines = 100;

    public RawTextComparisonService(ILogger<RawTextComparisonService> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Compares a classified non-success execution result pair as raw text.
    /// Returns a <see cref="FilePairComparisonResult"/> populated with raw text differences
    /// and HTTP status code metadata.
    /// </summary>
    public async Task<FilePairComparisonResult> CompareRawAsync(
        ClassifiedExecutionResult classified,
        CancellationToken cancellationToken = default)
    {
        var exec = classified.Execution;
        var fileName = Path.GetFileName(exec.Request.RelativePath);

        var pairResult = new FilePairComparisonResult
        {
            File1Name = fileName,
            File2Name = fileName,
            RequestRelativePath = exec.Request.RelativePath,
            File1Path = exec.ResponsePathA,
            File2Path = exec.ResponsePathB,
            HttpStatusCodeA = exec.StatusCodeA,
            HttpStatusCodeB = exec.StatusCodeB,
            PairOutcome = classified.Outcome,
            RawTextDifferences = new List<RawTextDifference>(),
        };

        // If one or both failed with exceptions, there may be no response files at all
        if (classified.Outcome == RequestPairOutcome.OneOrBothFailed)
        {
            pairResult.ErrorMessage = exec.ErrorMessage ?? "One or both requests failed";
            pairResult.ErrorType = "HttpRequestException";
            return pairResult;
        }

        // Add status code difference as the first entry when status codes differ
        if (exec.StatusCodeA != exec.StatusCodeB)
        {
            pairResult.RawTextDifferences.Add(new RawTextDifference
            {
                Type = RawTextDifferenceType.StatusCodeDifference,
                TextA = $"HTTP {exec.StatusCodeA}",
                TextB = $"HTTP {exec.StatusCodeB}",
                Description = $"Status code mismatch: A returned {exec.StatusCodeA}, B returned {exec.StatusCodeB}",
            });
        }

        // Read response bodies and perform line-by-line comparison
        var bodyA = await ReadResponseBodyAsync(exec.ResponsePathA, cancellationToken).ConfigureAwait(false);
        var bodyB = await ReadResponseBodyAsync(exec.ResponsePathB, cancellationToken).ConfigureAwait(false);

        var textDiffs = ComputeLineDifferences(bodyA.lines, bodyB.lines);
        pairResult.RawTextDifferences.AddRange(textDiffs);

        // If bodies were truncated, add a notice
        if (bodyA.wasTruncated || bodyB.wasTruncated)
        {
            pairResult.RawTextDifferences.Add(new RawTextDifference
            {
                Type = RawTextDifferenceType.Modified,
                Description = $"Response body truncated to {MaxBodyBytes / 1024} KB for comparison. Full bodies saved to disk.",
            });
        }

        var bodiesAreEqual = textDiffs.Count == 0;
        var hasStatusMismatch = exec.StatusCodeA != exec.StatusCodeB;
        var isTruncated = bodyA.wasTruncated || bodyB.wasTruncated;

        pairResult.Summary = new DifferenceSummary
        {
            AreEqual = classified.Outcome == RequestPairOutcome.BothNonSuccess
                && !hasStatusMismatch
                && bodiesAreEqual
                && !isTruncated,
            TotalDifferenceCount = pairResult.RawTextDifferences.Count,
        };

        logger.LogDebug(
            "Raw text comparison for {FileName}: {DiffCount} differences, A={StatusA} B={StatusB}",
            fileName,
            pairResult.RawTextDifferences.Count,
            exec.StatusCodeA,
            exec.StatusCodeB);

        return pairResult;
    }

    /// <summary>
    /// Batch compares all classified non-success results.
    /// </summary>
    public async Task<IReadOnlyList<FilePairComparisonResult>> CompareAllRawAsync(
        IEnumerable<ClassifiedExecutionResult> classifiedResults,
        IProgress<(int Completed, int Total, string Message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var items = classifiedResults.ToList();
        var results = new List<FilePairComparisonResult>(items.Count);
        var completed = 0;

        foreach (var classified in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await CompareRawAsync(classified, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            completed++;
            if (completed % Math.Max(1, items.Count / 20) == 0 || completed == items.Count)
            {
                progress?.Report((completed, items.Count,
                    $"Raw text comparison: {completed}/{items.Count}"));
            }
        }

        return results;
    }

    /// <summary>
    /// Compares two files on disk as raw text. Used as a fallback when domain-model
    /// deserialization fails for file/folder comparison.
    /// </summary>
    /// <param name="file1Path">Full path to the first file (may be null or missing).</param>
    /// <param name="file2Path">Full path to the second file (may be null or missing).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of raw text differences between the two files.</returns>
    public async Task<List<RawTextDifference>> CompareFilesRawAsync(
        string? file1Path,
        string? file2Path,
        CancellationToken cancellationToken = default)
    {
        var diffs = new List<RawTextDifference>();

        var bodyA = await ReadResponseBodyAsync(file1Path, cancellationToken).ConfigureAwait(false);
        var bodyB = await ReadResponseBodyAsync(file2Path, cancellationToken).ConfigureAwait(false);

        // If both files are missing/empty, nothing to diff
        if (bodyA.lines.Length == 0 && bodyB.lines.Length == 0)
        {
            return diffs;
        }

        var textDiffs = ComputeLineDifferences(bodyA.lines, bodyB.lines);
        diffs.AddRange(textDiffs);

        if (bodyA.wasTruncated || bodyB.wasTruncated)
        {
            diffs.Add(new RawTextDifference
            {
                Type = RawTextDifferenceType.Modified,
                Description = $"File content truncated to {MaxBodyBytes / 1024} KB for comparison.",
            });
        }

        logger.LogDebug(
            "Raw file comparison: {File1} vs {File2}: {DiffCount} differences",
            file1Path ?? "(null)",
            file2Path ?? "(null)",
            diffs.Count);

        return diffs;
    }

    /// <summary>
    /// Reads a response body from disk, truncating to <see cref="MaxBodyBytes"/>.
    /// </summary>
    private static async Task<(string[] lines, bool wasTruncated)> ReadResponseBodyAsync(
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return (Array.Empty<string>(), false);
        }

        var fileInfo = new FileInfo(filePath);
        var wasTruncated = fileInfo.Length > MaxBodyBytes;
        var bytesToRead = (int)Math.Min(fileInfo.Length, MaxBodyBytes);

        var buffer = new byte[bytesToRead];
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken).ConfigureAwait(false);
        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
        var lines = text.Split('\n');

        return (lines, wasTruncated);
    }

    /// <summary>
    /// Computes line-by-line differences between two text bodies using a simple LCS-based approach.
    /// </summary>
    private static List<RawTextDifference> ComputeLineDifferences(string[] linesA, string[] linesB)
    {
        var diffs = new List<RawTextDifference>();

        var maxLines = Math.Max(linesA.Length, linesB.Length);
        var indexA = 0;
        var indexB = 0;

        while (indexA < linesA.Length && indexB < linesB.Length && diffs.Count < MaxDiffLines)
        {
            var lineA = linesA[indexA].TrimEnd('\r');
            var lineB = linesB[indexB].TrimEnd('\r');

            if (string.Equals(lineA, lineB, StringComparison.Ordinal))
            {
                indexA++;
                indexB++;
                continue;
            }

            // Try to find lineA ahead in B (deletion from A or addition in B)
            var foundInB = FindLineAhead(linesA[indexA], linesB, indexB, 5);
            var foundInA = FindLineAhead(linesB[indexB], linesA, indexA, 5);

            if (foundInB >= 0 && (foundInA < 0 || foundInB - indexB <= foundInA - indexA))
            {
                // Lines in B before foundInB are additions (only in B)
                for (var i = indexB; i < foundInB && diffs.Count < MaxDiffLines; i++)
                {
                    diffs.Add(new RawTextDifference
                    {
                        Type = RawTextDifferenceType.OnlyInB,
                        LineNumberB = i + 1,
                        TextB = linesB[i].TrimEnd('\r'),
                        Description = $"Line only in B (line {i + 1})",
                    });
                }

                indexB = foundInB;
            }
            else if (foundInA >= 0)
            {
                // Lines in A before foundInA are deletions (only in A)
                for (var i = indexA; i < foundInA && diffs.Count < MaxDiffLines; i++)
                {
                    diffs.Add(new RawTextDifference
                    {
                        Type = RawTextDifferenceType.OnlyInA,
                        LineNumberA = i + 1,
                        TextA = linesA[i].TrimEnd('\r'),
                        Description = $"Line only in A (line {i + 1})",
                    });
                }

                indexA = foundInA;
            }
            else
            {
                // Lines differ at this position — record as modification
                diffs.Add(new RawTextDifference
                {
                    Type = RawTextDifferenceType.Modified,
                    LineNumberA = indexA + 1,
                    LineNumberB = indexB + 1,
                    TextA = lineA,
                    TextB = lineB,
                    Description = $"Line modified at A:{indexA + 1} / B:{indexB + 1}",
                });

                indexA++;
                indexB++;
            }
        }

        // Remaining lines in A are only-in-A
        while (indexA < linesA.Length && diffs.Count < MaxDiffLines)
        {
            diffs.Add(new RawTextDifference
            {
                Type = RawTextDifferenceType.OnlyInA,
                LineNumberA = indexA + 1,
                TextA = linesA[indexA].TrimEnd('\r'),
                Description = $"Line only in A (line {indexA + 1})",
            });

            indexA++;
        }

        // Remaining lines in B are only-in-B
        while (indexB < linesB.Length && diffs.Count < MaxDiffLines)
        {
            diffs.Add(new RawTextDifference
            {
                Type = RawTextDifferenceType.OnlyInB,
                LineNumberB = indexB + 1,
                TextB = linesB[indexB].TrimEnd('\r'),
                Description = $"Line only in B (line {indexB + 1})",
            });

            indexB++;
        }

        return diffs;
    }

    /// <summary>
    /// Looks ahead in <paramref name="lines"/> from <paramref name="startIndex"/> up to
    /// <paramref name="lookAhead"/> positions to find a line matching <paramref name="targetLine"/>.
    /// Returns the index if found, or -1.
    /// </summary>
    private static int FindLineAhead(string targetLine, string[] lines, int startIndex, int lookAhead)
    {
        var trimmedTarget = targetLine.TrimEnd('\r');
        var end = Math.Min(startIndex + lookAhead, lines.Length);

        for (var i = startIndex + 1; i < end; i++)
        {
            if (string.Equals(lines[i].TrimEnd('\r'), trimmedTarget, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
