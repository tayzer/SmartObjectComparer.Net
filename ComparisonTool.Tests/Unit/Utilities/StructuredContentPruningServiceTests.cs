using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Unit.Utilities;

[TestClass]
public sealed class StructuredContentPruningServiceTests
{
    private readonly StructuredContentPruningService service = new(NullLogger<StructuredContentPruningService>.Instance);

    [TestMethod]
    public void TryPrune_RemovesJsonExactAndRootQualifiedPaths()
    {
        const string content = """
            {
              "ResultCode": "00",
              "SourceSystem": "endpoint-a"
            }
            """;

        var result = service.TryPrune(
            content,
            "application/json",
            "response.json",
            new[] { "ExpectedJsonCustomerLookupResponse.SourceSystem" });

        result.IsSupported.ShouldBeTrue();
        result.WasPruned.ShouldBeTrue();
        result.RemovedFieldCount.ShouldBe(1);
        result.Content.ShouldContain("ResultCode");
        result.Content.ShouldNotContain("SourceSystem");
        result.Content.ShouldNotContain("endpoint-a");
    }

    [TestMethod]
    public void TryPrune_RemovesJsonNestedSubtree()
    {
        const string content = """
            {
              "Customer": {
                "Name": "Ada",
                "Audit": {
                  "TraceId": "abc",
                  "Source": "backend"
                }
              },
              "Status": "ok"
            }
            """;

        var result = service.TryPrune(content, "application/json", "response.json", new[] { "Customer.Audit" });

        result.WasPruned.ShouldBeTrue();
        result.Content.ShouldContain("Name");
        result.Content.ShouldContain("Status");
        result.Content.ShouldNotContain("Audit");
        result.Content.ShouldNotContain("TraceId");
    }

    [TestMethod]
    public void TryPrune_RemovesJsonCollectionWildcardPaths()
    {
        const string content = """
            {
              "Items": [
                { "Name": "first", "TraceId": "a" },
                { "Name": "second", "TraceId": "b" }
              ]
            }
            """;

        var result = service.TryPrune(content, "application/json", "response.json", new[] { "Items[*].TraceId" });

        result.WasPruned.ShouldBeTrue();
        result.RemovedFieldCount.ShouldBe(2);
        result.Content.ShouldContain("first");
        result.Content.ShouldContain("second");
        result.Content.ShouldNotContain("TraceId");
    }

    [TestMethod]
    public void TryPrune_RemovesXmlRootQualifiedAndCollectionWildcardPaths()
    {
        const string content = """
            <Response>
              <ResultCode>00</ResultCode>
              <SourceSystem>endpoint-a</SourceSystem>
              <Items>
                <Item><Name>first</Name><TraceId>a</TraceId></Item>
                <Item><Name>second</Name><TraceId>b</TraceId></Item>
              </Items>
            </Response>
            """;

        var result = service.TryPrune(
            content,
            "application/xml",
            "response.xml",
            new[] { "Response.SourceSystem", "Response.Items.Item[*].TraceId" });

        result.IsSupported.ShouldBeTrue();
        result.WasPruned.ShouldBeTrue();
        result.RemovedFieldCount.ShouldBe(3);
        result.Content.ShouldContain("ResultCode");
        result.Content.ShouldContain("first");
        result.Content.ShouldContain("second");
        result.Content.ShouldNotContain("SourceSystem");
        result.Content.ShouldNotContain("TraceId");
    }

    [TestMethod]
    public async Task PopulateFocusedRawContentAsync_DoesNotCreateArtifactsForOrderOnlyRules()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusedRawContentTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var fileA = Path.Combine(directory, "a.json");
            var fileB = Path.Combine(directory, "b.json");
            await File.WriteAllTextAsync(fileA, "{\"Items\":[{\"Name\":\"a\",\"TraceId\":\"1\"}]}");
            await File.WriteAllTextAsync(fileB, "{\"Items\":[{\"Name\":\"b\",\"TraceId\":\"2\"}]}");

            var result = new MultiFolderComparisonResult
            {
                FilePairResults =
                [
                    new FilePairComparisonResult
                    {
                        File1Path = fileA,
                        File2Path = fileB,
                        File1Name = "a.json",
                        File2Name = "b.json",
                        ContentTypeA = "application/json",
                        ContentTypeB = "application/json",
                    },
                ],
            };
            var artifactService = new FocusedRawContentArtifactService(
                service,
                NullLogger<FocusedRawContentArtifactService>.Instance);

            await artifactService.PopulateFocusedRawContentAsync(
                result,
                [new IgnoreRule { PropertyPath = "Items", IgnoreCollectionOrder = true, IgnoreCompletely = false }],
                Path.Combine(directory, "focused"));

            result.FilePairResults[0].HasFocusedRawContent.ShouldBeFalse();
            result.Metadata[FocusedRawContentArtifactService.MetadataFocusedPairCountKey].ShouldBe(0);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}