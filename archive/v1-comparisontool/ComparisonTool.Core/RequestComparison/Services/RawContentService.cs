using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Service for loading raw file content on-demand for side-by-side comparison viewing.
/// Content is loaded lazily from disk when a user requests full file comparison,
/// rather than stored in memory for all file pairs.
/// </summary>
public class RawContentService
{
    /// <summary>
    /// Maximum number of bytes to read per file. Files larger than this are truncated.
    /// </summary>
    private const int MaxFileSizeBytes = 512 * 1024; // 512 KB

    private readonly ILogger<RawContentService> logger;
    private readonly IBundledRawContentAccessor? bundledRawContentAccessor;
    private readonly StructuredContentPruningService focusedPruningService;
    private readonly ConcurrentDictionary<string, RawContentResult> focusedContentCache =
        new ConcurrentDictionary<string, RawContentResult>(StringComparer.Ordinal);

    public RawContentService(
        ILogger<RawContentService> logger,
        IBundledRawContentAccessor? bundledRawContentAccessor = null,
        StructuredContentPruningService? focusedPruningService = null)
    {
        this.logger = logger;
        this.bundledRawContentAccessor = bundledRawContentAccessor;
        this.focusedPruningService = focusedPruningService
            ?? new StructuredContentPruningService(NullLogger<StructuredContentPruningService>.Instance);
    }

    /// <summary>
    /// Loads raw file content for both files in a comparison pair.
    /// Returns the content strings along with truncation flags.
    /// </summary>
    /// <param name="pair">The file pair with paths to both files.</param>
    /// <returns>A tuple of (contentA, contentB, isTruncatedA, isTruncatedB).</returns>
    public async Task<RawContentResult> LoadRawContentAsync(FilePairComparisonResult pair, RawContentVariant variant = RawContentVariant.Full)
    {
        ArgumentNullException.ThrowIfNull(pair);

        return variant == RawContentVariant.Focused
            ? await LoadFocusedRawContentAsync(pair).ConfigureAwait(false)
            : await LoadFullRawContentAsync(pair).ConfigureAwait(false);
    }

    private async Task<RawContentResult> LoadFullRawContentAsync(FilePairComparisonResult pair)
    {
        var result = new RawContentResult();

        if (pair.HasEmbeddedRawContent)
        {
            result.ContentA = StructuredTextDisplayFormatter.FormatForDisplay(pair.EmbeddedRawContentA, pair.ContentTypeA, pair.File1Name);
            result.ContentB = StructuredTextDisplayFormatter.FormatForDisplay(pair.EmbeddedRawContentB, pair.ContentTypeB, pair.File2Name);
            result.IsTruncatedA = pair.EmbeddedRawContentTruncatedA;
            result.IsTruncatedB = pair.EmbeddedRawContentTruncatedB;
            result.IsLoaded = true;
            return result;
        }

        if (this.bundledRawContentAccessor != null)
        {
            var bundledResult = await this.bundledRawContentAccessor.TryLoadAsync(pair, RawContentVariant.Full).ConfigureAwait(false);
            if (bundledResult != null)
            {
                return bundledResult;
            }
        }

        if (OperatingSystem.IsBrowser())
        {
            result.ErrorMessage = "Full File View is unavailable because this static report does not include embedded source content for the selected pair.";
            return result;
        }

        return await LoadRawContentFromPathsAsync(
            pair.File1Path,
            pair.File2Path,
            pair.ContentTypeA,
            pair.ContentTypeB,
            pair.File1Name,
            pair.File2Name).ConfigureAwait(false);
    }

    private async Task<RawContentResult> LoadFocusedRawContentAsync(FilePairComparisonResult pair)
    {
        if (this.bundledRawContentAccessor != null && !string.IsNullOrWhiteSpace(pair.FocusedBundledRawContentPath))
        {
            var bundledResult = await this.bundledRawContentAccessor.TryLoadAsync(pair, RawContentVariant.Focused).ConfigureAwait(false);
            if (bundledResult != null)
            {
                return bundledResult;
            }
        }

        if (!string.IsNullOrWhiteSpace(pair.FocusedFile1Path) && !string.IsNullOrWhiteSpace(pair.FocusedFile2Path))
        {
            return await LoadRawContentFromPathsAsync(
                pair.FocusedFile1Path,
                pair.FocusedFile2Path,
                pair.ContentTypeA,
                pair.ContentTypeB,
                pair.File1Name,
                pair.File2Name).ConfigureAwait(false);
        }

        var ignorePaths = GetFocusedIgnorePaths(pair);
        if (ignorePaths.Count == 0)
        {
            return new RawContentResult
            {
                ErrorMessage = "Focused raw content is unavailable because no ignore-complete rules were recorded for this pair.",
            };
        }

        var cacheKey = BuildFocusedCacheKey(pair, ignorePaths);
        if (focusedContentCache.TryGetValue(cacheKey, out var cachedResult))
        {
            return cachedResult;
        }

        var generatedResult = !OperatingSystem.IsBrowser()
            && !string.IsNullOrWhiteSpace(pair.File1Path)
            && !string.IsNullOrWhiteSpace(pair.File2Path)
            ? await BuildFocusedRawContentFromSourceFilesAsync(pair, ignorePaths).ConfigureAwait(false)
            : await BuildFocusedRawContentFromLoadedFullContentAsync(pair, ignorePaths).ConfigureAwait(false);

        if (generatedResult.IsLoaded)
        {
            focusedContentCache[cacheKey] = generatedResult;
        }

        return generatedResult;
    }

    private async Task<RawContentResult> BuildFocusedRawContentFromSourceFilesAsync(
        FilePairComparisonResult pair,
        IReadOnlyCollection<string> ignorePaths)
    {
        try
        {
            var taskA = File.ReadAllBytesAsync(pair.File1Path!);
            var taskB = File.ReadAllBytesAsync(pair.File2Path!);

            await Task.WhenAll(taskA, taskB).ConfigureAwait(false);

            var bytesA = taskA.Result;
            var bytesB = taskB.Result;
            var prunedA = focusedPruningService.TryPrune(bytesA, pair.ContentTypeA, pair.File1Name, ignorePaths);
            var prunedB = focusedPruningService.TryPrune(bytesB, pair.ContentTypeB, pair.File2Name, ignorePaths);

            if (!prunedA.IsSupported || !prunedB.IsSupported)
            {
                return new RawContentResult
                {
                    ErrorMessage = "Focused raw content is only supported for JSON and XML responses.",
                };
            }

            var contentA = prunedA.WasPruned
                ? prunedA.Content
                : StructuredTextDisplayFormatter.FormatForDisplay(DecodeText(bytesA, bytesA.Length, pair.ContentTypeA), pair.ContentTypeA, pair.File1Name);
            var contentB = prunedB.WasPruned
                ? prunedB.Content
                : StructuredTextDisplayFormatter.FormatForDisplay(DecodeText(bytesB, bytesB.Length, pair.ContentTypeB), pair.ContentTypeB, pair.File2Name);

            var displayA = TruncateDisplayContent(contentA);
            var displayB = TruncateDisplayContent(contentB);

            return new RawContentResult
            {
                ContentA = displayA.content,
                ContentB = displayB.content,
                IsTruncatedA = displayA.isTruncated,
                IsTruncatedB = displayB.isTruncated,
                IsLoaded = true,
            };
        }
        catch (FileNotFoundException ex)
        {
            logger.LogWarning(ex, "File not found when building focused raw content for side-by-side view.");
            return new RawContentResult
            {
                ErrorMessage = $"File not found: {ex.FileName}. The file may have been moved or deleted.",
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build focused raw content for side-by-side view.");
            return new RawContentResult
            {
                ErrorMessage = $"Failed to build focused raw content: {ex.Message}",
            };
        }
    }

    private async Task<RawContentResult> BuildFocusedRawContentFromLoadedFullContentAsync(
        FilePairComparisonResult pair,
        IReadOnlyCollection<string> ignorePaths)
    {
        var fullContent = await LoadFullRawContentAsync(pair).ConfigureAwait(false);
        if (!fullContent.IsLoaded)
        {
            return fullContent;
        }

        var prunedA = focusedPruningService.TryPrune(fullContent.ContentA, pair.ContentTypeA, pair.File1Name, ignorePaths);
        var prunedB = focusedPruningService.TryPrune(fullContent.ContentB, pair.ContentTypeB, pair.File2Name, ignorePaths);

        if (!prunedA.IsSupported || !prunedB.IsSupported)
        {
            return new RawContentResult
            {
                ErrorMessage = "Focused raw content is only supported for JSON and XML responses.",
            };
        }

        return new RawContentResult
        {
            ContentA = prunedA.WasPruned ? prunedA.Content : fullContent.ContentA,
            ContentB = prunedB.WasPruned ? prunedB.Content : fullContent.ContentB,
            IsTruncatedA = fullContent.IsTruncatedA,
            IsTruncatedB = fullContent.IsTruncatedB,
            IsLoaded = true,
        };
    }

    private async Task<RawContentResult> LoadRawContentFromPathsAsync(
        string? file1Path,
        string? file2Path,
        string? contentTypeA,
        string? contentTypeB,
        string fileNameA,
        string fileNameB)
    {
        var result = new RawContentResult();

        if (string.IsNullOrEmpty(file1Path) || string.IsNullOrEmpty(file2Path))
        {
            logger.LogWarning("Cannot load raw content: file paths are not available on the comparison result.");
            result.ErrorMessage = "File paths are not available for this comparison pair. "
                + "Raw content viewing is only supported when original files are accessible on disk.";
            return result;
        }

        try
        {
            var taskA = ReadFileContentAsync(file1Path, contentTypeA, fileNameA);
            var taskB = ReadFileContentAsync(file2Path, contentTypeB, fileNameB);

            await Task.WhenAll(taskA, taskB).ConfigureAwait(false);

            var (contentA, truncatedA) = taskA.Result;
            var (contentB, truncatedB) = taskB.Result;

            result.ContentA = contentA;
            result.ContentB = contentB;
            result.IsTruncatedA = truncatedA;
            result.IsTruncatedB = truncatedB;
            result.IsLoaded = true;
        }
        catch (FileNotFoundException ex)
        {
            logger.LogWarning(ex, "File not found when loading raw content for side-by-side view.");
            result.ErrorMessage = $"File not found: {ex.FileName}. The file may have been moved or deleted.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load raw file content for side-by-side view.");
            result.ErrorMessage = $"Failed to load file content: {ex.Message}";
        }

        return result;
    }

    private async Task<(string content, bool isTruncated)> ReadFileContentAsync(string filePath, string? contentType, string? fileName)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The comparison source file was not found.", filePath);
        }

        var fileInfo = new FileInfo(filePath);
        var isTruncated = fileInfo.Length > MaxFileSizeBytes;

        if (isTruncated)
        {
            logger.LogInformation(
                "File {FilePath} is {FileSize} bytes, truncating to {MaxSize} bytes for side-by-side view.",
                filePath,
                fileInfo.Length,
                MaxFileSizeBytes);
        }

        var bytesToRead = (int)Math.Min(fileInfo.Length, MaxFileSizeBytes);
        var buffer = new byte[bytesToRead];

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var bytesRead = await ReadToBufferAsync(stream, buffer).ConfigureAwait(false);
        var decodedText = DecodeText(buffer, bytesRead, contentType);
        return (StructuredTextDisplayFormatter.FormatForDisplay(decodedText, contentType, fileName ?? filePath), isTruncated);
    }

    private static IReadOnlyList<string> GetFocusedIgnorePaths(FilePairComparisonResult pair) =>
        pair.FocusedRawContentIgnorePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string BuildFocusedCacheKey(FilePairComparisonResult pair, IReadOnlyCollection<string> ignorePaths) =>
        string.Join(
            '|',
            pair.RequestRelativePath,
            pair.File1Path,
            pair.File2Path,
            pair.BundledRawContentPath,
            pair.FocusedBundledRawContentPath,
            pair.ContentTypeA,
            pair.ContentTypeB,
            string.Join('\u001f', ignorePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)));

    private static (string content, bool isTruncated) TruncateDisplayContent(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length <= MaxFileSizeBytes)
        {
            return (content, false);
        }

        return (Encoding.UTF8.GetString(bytes, 0, MaxFileSizeBytes), true);
    }

    private static async Task<int> ReadToBufferAsync(FileStream stream, byte[] buffer)
    {
        var totalBytesRead = 0;

        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalBytesRead, buffer.Length - totalBytesRead)).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytesRead += bytesRead;
        }

        return totalBytesRead;
    }

    private static string DecodeText(byte[] buffer, int bytesRead, string? contentType)
    {
        var encoding = ResolveEncoding(buffer, bytesRead, contentType);
        var text = encoding.GetString(buffer, 0, bytesRead);

        return text.Length > 0 && text[0] == '\uFEFF'
            ? text[1..]
            : text;
    }

    private static Encoding ResolveEncoding(byte[] buffer, int bytesRead, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && MediaTypeHeaderValue.TryParse(contentType, out var header)
            && !string.IsNullOrWhiteSpace(header.CharSet))
        {
            try
            {
                return Encoding.GetEncoding(header.CharSet.Trim('"'));
            }
            catch (ArgumentException)
            {
            }
        }

        if (bytesRead >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (bytesRead >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (bytesRead >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return Encoding.UTF8;
    }
}

public enum RawContentVariant
{
    Full,
    Focused,
}

/// <summary>
/// Result of loading raw file content for side-by-side viewing.
/// </summary>
public class RawContentResult
{
    public string ContentA { get; set; } = "";
    public string ContentB { get; set; } = "";
    public bool IsTruncatedA { get; set; }
    public bool IsTruncatedB { get; set; }
    public bool IsLoaded { get; set; }
    public string? ErrorMessage { get; set; }
}
