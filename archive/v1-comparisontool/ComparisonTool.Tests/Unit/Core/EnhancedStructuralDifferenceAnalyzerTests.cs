using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Unit.Core;

[TestClass]
public class EnhancedStructuralDifferenceAnalyzerTests
{
    [TestMethod]
    public void AnalyzeStructuralPatterns_WhenIndexedValueChangesDoNotSwapAcrossIndices_ShouldClassifyAsValueNotOrder()
    {
        var analysis = Analyze(
            new Difference
            {
                PropertyName = "OrderData.Items[0].Product.Name",
                Object1Value = "Alpha",
                Object2Value = "Alpha Updated",
            },
            new Difference
            {
                PropertyName = "OrderData.Items[1].Product.Name",
                Object1Value = "Beta",
                Object2Value = "Beta Updated",
            });

        analysis.ElementOrderDifferences.ShouldBeEmpty();
        analysis.AllPatterns.Count(pattern =>
            pattern.FullPattern == "OrderData.Items[*].Product.Name" &&
            pattern.Category == DifferenceCategory.GeneralValueChanged &&
            pattern.OccurenceCount == 2).ShouldBe(1);
        analysis.FileClassification.FileCounts["Order"].ShouldBe(0);
        analysis.FileClassification.FileCounts["Value"].ShouldBe(1);
    }

    [TestMethod]
    public void AnalyzeStructuralPatterns_WhenCollectionMemberValuesSwapAcrossIndices_ShouldClassifyAsOrder()
    {
        var analysis = Analyze(
            new Difference
            {
                PropertyName = "Items[0].Id",
                Object1Value = "1",
                Object2Value = "2",
            },
            new Difference
            {
                PropertyName = "Items[0].Value",
                Object1Value = "A",
                Object2Value = "B",
            },
            new Difference
            {
                PropertyName = "Items[1].Id",
                Object1Value = "2",
                Object2Value = "1",
            },
            new Difference
            {
                PropertyName = "Items[1].Value",
                Object1Value = "B",
                Object2Value = "A",
            });

        analysis.ElementOrderDifferences.Count.ShouldBe(1);
        var pattern = analysis.ElementOrderDifferences.Single();
        pattern.FullPattern.ShouldBe("Items[Order]");
        pattern.OccurenceCount.ShouldBe(4);
        pattern.FileCount.ShouldBe(1);
        analysis.FileClassification.FileCounts["Order"].ShouldBe(1);
        analysis.FileClassification.FileCounts["Value"].ShouldBe(0);
    }

    [TestMethod]
    public void AnalyzeStructuralPatterns_WhenIgnoreCollectionOrderIsEnabled_ShouldSuppressOrderClassification()
    {
        var analysis = Analyze(
            true,
            new Difference
            {
                PropertyName = "Items[0].Id",
                Object1Value = "1",
                Object2Value = "2",
            },
            new Difference
            {
                PropertyName = "Items[0].Value",
                Object1Value = "A",
                Object2Value = "B",
            },
            new Difference
            {
                PropertyName = "Items[1].Id",
                Object1Value = "2",
                Object2Value = "1",
            },
            new Difference
            {
                PropertyName = "Items[1].Value",
                Object1Value = "B",
                Object2Value = "A",
            });

        analysis.ElementOrderDifferences.ShouldBeEmpty();
        analysis.AllPatterns.Any(pattern => pattern.Category == DifferenceCategory.CollectionItemChanged).ShouldBeFalse();
        analysis.FileClassification.FileCounts["Order"].ShouldBe(0);
        analysis.FileClassification.FileCounts["Value"].ShouldBe(1);
    }

    [TestMethod]
    public void AnalyzeStructuralPatterns_WhenPrimitiveCollectionItemsSwapAcrossIndices_ShouldClassifyAsOrder()
    {
        var analysis = Analyze(
            new Difference
            {
                PropertyName = "Values[0]",
                Object1Value = "A",
                Object2Value = "B",
            },
            new Difference
            {
                PropertyName = "Values[1]",
                Object1Value = "B",
                Object2Value = "A",
            });

        analysis.ElementOrderDifferences.Count.ShouldBe(1);
        analysis.ElementOrderDifferences[0].FullPattern.ShouldBe("Values[Order]");
        analysis.ElementOrderDifferences[0].OccurenceCount.ShouldBe(2);
        analysis.FileClassification.FileCounts["Order"].ShouldBe(1);
    }

    [TestMethod]
    public void AnalyzeStructuralPatterns_WhenPrimitiveCollectionItemsChangeInPlace_ShouldClassifyAsValueNotOrder()
    {
        var analysis = Analyze(
            new Difference
            {
                PropertyName = "Values[0]",
                Object1Value = "A",
                Object2Value = "B",
            },
            new Difference
            {
                PropertyName = "Values[1]",
                Object1Value = "C",
                Object2Value = "D",
            });

        analysis.ElementOrderDifferences.ShouldBeEmpty();
        analysis.AllPatterns.Count(pattern =>
            pattern.FullPattern == "Values[*]" &&
            pattern.Category == DifferenceCategory.GeneralValueChanged &&
            pattern.OccurenceCount == 2).ShouldBe(1);
        analysis.FileClassification.FileCounts["Order"].ShouldBe(0);
        analysis.FileClassification.FileCounts["Value"].ShouldBe(1);
    }

    [TestMethod]
    public void AnalyzeStructuralPatterns_WhenOnlyNonIdentifierFeatureFlagValuesSwapAcrossIndices_ShouldClassifyAsValueNotOrder()
    {
        var analysis = Analyze(
            new Difference
            {
                PropertyName = "Metadata.EnabledFeatures[0].Enabled",
                Object1Value = "False",
                Object2Value = "True",
            },
            new Difference
            {
                PropertyName = "Metadata.EnabledFeatures[1].Enabled",
                Object1Value = "True",
                Object2Value = "False",
            });

        analysis.ElementOrderDifferences.ShouldBeEmpty();
        analysis.AllPatterns.Count(pattern =>
            pattern.FullPattern == "Metadata.EnabledFeatures[*].Enabled" &&
            pattern.OccurenceCount == 2).ShouldBe(1);
        analysis.FileClassification.FileCounts["Order"].ShouldBe(0);
        analysis.FileClassification.FileCounts["Value"].ShouldBe(1);
    }

    [TestMethod]
    public void AnalyzeStructuralPatterns_WhenOnlyNonIdentifierAddressFieldsSwapAcrossIndices_ShouldClassifyAsValueNotOrder()
    {
        var analysis = Analyze(
            new Difference
            {
                PropertyName = "OrderData.Customer.Addresses[0].Type",
                Object1Value = "Home",
                Object2Value = "Work",
            },
            new Difference
            {
                PropertyName = "OrderData.Customer.Addresses[1].Type",
                Object1Value = "Work",
                Object2Value = "Home",
            },
            new Difference
            {
                PropertyName = "OrderData.Customer.Addresses[0].IsDefault",
                Object1Value = "True",
                Object2Value = "False",
            },
            new Difference
            {
                PropertyName = "OrderData.Customer.Addresses[1].IsDefault",
                Object1Value = "False",
                Object2Value = "True",
            });

        analysis.ElementOrderDifferences.ShouldBeEmpty();
        analysis.AllPatterns.Any(pattern =>
            pattern.FullPattern == "OrderData.Customer.Addresses[*].Type" &&
            pattern.Category == DifferenceCategory.GeneralValueChanged &&
            pattern.OccurenceCount == 2).ShouldBeTrue();
        analysis.AllPatterns.Any(pattern =>
            pattern.FullPattern == "OrderData.Customer.Addresses[*].IsDefault" &&
            pattern.Category == DifferenceCategory.GeneralValueChanged &&
            pattern.OccurenceCount == 2).ShouldBeTrue();
        analysis.FileClassification.FileCounts["Order"].ShouldBe(0);
        analysis.FileClassification.FileCounts["Value"].ShouldBe(1);
    }

    [TestMethod]
    public void AnalyzeStructuralPatterns_WhenNestedCollectionMemberValuesSwapAcrossIndices_ShouldClassifyInnermostCollectionAsOrder()
    {
        var analysis = Analyze(
            new Difference
            {
                PropertyName = "Items[0].Attributes[0].Name",
                Object1Value = "Red",
                Object2Value = "Blue",
            },
            new Difference
            {
                PropertyName = "Items[0].Attributes[1].Name",
                Object1Value = "Blue",
                Object2Value = "Red",
            });

        analysis.ElementOrderDifferences.Count.ShouldBe(1);
        analysis.ElementOrderDifferences[0].FullPattern.ShouldBe("Items[*].Attributes[Order]");
        analysis.ElementOrderDifferences[0].OccurenceCount.ShouldBe(2);
        analysis.FileClassification.FileCounts["Order"].ShouldBe(1);
    }

    [TestMethod]
    public void AnalyzeStructuralPatterns_WhenNestedReorderExistsBesideSiblingValueChanges_ShouldScopeOrderToTheMatchingParent()
    {
        var analysis = Analyze(
            new Difference
            {
                PropertyName = "Items[0].Attributes[0].Name",
                Object1Value = "Red",
                Object2Value = "Blue",
            },
            new Difference
            {
                PropertyName = "Items[0].Attributes[1].Name",
                Object1Value = "Blue",
                Object2Value = "Red",
            },
            new Difference
            {
                PropertyName = "Items[1].Attributes[0].Name",
                Object1Value = "Small",
                Object2Value = "Large",
            },
            new Difference
            {
                PropertyName = "Items[1].Attributes[1].Name",
                Object1Value = "Medium",
                Object2Value = "XL",
            });

        analysis.ElementOrderDifferences.Count.ShouldBe(1);
        analysis.ElementOrderDifferences[0].FullPattern.ShouldBe("Items[*].Attributes[Order]");
        analysis.ElementOrderDifferences[0].OccurenceCount.ShouldBe(2);
        analysis.AllPatterns.Count(pattern =>
            pattern.FullPattern == "Items[*].Attributes[*].Name" &&
            pattern.Category == DifferenceCategory.GeneralValueChanged &&
            pattern.OccurenceCount == 2).ShouldBe(1);
    }

    [TestMethod]
    public void AnalyzeStructuralPatterns_WhenRequestPairsHaveSameArtifactNames_ShouldUseRequestRelativePathAsAffectedFileIdentity()
    {
        var analysis = AnalyzePairs(
            CreatePair(
                "alpha/lookup.xml",
                new Difference
                {
                    PropertyName = "Response.CustomerName",
                    Object1Value = "Alpha",
                    Object2Value = "Beta",
                }),
            CreatePair(
                "beta/lookup.xml",
                new Difference
                {
                    PropertyName = "Response.CustomerName",
                    Object1Value = "Alpha",
                    Object2Value = "Beta",
                }));

        var pattern = analysis.AllPatterns.Single(pattern =>
            pattern.FullPattern == "Response.CustomerName" &&
            pattern.Category == DifferenceCategory.TextContentChanged);

        pattern.FileCount.ShouldBe(2);
        pattern.AffectedFiles.OrderBy(file => file, StringComparer.Ordinal).ToArray()
            .ShouldBe(new[] { "alpha/lookup.xml", "beta/lookup.xml" });
        analysis.FileClassification.FilesByCategory["Value"]
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray()
            .ShouldBe(new[] { "alpha/lookup.xml", "beta/lookup.xml" });
    }

    private static EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult Analyze(params Difference[] differences)
        => Analyze(false, differences);

    private static EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult Analyze(bool suppressOrderClassification, params Difference[] differences)
    {
        var comparisonResult = new ComparisonResult(new ComparisonConfig());
        comparisonResult.Differences.AddRange(differences);

        var folderResult = new MultiFolderComparisonResult
        {
            AllEqual = false,
            TotalPairsCompared = 1,
            FilePairResults = new List<FilePairComparisonResult>
            {
                new()
                {
                    File1Name = "Actual.xml",
                    File2Name = "Expected.xml",
                    Result = comparisonResult,
                    Summary = new DifferenceSummary
                    {
                        AreEqual = false,
                        TotalDifferenceCount = differences.Length,
                    },
                },
            },
        };

        var analyzer = new EnhancedStructuralDifferenceAnalyzer(folderResult, NullLogger.Instance, suppressOrderClassification);
        return analyzer.AnalyzeStructuralPatterns();
    }


    private static FilePairComparisonResult CreatePair(string requestRelativePath, params Difference[] differences)
    {
        var comparisonResult = new ComparisonResult(new ComparisonConfig());
        comparisonResult.Differences.AddRange(differences);

        return new FilePairComparisonResult
        {
            File1Name = "lookup.json",
            File2Name = "lookup.json",
            RequestRelativePath = requestRelativePath,
            Result = comparisonResult,
            Summary = new DifferenceSummary
            {
                AreEqual = false,
                TotalDifferenceCount = differences.Length,
            },
        };
    }

    private static EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult AnalyzePairs(
        params FilePairComparisonResult[] pairs)
    {
        var folderResult = new MultiFolderComparisonResult
        {
            AllEqual = false,
            TotalPairsCompared = pairs.Length,
            FilePairResults = pairs.ToList(),
        };

        var analyzer = new EnhancedStructuralDifferenceAnalyzer(folderResult, NullLogger.Instance);
        return analyzer.AnalyzeStructuralPatterns();
    }
}