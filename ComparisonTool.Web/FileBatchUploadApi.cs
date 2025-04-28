using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ComparisonTool.Web
{
    public static class FileBatchUploadApi
    {
        public static void MapFileBatchUploadApi(this WebApplication app)
        {
            app.MapPost("/api/upload/batch", async (HttpRequest request) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest("Content-Type must be multipart/form-data");
                }

                var form = await request.ReadFormAsync();
                var files = form.Files;
                var uploadedFiles = new List<string>();
                int processedCount = 0;

                foreach (var file in files)
                {
                    // Only accept XML files (for now)
                    if (!file.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Example: Save to temp folder (customize as needed)
                    var tempPath = Path.Combine(Path.GetTempPath(), "ComparisonToolUploads");
                    var destPath = Path.Combine(tempPath, file.FileName);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    await using (var stream = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                    {
                        await file.CopyToAsync(stream);
                    }
                    uploadedFiles.Add(file.FileName);
                    processedCount++;
                }

                return Results.Ok(new
                {
                    uploaded = uploadedFiles.Count,
                    files = uploadedFiles
                });
            });
        }
    }
}
