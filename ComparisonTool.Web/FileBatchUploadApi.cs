// <copyright file="FileBatchUploadApi.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Web {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;

    public static class FileBatchUploadApi {
        public static void MapFileBatchUploadApi(this WebApplication app) {
            app.MapPost("/api/upload/batch", async (HttpRequest request) => {
                if (!request.HasFormContentType) {
                    return Results.BadRequest("Content-Type must be multipart/form-data");
                }

                var form = await request.ReadFormAsync();
                var files = form.Files;
                var uploadedFiles = new List<string>();
                var tempPath = Path.Combine(Path.GetTempPath(), "ComparisonToolUploads");

                // Clear old temp files (optional, but helps manage disk space)
                if (Directory.Exists(tempPath) &&
                    Directory.GetCreationTime(tempPath) < DateTime.Now.AddDays(-1)) {
                    try {
                        Directory.Delete(tempPath, true);
                    }
                    catch {
                        // Ignore errors when cleaning up temp
                    }
                }

                // Create the uploads directory if it doesn't exist
                if (!Directory.Exists(tempPath)) {
                    Directory.CreateDirectory(tempPath);
                }

                // Use a unique subfolder for this upload batch to avoid name conflicts
                var batchId = Guid.NewGuid().ToString("N").Substring(0, 8);
                var batchPath = Path.Combine(tempPath, batchId);
                Directory.CreateDirectory(batchPath);

                foreach (var file in files) {
                    // Only accept supported files (XML and JSON)
                    if (!file.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                        !file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    try {
                        // Preserve folder structure by creating subdirectories
                        var filePath = file.FileName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                        var destPath = Path.Combine(batchPath, filePath);
                        var destDir = Path.GetDirectoryName(destPath) ?? batchPath;

                        if (!Directory.Exists(destDir)) {
                            Directory.CreateDirectory(destDir);
                        }

                        await using (var stream = new FileStream(destPath, FileMode.Create, FileAccess.Write)) {
                            await file.CopyToAsync(stream);
                        }

                        // Store the full path for later use (with batch ID prefix for identification)
                        uploadedFiles.Add(destPath);
                    }
                    catch (Exception) {
                        // Log exception and continue with next file
                        continue;
                    }
                }

                // For large file sets, don't return the entire list to avoid memory pressure
                // Instead, write to a temporary file and return its location
                if (uploadedFiles.Count > 100) {
                    var fileListPath = Path.Combine(batchPath, "_filelist.json");
                    await File.WriteAllTextAsync(fileListPath, JsonSerializer.Serialize(uploadedFiles));

                    return Results.Ok(new {
                        uploaded = uploadedFiles.Count,
                        batchId = batchId,
                        fileListPath = fileListPath,
                    });
                }
                else {
                    // For smaller sets, return the list directly
                    return Results.Ok(new {
                        uploaded = uploadedFiles.Count,
                        files = uploadedFiles,
                    });
                }
            });

            // Add an endpoint to get the file list for a specific batch
            app.MapGet("/api/upload/batch/{batchId}", (string batchId) => {
                var tempPath = Path.Combine(Path.GetTempPath(), "ComparisonToolUploads");
                var batchPath = Path.Combine(tempPath, batchId);
                var fileListPath = Path.Combine(batchPath, "_filelist.json");

                if (!File.Exists(fileListPath)) {
                    return Results.NotFound($"Batch {batchId} not found");
                }

                var fileList = JsonSerializer.Deserialize<List<string>>(
                    File.ReadAllText(fileListPath)) ?? new List<string>();

                return Results.Ok(new { files = fileList });
            });
        }
    }
}
