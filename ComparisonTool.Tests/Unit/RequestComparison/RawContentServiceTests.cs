using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using System.Text;

namespace ComparisonTool.Tests.Unit.RequestComparison;

[TestClass]
public sealed class RawContentServiceTests
{
    private RawContentService service = null!;
    private string tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        var logger = new Mock<ILogger<RawContentService>>();
        service = new RawContentService(logger.Object);
        tempDir = Path.Combine(Path.GetTempPath(), "RawContentServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task LoadRawContentAsync_UsesDeclaredCharsetsWhenAvailable()
    {
        var pathA = Path.Combine(tempDir, "responseA.xml");
        var pathB = Path.Combine(tempDir, "responseB.xml");
        const string body = "<fault>same body</fault>";

        await File.WriteAllTextAsync(pathA, body, Encoding.Unicode);
        await File.WriteAllTextAsync(pathB, body, Encoding.UTF8);

        var pair = new FilePairComparisonResult
        {
            File1Path = pathA,
            File2Path = pathB,
            ContentTypeA = "application/xml; charset=utf-16",
            ContentTypeB = "application/xml; charset=utf-8",
        };

        var result = await service.LoadRawContentAsync(pair);

        result.IsLoaded.ShouldBeTrue();
        result.ErrorMessage.ShouldBeNull();
        result.ContentA.ShouldBe(body);
        result.ContentB.ShouldBe(body);
    }

    [TestMethod]
    public async Task LoadRawContentAsync_UsesEmbeddedContentBeforeDiskAccess()
    {
        var pair = new FilePairComparisonResult
        {
            HasEmbeddedRawContent = true,
            EmbeddedRawContentA = "<embedded>a</embedded>",
            EmbeddedRawContentB = "<embedded>b</embedded>",
            EmbeddedRawContentTruncatedA = true,
            EmbeddedRawContentTruncatedB = false,
            File1Path = Path.Combine(tempDir, "missing-a.xml"),
            File2Path = Path.Combine(tempDir, "missing-b.xml"),
        };

        var result = await service.LoadRawContentAsync(pair);

        result.IsLoaded.ShouldBeTrue();
        result.ErrorMessage.ShouldBeNull();
        result.ContentA.ShouldBe("<embedded>a</embedded>");
        result.ContentB.ShouldBe("<embedded>b</embedded>");
        result.IsTruncatedA.ShouldBeTrue();
        result.IsTruncatedB.ShouldBeFalse();
    }

    [TestMethod]
    public async Task LoadRawContentAsync_UsesBundledRawContentAccessorBeforeDiskAccess()
    {
        var accessor = new StubBundledRawContentAccessor(
            new RawContentResult
            {
                ContentA = "bundled-a",
                ContentB = "bundled-b",
                IsTruncatedA = false,
                IsTruncatedB = true,
                IsLoaded = true,
            });
        var logger = new Mock<ILogger<RawContentService>>();
        service = new RawContentService(logger.Object, accessor);

        var pair = new FilePairComparisonResult
        {
            BundledRawContentPath = "raw/pair-1.json",
            File1Path = Path.Combine(tempDir, "missing-a.xml"),
            File2Path = Path.Combine(tempDir, "missing-b.xml"),
        };

        var result = await service.LoadRawContentAsync(pair);

        result.IsLoaded.ShouldBeTrue();
        result.ErrorMessage.ShouldBeNull();
        result.ContentA.ShouldBe("bundled-a");
        result.ContentB.ShouldBe("bundled-b");
        result.IsTruncatedA.ShouldBeFalse();
        result.IsTruncatedB.ShouldBeTrue();
        accessor.InvocationCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task LoadRawContentAsync_LoadsNormalizedJsonArtifactsFromDisk()
    {
        var pathA = Path.Combine(tempDir, "comparison-a.json");
        var pathB = Path.Combine(tempDir, "comparison-b.json");
        const string bodyA = "{\"resultCode\":\"00\",\"sourceSystem\":\"endpoint-a\"}";
        const string bodyB = "{\"resultCode\":\"00\",\"sourceSystem\":\"endpoint-b\"}";

        await File.WriteAllTextAsync(pathA, bodyA, Encoding.UTF8);
        await File.WriteAllTextAsync(pathB, bodyB, Encoding.UTF8);

        var pair = new FilePairComparisonResult
        {
            File1Path = pathA,
            File2Path = pathB,
            ContentTypeA = "application/json",
            ContentTypeB = "application/json",
        };

        var result = await service.LoadRawContentAsync(pair);

        result.IsLoaded.ShouldBeTrue();
        result.ErrorMessage.ShouldBeNull();
        result.ContentA.ShouldBe(bodyA);
        result.ContentB.ShouldBe(bodyB);
    }

    private sealed class StubBundledRawContentAccessor : IBundledRawContentAccessor
    {
        private readonly RawContentResult result;

        public StubBundledRawContentAccessor(RawContentResult result)
        {
            this.result = result;
        }

        public int InvocationCount { get; private set; }

        public Task<RawContentResult?> TryLoadAsync(FilePairComparisonResult pair)
        {
            InvocationCount++;
            return Task.FromResult<RawContentResult?>(result);
        }
    }
}