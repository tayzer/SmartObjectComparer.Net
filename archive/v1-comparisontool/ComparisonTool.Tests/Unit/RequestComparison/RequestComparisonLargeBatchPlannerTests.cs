using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Unit.RequestComparison;

[TestClass]
public class RequestComparisonLargeBatchPlannerTests
{
    [TestMethod]
    public void ShouldUseLargeBatchMode_TriggersAtThreshold()
    {
        var options = new RequestComparisonLargeBatchOptions
        {
            LargeBatchThreshold = 1000,
        };

        RequestComparisonLargeBatchPlanner.ShouldUseLargeBatchMode(999, options).ShouldBeFalse();
        RequestComparisonLargeBatchPlanner.ShouldUseLargeBatchMode(1000, options).ShouldBeTrue();
        RequestComparisonLargeBatchPlanner.ShouldUseLargeBatchMode(1001, options).ShouldBeTrue();
    }

    [TestMethod]
    public void Partition_CreatesDeterministicChunks()
    {
        var items = Enumerable.Range(1, 1200).ToList();

        var chunks = RequestComparisonLargeBatchPlanner.Partition(items, 500);

        chunks.Count.ShouldBe(3);
        chunks[0].ShouldBe(Enumerable.Range(1, 500));
        chunks[1].ShouldBe(Enumerable.Range(501, 500));
        chunks[2].ShouldBe(Enumerable.Range(1001, 200));
    }
}
