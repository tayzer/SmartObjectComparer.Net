using System.Text.Json;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization.BlazorReport;
using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        result.Result!.AllEqual.Should().Be(false);
        result.Result.TotalPairsCompared.Should().Be(1);
        result.Result.FilePairResults[0].File1Name.Should().Be("expected.xml");
        result.Result.FilePairResults[0].Result!.Differences[0].PropertyName.Should().Be("Root.Name");
        result.Result.FilePairResults[0].Result!.Differences[0].Object1Value.Should().Be("Alice");
        result.Result.FilePairResults[0].Result!.Differences[0].Object2Value.Should().Be("Bob");
        result.Metadata!.ReportId.Should().Be("test-report-001");
        result.Metadata.ElapsedSeconds.Should().Be(1.234);
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
        roundTripped.PropertyName.Should().Be("Root.Items[0].Value");
        roundTripped.Object1Value.Should().Be("100");
        roundTripped.Object2Value.Should().Be("200");
        roundTripped.ChildPropertyName.Should().Be("Value");

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
        result.EnhancedAnalysis.Should().BeNull();
        result.SemanticAnalysis.Should().BeNull();
        result.Result!.FilePairResults.Should().BeEmpty();
        result.Result.AllEqual.Should().BeTrue();
        result.Metadata!.ReportId.Should().Be("empty-test");
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
        m.ReportId.Should().Be("rpt-42");
        m.GeneratedAt.Should().Be("2026-04-15T12:30:00Z");
        m.Command.Should().Be("request");
        m.ModelName.Should().Be("ComplexOrderResponse");
        m.Directory1.Should().Be(@"C:\Dir1");
        m.Directory2.Should().Be(@"C:\Dir2");
        m.EndpointA.Should().Be("https://api-a.example.com");
        m.EndpointB.Should().Be("https://api-b.example.com");
        m.JobId.Should().Be("job-99");
        m.ElapsedSeconds.Should().Be(42.567);
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

        result.Result!.FilePairResults.Should().ContainSingle();
        var pair = result.Result.FilePairResults[0];
        pair.HasEmbeddedRawContent.Should().BeTrue();
        pair.EmbeddedRawContentA.Should().Be("<fault>endpoint-a</fault>");
        pair.EmbeddedRawContentB.Should().Be("<fault>endpoint-b</fault>");
        pair.EmbeddedRawContentTruncatedA.Should().BeTrue();
        pair.EmbeddedRawContentTruncatedB.Should().BeFalse();
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

        result.Result!.FilePairResults.Should().ContainSingle();
        result.Result.FilePairResults[0].BundledRawContentPath.Should().Be("raw/pair-1-a1b2c3d4.json");
    }
}
