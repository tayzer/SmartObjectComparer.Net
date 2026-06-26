using ComparisonTool.Core.Comparison;
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
    public async Task PopulateFocusedRawContentAsync_ReportsProgressForEachPair()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusedRawContentTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var fileA1 = Path.Combine(directory, "a1.json");
            var fileB1 = Path.Combine(directory, "b1.json");
            var fileA2 = Path.Combine(directory, "a2.json");
            var fileB2 = Path.Combine(directory, "b2.json");
            await File.WriteAllTextAsync(fileA1, """{"ResultCode":"00","TraceId":"a1"}""");
            await File.WriteAllTextAsync(fileB1, """{"ResultCode":"00","TraceId":"b1"}""");
            await File.WriteAllTextAsync(fileA2, """{"ResultCode":"00","TraceId":"a2"}""");
            await File.WriteAllTextAsync(fileB2, """{"ResultCode":"00","TraceId":"b2"}""");

            var result = new MultiFolderComparisonResult
            {
                FilePairResults =
                [
                    new FilePairComparisonResult
                    {
                        File1Path = fileA1,
                        File2Path = fileB1,
                        File1Name = "a1.json",
                        File2Name = "b1.json",
                        ContentTypeA = "application/json",
                        ContentTypeB = "application/json",
                    },
                    new FilePairComparisonResult
                    {
                        File1Path = fileA2,
                        File2Path = fileB2,
                        File1Name = "a2.json",
                        File2Name = "b2.json",
                        ContentTypeA = "application/json",
                        ContentTypeB = "application/json",
                    },
                ],
            };
            var updates = new List<ComparisonProgress>();
            var progress = new CapturingProgress<ComparisonProgress>(updates.Add);
            var artifactService = new FocusedRawContentArtifactService(
                service,
                NullLogger<FocusedRawContentArtifactService>.Instance);

            await artifactService.PopulateFocusedRawContentAsync(
                result,
                [new IgnoreRule { PropertyPath = "TraceId", IgnoreCompletely = true }],
                Path.Combine(directory, "focused"),
                progress: progress);

            updates.Count.ShouldBeGreaterThanOrEqualTo(3);
            updates.First().Completed.ShouldBe(0);
            updates.First().Total.ShouldBe(2);
            updates.Last().Completed.ShouldBe(2);
            updates.Last().Total.ShouldBe(2);
            updates.Last().Status.ShouldBe("Preparing focused raw content 2 of 2");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
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
    private sealed class CapturingProgress<T> : IProgress<T>
    {
        private readonly Action<T> handler;

        public CapturingProgress(Action<T> handler)
        {
            this.handler = handler;
        }

        public void Report(T value) => handler(value);
    }
}
