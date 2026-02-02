// <copyright file="FileBatchUploadApi.cs" company="PlaceholderCompany">
namespace ComparisonTool.Web;

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

public static class FileBatchUploadApi
{
    private const int BufferSize = 81920; // 80KB buffer for streaming

    // Shared buffer pool to reduce GC pressure during file uploads
    private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Shared;

    public static void MapFileBatchUploadApi(this WebApplication app)
    {
        app.MapPost("/api/upload/batch", async (HttpRequest request) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest("Content-Type must be multipart/form-data");
            }

            var form = await request.ReadFormAsync().ConfigureAwait(false);
            var files = form.Files;
            var uploadedFiles = new ConcurrentBag<string>();
            var tempPath = Path.Combine(Path.GetTempPath(), "ComparisonToolUploads");

            // NOTE: Cleanup moved to application startup to avoid race conditions
            // during parallel batch uploads. Old files will be cleaned on next app start.

            // Create the uploads directory if it doesn't exist
            if (!Directory.Exists(tempPath))
            {
                Directory.CreateDirectory(tempPath);
            }

            // Use a unique subfolder for this upload batch to avoid name conflicts
            var batchId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var batchPath = Path.Combine(tempPath, batchId);
            Directory.CreateDirectory(batchPath);

            // Pre-create all needed directories to avoid lock contention
            var directories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (!file.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    !file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var filePath = file.FileName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var destPath = Path.Combine(batchPath, filePath);
                var destDir = Path.GetDirectoryName(destPath) ?? batchPath;
                directories.Add(destDir);
            }

            // Create directories in parallel (though this is usually fast)
            foreach (var dir in directories)
            {
                Directory.CreateDirectory(dir);
            }

            // Process files in parallel with controlled concurrency
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount * 2, 16),
            };

            await Parallel.ForEachAsync(files, parallelOptions, async (file, ct) =>
            {
                // Only accept supported files (XML and JSON)
                if (!file.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    !file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                try
                {
                    // Preserve folder structure by creating subdirectories
                    var filePath = file.FileName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    var destPath = Path.Combine(batchPath, filePath);

                    // Use buffered streaming with pooled buffer to reduce memory pressure
                    var buffer = BufferPool.Rent(BufferSize);
                    try
                    {
                        var sourceStream = file.OpenReadStream();
                        await using (sourceStream.ConfigureAwait(false))
                        {
                            await using var destStream = new FileStream(
                                destPath,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.None,
                                BufferSize,
                                FileOptions.Asynchronous | FileOptions.SequentialScan);

                            int bytesRead;
                            while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
                            {
                                await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                            }
                        }
                    }
                    finally
                    {
                        BufferPool.Return(buffer);
                    }

                    // Store the full path for later use
                    uploadedFiles.Add(destPath);
                }
                catch (Exception)
                {
                    // Log exception and continue with next file
                }
            }).ConfigureAwait(false);

            var sortedFiles = uploadedFiles.OrderBy(f => f, StringComparer.Ordinal).ToList();

            // For large file sets, don't return the entire list to avoid memory pressure
            // Instead, write to a temporary file and return its location
            if (sortedFiles.Count > 100)
            {
                var fileListPath = Path.Combine(batchPath, "_filelist.json");
                await File.WriteAllTextAsync(fileListPath, JsonSerializer.Serialize(sortedFiles)).ConfigureAwait(false);

                return Results.Ok(new
                {
                    uploaded = sortedFiles.Count,
                    batchId = batchId,
                    fileListPath = fileListPath,
                });
            }
            else
            {
                // For smaller sets, return the list directly
                return Results.Ok(new
                {
                    uploaded = sortedFiles.Count,
                    files = sortedFiles,
                });
            }
        });

        // Add an endpoint to get the file list for a specific batch
        app.MapGet("/api/upload/batch/{batchId}", (string batchId) =>
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "ComparisonToolUploads");
            var batchPath = Path.Combine(tempPath, batchId);
            var fileListPath = Path.Combine(batchPath, "_filelist.json");

            if (!File.Exists(fileListPath))
            {
                return Results.NotFound($"Batch {batchId} not found");
            }

            var fileList = JsonSerializer.Deserialize<List<string>>(
                File.ReadAllText(fileListPath)) ?? new List<string>();

            return Results.Ok(new
            {
                files = fileList,
            });
        });
    }
}
