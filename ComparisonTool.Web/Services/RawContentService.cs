using ComparisonTool.Core.Comparison.Results;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Web.Services;

/// <summary>
/// Service for loading raw file content on-demand for side-by-side comparison viewing.
/// Content is loaded lazily from disk when a user requests full file comparison,
/// rather than stored in memory for all file pairs.
/// </summary>
public class RawContentService
{
    private readonly ILogger<RawContentService> logger;

    /// <summary>
    /// Maximum number of bytes to read per file. Files larger than this are truncated.
    /// </summary>
    private const int MaxFileSizeBytes = 512 * 1024; // 512 KB

    public RawContentService(ILogger<RawContentService> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Loads raw file content for both files in a comparison pair.
    /// Returns the content strings along with truncation flags.
    /// </summary>
    /// <param name="pair">The file pair with paths to both files.</param>
    /// <returns>A tuple of (contentA, contentB, isTruncatedA, isTruncatedB).</returns>
    public async Task<RawContentResult> LoadRawContentAsync(FilePairComparisonResult pair)
    {
        var result = new RawContentResult();

        if (string.IsNullOrEmpty(pair.File1Path) || string.IsNullOrEmpty(pair.File2Path))
        {
            logger.LogWarning("Cannot load raw content: file paths are not available on the comparison result.");
            result.ErrorMessage = "File paths are not available for this comparison pair. "
                + "Raw content viewing is only supported when original files are accessible on disk.";
            return result;
        }

        try
        {
            var taskA = ReadFileContentAsync(pair.File1Path);
            var taskB = ReadFileContentAsync(pair.File2Path);

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

    private async Task<(string content, bool isTruncated)> ReadFileContentAsync(string filePath)
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

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream);

        if (isTruncated)
        {
            var buffer = new char[MaxFileSizeBytes];
            var charsRead = await reader.ReadBlockAsync(buffer, 0, MaxFileSizeBytes);
            return (new string(buffer, 0, charsRead), true);
        }

        var content = await reader.ReadToEndAsync();
        return (content, false);
    }
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
