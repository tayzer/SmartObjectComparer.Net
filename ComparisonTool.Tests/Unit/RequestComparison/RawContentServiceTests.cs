using System.Text;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;

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
    public async Task LoadRawContentAsync_FormatsJsonArtifactsFromDisk()
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
        result.ContentA.ShouldContain("\n  \"resultCode\": \"00\"");
        result.ContentA.ShouldContain("\n  \"sourceSystem\": \"endpoint-a\"");
        result.ContentB.ShouldContain("\n  \"sourceSystem\": \"endpoint-b\"");
    }

    [TestMethod]
    public async Task LoadRawContentAsync_LoadsFocusedVariantWithoutChangingFullDefault()
    {
        var fullA = Path.Combine(tempDir, "full-a.json");
        var fullB = Path.Combine(tempDir, "full-b.json");
        var focusedA = Path.Combine(tempDir, "focused-a.json");
        var focusedB = Path.Combine(tempDir, "focused-b.json");

        await File.WriteAllTextAsync(fullA, "{\"ResultCode\":\"00\",\"TraceId\":\"full-a\"}", Encoding.UTF8);
        await File.WriteAllTextAsync(fullB, "{\"ResultCode\":\"00\",\"TraceId\":\"full-b\"}", Encoding.UTF8);
        await File.WriteAllTextAsync(focusedA, "{\"ResultCode\":\"00\"}", Encoding.UTF8);
        await File.WriteAllTextAsync(focusedB, "{\"ResultCode\":\"00\"}", Encoding.UTF8);

        var pair = new FilePairComparisonResult
        {
            File1Path = fullA,
            File2Path = fullB,
            FocusedFile1Path = focusedA,
            FocusedFile2Path = focusedB,
            FocusedRawContentRuleCount = 1,
            ContentTypeA = "application/json",
            ContentTypeB = "application/json",
        };

        var full = await service.LoadRawContentAsync(pair);
        var focused = await service.LoadRawContentAsync(pair, RawContentVariant.Focused);

        full.ContentA.ShouldContain("full-a");
        full.ContentB.ShouldContain("full-b");
        focused.ContentA.ShouldNotContain("TraceId");
        focused.ContentB.ShouldNotContain("TraceId");
        focused.ContentA.ShouldContain("ResultCode");
    }

    [TestMethod]
    public async Task LoadRawContentAsync_BuildsFocusedVariantFromFullArtifactsWhenFocusedFilesAreMissing()
    {
        var fullA = Path.Combine(tempDir, "lazy-full-a.json");
        var fullB = Path.Combine(tempDir, "lazy-full-b.json");

        await File.WriteAllTextAsync(fullA, "{\"ResultCode\":\"00\",\"SourceSystem\":\"endpoint-a\"}", Encoding.UTF8);
        await File.WriteAllTextAsync(fullB, "{\"ResultCode\":\"00\",\"SourceSystem\":\"endpoint-b\"}", Encoding.UTF8);

        var pair = new FilePairComparisonResult
        {
            File1Path = fullA,
            File2Path = fullB,
            FocusedRawContentRuleCount = 1,
            FocusedRawContentIgnorePaths = new List<string> { "SourceSystem" },
            ContentTypeA = "application/json",
            ContentTypeB = "application/json",
        };

        var full = await service.LoadRawContentAsync(pair);
        var focused = await service.LoadRawContentAsync(pair, RawContentVariant.Focused);

        pair.HasFocusedRawContent.ShouldBeTrue();
        pair.FocusedFile1Path.ShouldBeNull();
        pair.FocusedFile2Path.ShouldBeNull();
        full.ContentA.ShouldContain("SourceSystem");
        focused.ErrorMessage.ShouldBeNull();
        focused.IsLoaded.ShouldBeTrue();
        focused.ContentA.ShouldContain("ResultCode");
        focused.ContentA.ShouldNotContain("SourceSystem");
        focused.ContentB.ShouldNotContain("endpoint-b");
    }

    [TestMethod]
    public async Task LoadRawContentAsync_BuildsFocusedVariantFromBundledFullContentWhenFocusedSidecarIsMissing()
    {
        var accessor = new VariantBundledRawContentAccessor(
            fullResult: new RawContentResult
            {
                ContentA = "{\n  \"ResultCode\": \"00\",\n  \"TraceId\": \"bundled-a\"\n}",
                ContentB = "{\n  \"ResultCode\": \"00\",\n  \"TraceId\": \"bundled-b\"\n}",
                IsLoaded = true,
            },
            focusedResult: null);
        var logger = new Mock<ILogger<RawContentService>>();
        service = new RawContentService(logger.Object, accessor);

        var pair = new FilePairComparisonResult
        {
            BundledRawContentPath = "raw/pair-1.json",
            FocusedRawContentRuleCount = 1,
            FocusedRawContentIgnorePaths = new List<string> { "TraceId" },
            ContentTypeA = "application/json",
            ContentTypeB = "application/json",
        };

        var focused = await service.LoadRawContentAsync(pair, RawContentVariant.Focused);

        focused.ErrorMessage.ShouldBeNull();
        focused.IsLoaded.ShouldBeTrue();
        focused.ContentA.ShouldContain("ResultCode");
        focused.ContentA.ShouldNotContain("TraceId");
        focused.ContentB.ShouldNotContain("bundled-b");
        accessor.FocusedInvocationCount.ShouldBe(0);
        accessor.FullInvocationCount.ShouldBe(1);
    }

    private sealed class StubBundledRawContentAccessor : IBundledRawContentAccessor
    {
        private readonly RawContentResult result;

        public StubBundledRawContentAccessor(RawContentResult result)
        {
            this.result = result;
        }

        public int InvocationCount { get; private set; }

        public Task<RawContentResult?> TryLoadAsync(FilePairComparisonResult pair, RawContentVariant variant = RawContentVariant.Full)
        {
            InvocationCount++;
            return Task.FromResult<RawContentResult?>(result);
        }
    }

    private sealed class VariantBundledRawContentAccessor : IBundledRawContentAccessor
    {
        private readonly RawContentResult? fullResult;
        private readonly RawContentResult? focusedResult;

        public VariantBundledRawContentAccessor(RawContentResult? fullResult, RawContentResult? focusedResult)
        {
            this.fullResult = fullResult;
            this.focusedResult = focusedResult;
        }

        public int FullInvocationCount { get; private set; }

        public int FocusedInvocationCount { get; private set; }

        public Task<RawContentResult?> TryLoadAsync(FilePairComparisonResult pair, RawContentVariant variant = RawContentVariant.Full)
        {
            if (variant == RawContentVariant.Focused)
            {
                FocusedInvocationCount++;
                return Task.FromResult(focusedResult);
            }

            FullInvocationCount++;
            return Task.FromResult(fullResult);
        }
    }
}
