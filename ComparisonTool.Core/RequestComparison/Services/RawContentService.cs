using System.Net.Http.Headers;
using System.Text;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Service for loading raw file content on-demand for side-by-side comparison viewing.
/// Content is loaded lazily from disk when a user requests full file comparison,
/// rather than stored in memory for all file pairs.
/// </summary>
public class RawContentService
{
    private readonly ILogger<RawContentService> logger;
    private readonly IBundledRawContentAccessor? bundledRawContentAccessor;

    /// <summary>
    /// Maximum number of bytes to read per file. Files larger than this are truncated.
    /// </summary>
    private const int MaxFileSizeBytes = 512 * 1024; // 512 KB

    public RawContentService(ILogger<RawContentService> logger)
        : this(logger, bundledRawContentAccessor: null)
    {
    }

    public RawContentService(ILogger<RawContentService> logger, IBundledRawContentAccessor? bundledRawContentAccessor)
    {
        this.logger = logger;
        this.bundledRawContentAccessor = bundledRawContentAccessor;
    }

    /// <summary>
    /// Loads raw file content for both files in a comparison pair.
    /// Returns the content strings along with truncation flags.
    /// </summary>
    /// <param name="pair">The file pair with paths to both files.</param>
    /// <returns>A tuple of (contentA, contentB, isTruncatedA, isTruncatedB).</returns>
    public async Task<RawContentResult> LoadRawContentAsync(FilePairComparisonResult pair, RawContentVariant variant = RawContentVariant.Full)
    {
        var result = new RawContentResult();

        if (variant == RawContentVariant.Full && pair.HasEmbeddedRawContent)
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
            var bundledResult = await this.bundledRawContentAccessor.TryLoadAsync(pair, variant).ConfigureAwait(false);
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

        var file1Path = variant == RawContentVariant.Focused ? pair.FocusedFile1Path : pair.File1Path;
        var file2Path = variant == RawContentVariant.Focused ? pair.FocusedFile2Path : pair.File2Path;

        if (string.IsNullOrEmpty(file1Path) || string.IsNullOrEmpty(file2Path))
        {
            logger.LogWarning("Cannot load raw content: file paths are not available on the comparison result.");
            result.ErrorMessage = "File paths are not available for this comparison pair. "
                + "Raw content viewing is only supported when original files are accessible on disk.";
            return result;
        }

        try
        {
            var taskA = ReadFileContentAsync(file1Path, pair.ContentTypeA, pair.File1Name);
            var taskB = ReadFileContentAsync(file2Path, pair.ContentTypeB, pair.File2Name);

            await Task.WhenAll(taskA, taskB);

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
