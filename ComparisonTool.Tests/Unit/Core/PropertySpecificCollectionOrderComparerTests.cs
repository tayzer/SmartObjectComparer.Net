using ComparisonTool.Core.Comparison.Configuration;
using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using KellermanSoftware.CompareNetObjects.TypeComparers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparisonTool.Tests.Unit.Core;

[TestClass]
public class PropertySpecificCollectionOrderComparerTests
{
    [TestMethod]
    public void Compare_WithGlobalIgnoreCollectionOrder_ShouldTreatReorderedStableIdsAsEqual()
    {
        var compareLogic = CreateCompareLogic(ignoreCollectionOrder: true);

        var left = new IdentifiedContainer
        {
            Items = new List<IdentifiedItem>
            {
                new() { Id = 2, Value = "two" },
                new() { Id = 1, Value = "one" },
            },
        };

        var right = new IdentifiedContainer
        {
            Items = new List<IdentifiedItem>
            {
                new() { Id = 1, Value = "one" },
                new() { Id = 2, Value = "two" },
            },
        };

        var result = compareLogic.Compare(left, right);

        result.AreEqual.Should().BeTrue();
        result.Differences.Should().BeEmpty();
    }

    [TestMethod]
    public void Compare_WithGlobalIgnoreCollectionOrder_ShouldRecognizeCommonDomainIdentifierNames()
    {
        var compareLogic = CreateCompareLogic(ignoreCollectionOrder: true);

        var left = new DomainIdentifierContainer
        {
            Items = new List<DomainIdentifierItem>
            {
                new() { ItemId = "ITEM-02", Value = "two" },
                new() { ItemId = "ITEM-01", Value = "one" },
            },
        };

        var right = new DomainIdentifierContainer
        {
            Items = new List<DomainIdentifierItem>
            {
                new() { ItemId = "ITEM-01", Value = "one" },
                new() { ItemId = "ITEM-02", Value = "two" },
            },
        };

        var result = compareLogic.Compare(left, right);

        result.AreEqual.Should().BeTrue();
        result.Differences.Should().BeEmpty();
    }

    [TestMethod]
    public void Compare_WithGlobalIgnoreCollectionOrder_ShouldHandlePrimitiveCollectionsSafely()
    {
        var compareLogic = CreateCompareLogic(ignoreCollectionOrder: true);

        var left = new PrimitiveContainer
        {
            Values = new List<int> { 3, 1, 2 },
        };

        var right = new PrimitiveContainer
        {
            Values = new List<int> { 2, 3, 1 },
        };

        var result = compareLogic.Compare(left, right);

        result.AreEqual.Should().BeTrue();
        result.Differences.Should().BeEmpty();
    }

    [TestMethod]
    public void Compare_WithDuplicateCandidateKeys_ShouldFallBackToDefaultUnorderedMatching()
    {
        var compareLogic = CreateCompareLogic(ignoreCollectionOrder: true);

        var left = new NamedContainer
        {
            Items = new List<NamedItem>
            {
                new() { Name = "duplicate", Value = "A" },
                new() { Name = "duplicate", Value = "B" },
            },
        };

        var right = new NamedContainer
        {
            Items = new List<NamedItem>
            {
                new() { Name = "duplicate", Value = "B" },
                new() { Name = "duplicate", Value = "A" },
            },
        };

        var result = compareLogic.Compare(left, right);

        result.AreEqual.Should().BeTrue();
        result.Differences.Should().BeEmpty();
    }

    [TestMethod]
    public void Compare_WithIgnoredIdentifierProperty_ShouldUseNonIgnoredPropertiesForAlignment()
    {
        var compareLogic = CreateCompareLogic(
            ignoreCollectionOrder: true,
            ignoredPropertyPaths: new[] { "Items[*].Id" });

        var left = new IdentifiedContainer
        {
            Items = new List<IdentifiedItem>
            {
                new() { Id = 101, Value = "303 Pine Rd, Springfield" },
                new() { Id = 102, Value = "101 Main St, Springfield" },
                new() { Id = 103, Value = "202 Oak Ave, Springfield" },
            },
        };

        var right = new IdentifiedContainer
        {
            Items = new List<IdentifiedItem>
            {
                new() { Id = 101, Value = "101 Main St, Springfield" },
                new() { Id = 102, Value = "202 Oak Ave, Springfield" },
                new() { Id = 103, Value = "303 Pine Rd, Springfield" },
            },
        };

        var result = compareLogic.Compare(left, right);

        result.AreEqual.Should().BeFalse();
        result.Differences.Should().NotBeEmpty();
        result.Differences.Should().NotContain(d => d.PropertyName.Contains("Value"));
        result.Differences.Should().OnlyContain(d => d.PropertyName.Contains(".Id", StringComparison.Ordinal));
    }

    private static CompareLogic CreateCompareLogic(
        bool ignoreCollectionOrder,
        string[]? ignoredPropertyPaths = null,
        params string[] propertiesToIgnoreOrder)
    {
        var compareLogic = new CompareLogic();
        compareLogic.Config.IgnoreCollectionOrder = ignoreCollectionOrder;
        compareLogic.Config.CustomComparers = new List<BaseTypeComparer>
        {
            new PropertySpecificCollectionOrderComparer(
                RootComparerFactory.GetRootComparer(),
                propertiesToIgnoreOrder,
                NullLogger.Instance,
                applyGlobally: ignoreCollectionOrder,
                ignoredPropertyPatterns: ignoredPropertyPaths),
        };

        return compareLogic;
    }

    private sealed class IdentifiedContainer
    {
        public List<IdentifiedItem> Items { get; set; } = new List<IdentifiedItem>();
    }

    private sealed class PrimitiveContainer
    {
        public List<int> Values { get; set; } = new List<int>();
    }

    private sealed class DomainIdentifierContainer
    {
        public List<DomainIdentifierItem> Items { get; set; } = new List<DomainIdentifierItem>();
    }

    private sealed class NamedContainer
    {
        public List<NamedItem> Items { get; set; } = new List<NamedItem>();
    }

    private sealed class IdentifiedItem
    {
        public int Id
        {
            get; set;
        }

        public string? Value
        {
            get; set;
        }
    }

    private sealed class NamedItem
    {
        public string? Name
        {
            get; set;
        }

        public string? Value
        {
            get; set;
        }
    }

    private sealed class DomainIdentifierItem
    {
        public string ItemId
        {
            get; set;
        } = string.Empty;

        public string? Value
        {
            get; set;
        }
    }
}