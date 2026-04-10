using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using KellermanSoftware.CompareNetObjects.TypeComparers;
using Microsoft.Extensions.Logging;
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
    public void Compare_WithDuplicateCandidateKeys_ShouldRecordCollectionOrderFallbackTiming()
    {
        var logger = new TestLogger();
        var compareLogic = CreateCompareLogic(
            ignoreCollectionOrder: false,
            logger: logger,
            propertiesToIgnoreOrder: "Items");
        var phaseTimings = new ComparisonPhaseTimingContext("unit-test");

        var left = new ComplexOnlyContainer
        {
            Items = new List<ComplexOnlyItem>
            {
                new() { Child = new ComplexChild { Value = "A" } },
                new() { Child = new ComplexChild { Value = "B" } },
            },
        };

        var right = new ComplexOnlyContainer
        {
            Items = new List<ComplexOnlyItem>
            {
                new() { Child = new ComplexChild { Value = "B" } },
                new() { Child = new ComplexChild { Value = "A" } },
            },
        };

        using var scope = ComparisonPhaseTimingScope.Push(phaseTimings);

        var result = compareLogic.Compare(left, right);
        var snapshot = phaseTimings.CreateSnapshot();

        result.Should().NotBeNull();
        logger.WarningMessages.Should().ContainSingle(message => message.Contains("falling back to O(n²) comparison", StringComparison.Ordinal));
        snapshot.CollectionOrderFallbackCount.Should().Be(1);
        snapshot.CollectionOrderDeterministicOrderingMs.Should().BeGreaterThanOrEqualTo(0);
        snapshot.CollectionOrderFallbackMs.Should().BeGreaterThanOrEqualTo(0);
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

    [TestMethod]
    public void Compare_WithCertificationNumberIdentifiers_ShouldAvoidFallbackAndTreatReorderedItemsAsEqual()
    {
        var logger = new TestLogger();
        var compareLogic = CreateCompareLogic(
            ignoreCollectionOrder: true,
            ignoredPropertyPaths: new[] { "Items[*].Name", "Items[*].IssuingAuthority", "Items[*].ValidUntil" },
            applyIgnoredPropertiesToComparison: true,
            logger: logger);

        var left = new CertificationContainer
        {
            Items = new List<CertificationItem>
            {
                new() { CertificationNumber = "CERT-002", Name = "B", IssuingAuthority = "AUTH-B" },
                new() { CertificationNumber = "CERT-001", Name = "A", IssuingAuthority = "AUTH-A" },
            },
        };

        var right = new CertificationContainer
        {
            Items = new List<CertificationItem>
            {
                new() { CertificationNumber = "CERT-001", Name = "A", IssuingAuthority = "AUTH-A" },
                new() { CertificationNumber = "CERT-002", Name = "B", IssuingAuthority = "AUTH-B" },
            },
        };

        var result = compareLogic.Compare(left, right);

        result.AreEqual.Should().BeTrue();
        result.Differences.Should().BeEmpty();
        logger.WarningMessages.Should().BeEmpty();
    }

    [TestMethod]
    public void Compare_WithFullyIgnoredCollectionItems_ShouldShortCircuitWithoutFallbackWarning()
    {
        var logger = new TestLogger();
        var compareLogic = CreateCompareLogic(
            ignoreCollectionOrder: true,
            ignoredPropertyPaths: new[] { "Items[*]" },
            applyIgnoredPropertiesToComparison: true,
            logger: logger);

        var left = new NamedContainer
        {
            Items = new List<NamedItem>
            {
                new() { Name = "first", Value = "A" },
                new() { Name = "second", Value = "B" },
            },
        };

        var right = new NamedContainer
        {
            Items = new List<NamedItem>
            {
                new() { Name = "different-second", Value = "different-B" },
                new() { Name = "different-first", Value = "different-A" },
            },
        };

        var result = compareLogic.Compare(left, right);

        result.AreEqual.Should().BeTrue();
        result.Differences.Should().BeEmpty();
        logger.WarningMessages.Should().BeEmpty();
    }

    private static CompareLogic CreateCompareLogic(
        bool ignoreCollectionOrder,
        string[]? ignoredPropertyPaths = null,
        bool applyIgnoredPropertiesToComparison = false,
        ILogger? logger = null,
        params string[] propertiesToIgnoreOrder)
    {
        var compareLogic = new CompareLogic();
        compareLogic.Config.IgnoreCollectionOrder = ignoreCollectionOrder;

        if (applyIgnoredPropertiesToComparison && ignoredPropertyPaths != null)
        {
            foreach (var ignoredPropertyPath in ignoredPropertyPaths)
            {
                compareLogic.Config.MembersToIgnore.Add(ignoredPropertyPath);
            }
        }

        compareLogic.Config.CustomComparers = new List<BaseTypeComparer>
        {
            new PropertySpecificCollectionOrderComparer(
                RootComparerFactory.GetRootComparer(),
                propertiesToIgnoreOrder,
                logger ?? NullLogger.Instance,
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

    private sealed class CertificationContainer
    {
        public List<CertificationItem> Items { get; set; } = new List<CertificationItem>();
    }

    private sealed class NamedContainer
    {
        public List<NamedItem> Items { get; set; } = new List<NamedItem>();
    }

    private sealed class ComplexOnlyContainer
    {
        public List<ComplexOnlyItem> Items { get; set; } = new List<ComplexOnlyItem>();
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

    private sealed class ComplexOnlyItem
    {
        public ComplexChild? Child
        {
            get; set;
        }
    }

    private sealed class ComplexChild
    {
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

    private sealed class CertificationItem
    {
        public string CertificationNumber
        {
            get; set;
        } = string.Empty;

        public string? Name
        {
            get; set;
        }

        public string? IssuingAuthority
        {
            get; set;
        }

        public DateTime? ValidUntil
        {
            get; set;
        }
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> WarningMessages { get; } = new List<string>();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningMessages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new NullScope();

        public void Dispose()
        {
        }
    }
}