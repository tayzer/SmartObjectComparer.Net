using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;
using Moq;

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

        result.IsLoaded.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.ContentA.Should().Be(body);
        result.ContentB.Should().Be(body);
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

        result.IsLoaded.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.ContentA.Should().Be("<embedded>a</embedded>");
        result.ContentB.Should().Be("<embedded>b</embedded>");
        result.IsTruncatedA.Should().BeTrue();
        result.IsTruncatedB.Should().BeFalse();
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

        result.IsLoaded.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.ContentA.Should().Be("bundled-a");
        result.ContentB.Should().Be("bundled-b");
        result.IsTruncatedA.Should().BeFalse();
        result.IsTruncatedB.Should().BeTrue();
        accessor.InvocationCount.Should().Be(1);
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