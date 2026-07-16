using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Web;

/// <summary>
/// Development-only endpoints for staging tracked request-comparison fixtures.
/// </summary>
public static class RequestComparisonTestFixtureApi
{
    private const string FixtureRootRelativePath =
        "ComparisonTool.Domain/TestFiles/RequestComparison/AlternativeContract/ExpectedJsonCustomerLookup";

    /// <summary>
    /// Maps fixture staging endpoints.
    /// </summary>
    public static void MapRequestComparisonTestFixtureApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/test-fixtures/request-comparison")
            .WithTags("Request Comparison Test Fixtures");

        api.MapPost("/{fixtureSet}/stage", StageFixtureSetAsync)
            .DisableAntiforgery()
            .WithName("StageRequestComparisonTestFixture")
            .WithDescription("Stages a tracked request-comparison fixture set into a temp request batch.");
    }

    private static async Task<IResult> StageFixtureSetAsync(
        string fixtureSet,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!IsSafeFixtureSetName(fixtureSet))
        {
            return Results.BadRequest("Invalid fixture set name.");
        }

        var solutionRoot = FindSolutionRoot(environment.ContentRootPath);
        if (solutionRoot == null)
        {
            return Results.Problem("Could not locate the solution root for fixture staging.");
        }

        var fixtureRoot = Path.GetFullPath(Path.Combine(
            solutionRoot,
            FixtureRootRelativePath.Replace('/', Path.DirectorySeparatorChar),
            fixtureSet));

        if (!Directory.Exists(fixtureRoot))
        {
            return Results.NotFound($"Fixture set '{fixtureSet}' was not found.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests");
        Directory.CreateDirectory(tempRoot);

        var batchId = Guid.NewGuid().ToString("N")[..8];
        var batchPath = Path.GetFullPath(Path.Combine(tempRoot, batchId));
        Directory.CreateDirectory(batchPath);

        var batchPathPrefix = batchPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var copiedFiles = new List<string>();

        foreach (var sourcePath in Directory.EnumerateFiles(fixtureRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsSupportedRequestFile(sourcePath))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(fixtureRoot, sourcePath);
            var destinationPath = Path.GetFullPath(Path.Combine(batchPath, relativePath));

            if (!destinationPath.StartsWith(batchPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Fixture contains an invalid relative path.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? batchPath);
            await using var source = File.OpenRead(sourcePath);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

            copiedFiles.Add(destinationPath);
        }

        copiedFiles.Sort(StringComparer.Ordinal);

        return Results.Ok(new RequestBatchUploadResponse
        {
            Uploaded = copiedFiles.Count,
            BatchId = batchId,
            Files = copiedFiles,
            CacheHit = false,
        });
    }

    private static bool IsSafeFixtureSetName(string fixtureSet)
    {
        return !string.IsNullOrWhiteSpace(fixtureSet)
            && fixtureSet.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    private static bool IsSupportedRequestFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ComparisonTool.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
