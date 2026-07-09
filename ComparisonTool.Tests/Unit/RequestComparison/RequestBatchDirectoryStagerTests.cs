using ComparisonTool.Core.RequestComparison.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Unit.RequestComparison;

[TestClass]
public class RequestBatchDirectoryStagerTests : IDisposable
{
    private readonly List<string> createdPaths = new();

    public void Dispose()
    {
        foreach (var path in createdPaths)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    [TestMethod]
    public void StageDirectory_IncludesEligibleRequestFilesAndHeaderSidecars()
    {
        var sourceRoot = CreateDirectory();
        var batchRoot = CreateDirectory();
        var nestedDirectory = Path.Combine(sourceRoot, "region-a", "nested");
        Directory.CreateDirectory(nestedDirectory);

        File.WriteAllText(Path.Combine(sourceRoot, "root.json"), "{}");
        File.WriteAllText(Path.Combine(sourceRoot, "root.json.headers.json"), "{\"headers\":{\"x-test\":\"1\"}}");
        File.WriteAllText(Path.Combine(nestedDirectory, "request.xml"), "<Request />");
        File.WriteAllText(Path.Combine(nestedDirectory, "_ignored.xml"), "<Request />");
        File.WriteAllText(Path.Combine(nestedDirectory, "ignored.csv"), "x,y");

        var result = RequestBatchDirectoryStager.StageDirectory(sourceRoot, batchRoot);

        result.RequestFileCount.ShouldBe(2);
        result.SidecarFileCount.ShouldBe(1);
        File.Exists(Path.Combine(batchRoot, "root.json")).ShouldBeTrue();
        File.Exists(Path.Combine(batchRoot, "root.json.headers.json")).ShouldBeTrue();
        File.Exists(Path.Combine(batchRoot, "region-a", "nested", "request.xml")).ShouldBeTrue();
        File.Exists(Path.Combine(batchRoot, "region-a", "nested", "_ignored.xml")).ShouldBeFalse();
        File.Exists(Path.Combine(batchRoot, "region-a", "nested", "ignored.csv")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(batchRoot, "root.json")).ShouldBe("{}");
        File.ReadAllText(Path.Combine(batchRoot, "root.json.headers.json")).ShouldContain("x-test");
        File.ReadAllText(Path.Combine(batchRoot, "region-a", "nested", "request.xml")).ShouldContain("<Request");
    }

    [TestMethod]
    public void GetSafeRelativePath_RejectsFilesOutsideSourceDirectory()
    {
        var sourceRoot = CreateDirectory();
        var outsideDirectory = CreateDirectory();
        var outsideFile = Path.Combine(outsideDirectory, "request.json");
        File.WriteAllText(outsideFile, "{}");

        Should.Throw<InvalidOperationException>(() =>
            RequestBatchDirectoryStager.GetSafeRelativePath(sourceRoot, outsideFile));
    }

    private string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ComparisonToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        createdPaths.Add(path);
        return path;
    }
}
