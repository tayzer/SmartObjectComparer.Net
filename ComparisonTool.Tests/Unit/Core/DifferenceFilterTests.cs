using System.Collections.Generic;
using System.Linq;
using ComparisonTool.Core.Comparison.Utilities;
using KellermanSoftware.CompareNetObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Unit.Core;

[TestClass]
public class DifferenceFilterTests
{
    [TestMethod]
    public void FilterDuplicateDifferences_WhenParentObjectDifferenceHasSpecificChildDifference_RemovesParentDifference()
    {
        var result = new ComparisonResult(new ComparisonConfig())
        {
            Differences = new List<Difference>
            {
                new ()
                {
                    PropertyName = "Payload.Requests[0]",
                    Object1Value = "{ Id = 1, Name = Alice }",
                    Object2Value = "{ Id = 1, Name = Bob }",
                },
                new ()
                {
                    PropertyName = "Payload.Requests[0].Name",
                    Object1Value = "Alice",
                    Object2Value = "Bob",
                },
            },
        };

        var filteredResult = DifferenceFilter.FilterDuplicateDifferences(result);

        filteredResult.Differences.Count.ShouldBe(1);
        filteredResult.Differences.Single().PropertyName.ShouldBe("Payload.Requests[0].Name");
        filteredResult.Differences.Single().Object1Value.ShouldBe("Alice");
        filteredResult.Differences.Single().Object2Value.ShouldBe("Bob");
    }
}