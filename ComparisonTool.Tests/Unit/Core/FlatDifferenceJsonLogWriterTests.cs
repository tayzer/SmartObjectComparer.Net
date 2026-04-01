using System.Text.Json;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparisonTool.Tests.Unit.Core;

[TestClass]
public class FlatDifferenceJsonLogWriterTests
{
    [TestMethod]
    public void TryWrite_WhenDifferencesExist_ShouldWriteFlatJsonArrayAndStoreMetadataPath()
    {
        var result = new MultiFolderComparisonResult
        {
            AllEqual = false,
            TotalPairsCompared = 1,
            FilePairResults = new List<FilePairComparisonResult>
            {
                new()
                {
                    File1Name = "Expected.xml",
                    File2Name = "Actual.xml",
                    Result = new ComparisonResult(new ComparisonConfig())
                    {
                        Differences =
                        {
                            new Difference
                            {
                                PropertyName = "Items[0].Name",
                                Object1Value = "Alpha",
                                Object2Value = "Beta",
                            },
                        },
                    },
                    Summary = new DifferenceSummary
                    {
                        AreEqual = false,
                        TotalDifferenceCount = 1,
                    },
                },
            },
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal),
        };

        FlatDifferenceJsonLogWriter.TryWrite(result, "unit_test", "flatdiffs", NullLogger.Instance);

        result.Metadata.Should().ContainKey(FlatDifferenceJsonLogWriter.MetadataKey);

        var outputPath = result.Metadata[FlatDifferenceJsonLogWriter.MetadataKey].Should().BeOfType<string>().Which;

        try
        {
            File.Exists(outputPath).Should().BeTrue();

            using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
            document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            document.RootElement.GetArrayLength().Should().Be(1);

            var firstEntry = document.RootElement[0];
            firstEntry.GetProperty("File1Name").GetString().Should().Be("Expected.xml");
            firstEntry.GetProperty("File2Name").GetString().Should().Be("Actual.xml");
            firstEntry.GetProperty("PropertyName").GetString().Should().Be("Items[0].Name");
            firstEntry.GetProperty("Object1Value").GetString().Should().Be("Alpha");
            firstEntry.GetProperty("Object2Value").GetString().Should().Be("Beta");
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [TestMethod]
    public void TryWrite_WhenRawTextDifferencesExist_ShouldIncludeRawTextEntries()
    {
        var result = new MultiFolderComparisonResult
        {
            AllEqual = false,
            TotalPairsCompared = 1,
            FilePairResults = new List<FilePairComparisonResult>
            {
                new()
                {
                    File1Name = "Request.json",
                    File2Name = "Request.json",
                    RequestRelativePath = "requests/request.json",
                    RawTextDifferences = new List<RawTextDifference>
                    {
                        new()
                        {
                            Type = RawTextDifferenceType.Modified,
                            LineNumberA = 10,
                            LineNumberB = 10,
                            TextA = "old",
                            TextB = "new",
                            Description = "Body text differs",
                        },
                    },
                    Summary = new DifferenceSummary
                    {
                        AreEqual = false,
                        TotalDifferenceCount = 1,
                    },
                },
            },
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal),
        };

        FlatDifferenceJsonLogWriter.TryWrite(result, "unit_test_raw", "rawdiffs", NullLogger.Instance);

        var outputPath = result.Metadata[FlatDifferenceJsonLogWriter.MetadataKey].Should().BeOfType<string>().Which;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
            document.RootElement.GetArrayLength().Should().Be(1);

            var firstEntry = document.RootElement[0];
            firstEntry.GetProperty("DifferenceSource").GetString().Should().Be("RawText");
            firstEntry.GetProperty("Type").GetString().Should().Be(nameof(RawTextDifferenceType.Modified));
            firstEntry.GetProperty("TextA").GetString().Should().Be("old");
            firstEntry.GetProperty("TextB").GetString().Should().Be("new");
            firstEntry.GetProperty("Description").GetString().Should().Be("Body text differs");
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [TestMethod]
    public void TryWrite_WhenStructuredDifferencesContainNullEntries_ShouldSkipNullEntries()
    {
        var result = new MultiFolderComparisonResult
        {
            AllEqual = false,
            TotalPairsCompared = 1,
            FilePairResults = new List<FilePairComparisonResult>
            {
                new()
                {
                    File1Name = "Expected.xml",
                    File2Name = "Actual.xml",
                    Result = new ComparisonResult(new ComparisonConfig())
                    {
                        Differences =
                        {
                            null!,
                            new Difference
                            {
                                PropertyName = "Items[0].Name",
                                Object1Value = "Alpha",
                                Object2Value = "Beta",
                            },
                        },
                    },
                    Summary = new DifferenceSummary
                    {
                        AreEqual = false,
                        TotalDifferenceCount = 2,
                    },
                },
            },
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal),
        };

        FlatDifferenceJsonLogWriter.TryWrite(result, "unit_test_null_entries", "skipnulls", NullLogger.Instance);

        var outputPath = result.Metadata[FlatDifferenceJsonLogWriter.MetadataKey].Should().BeOfType<string>().Which;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
            document.RootElement.GetArrayLength().Should().Be(1);

            var firstEntry = document.RootElement[0];
            firstEntry.GetProperty("DifferenceSource").GetString().Should().Be("Structured");
            firstEntry.GetProperty("PropertyName").GetString().Should().Be("Items[0].Name");
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}