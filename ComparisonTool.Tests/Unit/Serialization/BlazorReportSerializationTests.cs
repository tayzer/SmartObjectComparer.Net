using System.Text.Json;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization.BlazorReport;
using KellermanSoftware.CompareNetObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Unit.Serialization;

[TestClass]
public class BlazorReportSerializationTests
{
    [TestMethod]
    public void ReportBootstrapData_RoundTrips_CoreProperties()
    {
        // Arrange
        var diff = new Difference
        {
            PropertyName = "Root.Name",
            Object1Value = "Alice",
            Object2Value = "Bob",
        };

        var comparisonResult = new ComparisonResult(new ComparisonConfig());
        comparisonResult.Differences.Add(diff);

        var filePair = new FilePairComparisonResult
        {
            File1Name = "expected.xml",
            File2Name = "actual.xml",
            Result = comparisonResult,
            Summary = new DifferenceSummary
            {
                AreEqual = false,
                TotalDifferenceCount = 1,
            },
        };

        var metadata = new ReportMetadata
        {
            ReportId = "test-report-001",
            GeneratedAt = "2026-04-15T10:00:00Z",
            Command = "folder",
            ModelName = "TestModel",
            Directory1 = @"C:\Expected",
            Directory2 = @"C:\Actual",
            ElapsedSeconds = 1.234,
        };

        var bootstrapData = new ReportBootstrapData
        {
            Result = new MultiFolderComparisonResult
            {
                AllEqual = false,
                TotalPairsCompared = 1,
                FilePairResults = new List<FilePairComparisonResult> { filePair },
            },
            EnhancedAnalysis = null,
            SemanticAnalysis = null,
            Metadata = metadata,
        };

        // Act
        var json = JsonSerializer.Serialize(bootstrapData, BlazorReportSerializerOptions.Default);
        var result = JsonSerializer.Deserialize<ReportBootstrapData>(json, BlazorReportSerializerOptions.Default)!;

        // Assert
        result.Result!.AllEqual.ShouldBe(false);
        result.Result.TotalPairsCompared.ShouldBe(1);
        result.Result.FilePairResults[0].File1Name.ShouldBe("expected.xml");
        result.Result.FilePairResults[0].Result!.Differences[0].PropertyName.ShouldBe("Root.Name");
        result.Result.FilePairResults[0].Result!.Differences[0].Object1Value.ShouldBe("Alice");
        result.Result.FilePairResults[0].Result!.Differences[0].Object2Value.ShouldBe("Bob");
        result.Metadata!.ReportId.ShouldBe("test-report-001");
        result.Metadata.ElapsedSeconds.ShouldBe(1.234);
    }

    [TestMethod]
    public void Difference_RoundTrips_AllWritableProperties()
    {
        // Arrange
        var diff = new Difference
        {
            PropertyName = "Root.Items[0].Value",
            Object1Value = "100",
            Object2Value = "200",
            ChildPropertyName = "Value",
        };

        var comparisonResult = new ComparisonResult(new ComparisonConfig());
        comparisonResult.Differences.Add(diff);

        // Act
        var json = JsonSerializer.Serialize(comparisonResult, BlazorReportSerializerOptions.Default);
        var result = JsonSerializer.Deserialize<ComparisonResult>(json, BlazorReportSerializerOptions.Default)!;

        // Assert
        var roundTripped = result.Differences[0];
        roundTripped.PropertyName.ShouldBe("Root.Items[0].Value");
        roundTripped.Object1Value.ShouldBe("100");
        roundTripped.Object2Value.ShouldBe("200");
        roundTripped.ChildPropertyName.ShouldBe("Value");

        // ParentPropertyName is read-only (computed from PropertyName) on Difference;
        // it is NOT deserialized from JSON, but is recomputed from the round-tripped PropertyName.
        // We just verify the serializer didn't throw and the writable props survived.
    }

    [TestMethod]
    public void NullAndEmpty_EdgeCases()
    {
        // Arrange
        var bootstrapData = new ReportBootstrapData
        {
            Result = new MultiFolderComparisonResult
            {
                AllEqual = true,
                TotalPairsCompared = 0,
                FilePairResults = new List<FilePairComparisonResult>(),
            },
            EnhancedAnalysis = null,
            SemanticAnalysis = null,
            Metadata = new ReportMetadata
            {
                ReportId = "empty-test",
            },
        };

        // Act
        var json = JsonSerializer.Serialize(bootstrapData, BlazorReportSerializerOptions.Default);
        var result = JsonSerializer.Deserialize<ReportBootstrapData>(json, BlazorReportSerializerOptions.Default)!;

        // Assert
        result.EnhancedAnalysis.ShouldBeNull();
        result.SemanticAnalysis.ShouldBeNull();
        result.Result!.FilePairResults.ShouldBeEmpty();
        result.Result.AllEqual.ShouldBeTrue();
        result.Metadata!.ReportId.ShouldBe("empty-test");
    }

    [TestMethod]
    public void Metadata_RoundTrips_AllFields()
    {
        // Arrange
        var metadata = new ReportMetadata
        {
            ReportId = "rpt-42",
            GeneratedAt = "2026-04-15T12:30:00Z",
            Command = "request",
            ModelName = "ComplexOrderResponse",
            Directory1 = @"C:\Dir1",
            Directory2 = @"C:\Dir2",
            EndpointA = "https://api-a.example.com",
            EndpointB = "https://api-b.example.com",
            JobId = "job-99",
            ElapsedSeconds = 42.567,
        };

        var bootstrapData = new ReportBootstrapData
        {
            Result = new MultiFolderComparisonResult
            {
                AllEqual = true,
                TotalPairsCompared = 0,
                FilePairResults = new List<FilePairComparisonResult>(),
            },
            Metadata = metadata,
        };

        // Act
        var json = JsonSerializer.Serialize(bootstrapData, BlazorReportSerializerOptions.Default);
        var result = JsonSerializer.Deserialize<ReportBootstrapData>(json, BlazorReportSerializerOptions.Default)!;

        // Assert
        var m = result.Metadata!;
        m.ReportId.ShouldBe("rpt-42");
        m.GeneratedAt.ShouldBe("2026-04-15T12:30:00Z");
        m.Command.ShouldBe("request");
        m.ModelName.ShouldBe("ComplexOrderResponse");
        m.Directory1.ShouldBe(@"C:\Dir1");
        m.Directory2.ShouldBe(@"C:\Dir2");
        m.EndpointA.ShouldBe("https://api-a.example.com");
        m.EndpointB.ShouldBe("https://api-b.example.com");
        m.JobId.ShouldBe("job-99");
        m.ElapsedSeconds.ShouldBe(42.567);
    }

    [TestMethod]
    public void ReportBootstrapData_RoundTrips_EmbeddedRawContent()
    {
        var bootstrapData = new ReportBootstrapData
        {
            Result = new MultiFolderComparisonResult
            {
                AllEqual = false,
                TotalPairsCompared = 1,
                FilePairResults = new List<FilePairComparisonResult>
                {
                    new()
                    {
                        File1Name = "request1.xml",
                        File2Name = "request1.xml",
                        HasEmbeddedRawContent = true,
                        EmbeddedRawContentA = "<fault>endpoint-a</fault>",
                        EmbeddedRawContentB = "<fault>endpoint-b</fault>",
                        EmbeddedRawContentTruncatedA = true,
                        EmbeddedRawContentTruncatedB = false,
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(bootstrapData, BlazorReportSerializerOptions.Default);
        var result = JsonSerializer.Deserialize<ReportBootstrapData>(json, BlazorReportSerializerOptions.Default)!;

        result.Result!.FilePairResults.Count.ShouldBe(1);
        var pair = result.Result.FilePairResults[0];
        pair.HasEmbeddedRawContent.ShouldBeTrue();
        pair.EmbeddedRawContentA.ShouldBe("<fault>endpoint-a</fault>");
        pair.EmbeddedRawContentB.ShouldBe("<fault>endpoint-b</fault>");
        pair.EmbeddedRawContentTruncatedA.ShouldBeTrue();
        pair.EmbeddedRawContentTruncatedB.ShouldBeFalse();
    }

    [TestMethod]
    public void ReportBootstrapData_RoundTrips_BundledRawContentPath()
    {
        var bootstrapData = new ReportBootstrapData
        {
            Result = new MultiFolderComparisonResult
            {
                AllEqual = false,
                TotalPairsCompared = 1,
                FilePairResults = new List<FilePairComparisonResult>
                {
                    new()
                    {
                        File1Name = "request1.xml",
                        File2Name = "request1.xml",
                        BundledRawContentPath = "raw/pair-1-a1b2c3d4.json",
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(bootstrapData, BlazorReportSerializerOptions.Default);
        var result = JsonSerializer.Deserialize<ReportBootstrapData>(json, BlazorReportSerializerOptions.Default)!;

        result.Result!.FilePairResults.Count.ShouldBe(1);
        result.Result.FilePairResults[0].BundledRawContentPath.ShouldBe("raw/pair-1-a1b2c3d4.json");
    }
}
