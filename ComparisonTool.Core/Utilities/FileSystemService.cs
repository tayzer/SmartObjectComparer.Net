// <copyright file="FileSystemService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Interface for file system operations with support for folder handling.
/// </summary>
public interface IFileSystemService {
    /// <summary>
    /// Gets a list of XML files from a directory and its subdirectories.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<List<(string FilePath, string RelativePath)>> GetXmlFilesFromDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a memory stream from a file.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<MemoryStream> GetFileAsMemoryStreamAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file as a stream without loading it all into memory.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<Stream> OpenFileStreamAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates pairs of files from two directories with matching file names.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<List<(string File1Path, string File2Path, string RelativePath)>> CreateFilePairsAsync(
        string directory1,
        string directory2,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Maps browser file lists to a virtual file system for efficient processing.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<Dictionary<string, List<(MemoryStream Stream, string FileName)>>> MapFilesByFolderAsync(
        List<(MemoryStream Stream, string FileName)> files,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of file system operations with folder handling capabilities.
/// </summary>
public class FileSystemService : IFileSystemService {
    private readonly ILogger<FileSystemService> logger;

    public FileSystemService(ILogger<FileSystemService> logger) {
        this.logger = logger;
    }

    /// <summary>
    /// Gets a list of XML files from a directory and its subdirectories.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<List<(string FilePath, string RelativePath)>> GetXmlFilesFromDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default) {
        if (!Directory.Exists(directoryPath)) {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var result = new List<(string FilePath, string RelativePath)>();

        // This could take time for large directories, so use Task.Run
        await Task.Run(
            () => {
                var xmlFiles = Directory.GetFiles(directoryPath, "*.xml", SearchOption.AllDirectories);

                foreach (var filePath in xmlFiles) {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Calculate the relative path from the base directory
                    var relativePath = Path.GetRelativePath(directoryPath, filePath);
                    result.Add((filePath, relativePath));
                }

                this.logger.LogInformation(
                    "Found {Count} XML files in directory {Directory}",
                    result.Count, directoryPath);
            }, cancellationToken);

        return result;
    }

    /// <summary>
    /// Gets a memory stream from a file.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<MemoryStream> GetFileAsMemoryStreamAsync(
        string filePath,
        CancellationToken cancellationToken = default) {
        if (!File.Exists(filePath)) {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var memoryStream = new MemoryStream();

        using (var fileStream = new FileStream(
                   filePath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   4096, // Use a small buffer size
                   FileOptions.Asynchronous | FileOptions.SequentialScan)) {
            await fileStream.CopyToAsync(memoryStream, 81920, cancellationToken); // Use a decent buffer size
        }

        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Opens a file as a stream without loading it all into memory.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<Stream> OpenFileStreamAsync(
        string filePath,
        CancellationToken cancellationToken = default) {
        if (!File.Exists(filePath)) {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        // For very large files, we don't want to load it all into memory
        // We'll return a FileStream that the caller must dispose
        var fileSize = new FileInfo(filePath).Length;
        var bufferSize = this.CalculateOptimalBufferSize(fileSize);

        var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // We'll let the caller dispose the stream
        return fileStream;
    }

    /// <summary>
    /// Creates pairs of files from two directories with matching file names.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<List<(string File1Path, string File2Path, string RelativePath)>> CreateFilePairsAsync(
        string directory1,
        string directory2,
        CancellationToken cancellationToken = default) {
        if (!Directory.Exists(directory1)) {
            throw new DirectoryNotFoundException($"Directory not found: {directory1}");
        }

        if (!Directory.Exists(directory2)) {
            throw new DirectoryNotFoundException($"Directory not found: {directory2}");
        }

        var result = new List<(string File1Path, string File2Path, string RelativePath)>();

        // Get XML files from both directories
        var files1 = await this.GetXmlFilesFromDirectoryAsync(directory1, cancellationToken);
        var files2 = await this.GetXmlFilesFromDirectoryAsync(directory2, cancellationToken);

        this.logger.LogInformation(
            "Creating file pairs from {Count1} files in directory 1 and {Count2} files in directory 2",
            files1.Count, files2.Count);

        // Create a dictionary of files in directory 2 keyed by relative path
        var files2Dict = files2.ToDictionary(f => f.RelativePath, f => f.FilePath);

        // Match files by relative path
        foreach (var (filePath, relativePath) in files1) {
            cancellationToken.ThrowIfCancellationRequested();

            if (files2Dict.TryGetValue(relativePath, out var matchingFile)) {
                result.Add((filePath, matchingFile, relativePath));
            }
        }

        this.logger.LogInformation("Found {Count} matching file pairs", result.Count);

        return result;
    }

    /// <summary>
    /// Maps browser file lists to a virtual file system for efficient processing.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<Dictionary<string, List<(MemoryStream Stream, string FileName)>>> MapFilesByFolderAsync(
        List<(MemoryStream Stream, string FileName)> files,
        CancellationToken cancellationToken = default) {
        var result =
            new Dictionary<string, List<(MemoryStream Stream, string FileName)>>(StringComparer.OrdinalIgnoreCase);

        // Process files in batches to avoid memory pressure
        const int batchSize = 50;

        for (var i = 0; i < files.Count; i += batchSize) {
            var batch = files.Skip(i).Take(batchSize).ToList();

            foreach (var (stream, fileName) in batch) {
                cancellationToken.ThrowIfCancellationRequested();

                // Extract folder path from file name
                var folderPath = this.GetFolderPath(fileName);

                // Create or update the folder entry
                if (!result.ContainsKey(folderPath)) {
                    result[folderPath] = new List<(MemoryStream Stream, string FileName)>();
                }

                // Add the file to its folder
                result[folderPath].Add((stream, fileName));
            }

            // Allow GC to work between batches
            if (i + batchSize < files.Count) {
                await Task.Delay(1, cancellationToken);
            }
        }

        this.logger.LogInformation(
            "Mapped {FileCount} files into {FolderCount} folders",
            files.Count, result.Count);

        return result;
    }

    /// <summary>
    /// Extract folder path from a file name.
    /// </summary>
    private string GetFolderPath(string fileName) {
        // Normalize path separators
        var normalizedPath = fileName.Replace('\\', '/');

        var lastSeparatorIndex = normalizedPath.LastIndexOf('/');
        if (lastSeparatorIndex < 0) {
            return string.Empty; // No folder path
        }

        return normalizedPath.Substring(0, lastSeparatorIndex);
    }

    /// <summary>
    /// Calculate an optimal buffer size based on file size.
    /// </summary>
    private int CalculateOptimalBufferSize(long fileSize) {
        // For very small files, use a small buffer
        if (fileSize < 4096) {
            return 4096; // 4 KB
        }

        // For small files, use a moderate buffer
        if (fileSize < 1024 * 1024) {
            return 16 * 1024; // 16 KB
        }

        // For medium files, use a larger buffer
        if (fileSize < 10 * 1024 * 1024) {
            return 64 * 1024; // 64 KB
        }

        // For large files, use a very large buffer for streaming
        return 128 * 1024; // 128 KB
    }
}
