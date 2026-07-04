using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Tests;

[TestClass]
public sealed class RequestPairResultTests
{
    [TestMethod]
    public void Create_WhenRequestRelativePathIsRooted_ThrowsArgumentException()
    {
        AssertThrows<ArgumentException>(() => new RequestItem(@"C:\requests\one.json"));
    }

    [TestMethod]
    public void Create_WhenRequestRelativePathContainsParentTraversal_ThrowsArgumentException()
    {
        AssertThrows<ArgumentException>(() => new RequestItem("../one.json"));
    }

    [TestMethod]
    public void Classify_WhenBothSuccessHashesMatch_ReturnsEqual()
    {
        RequestPairResult result = RequestPairResult.Classify(
            CreateRequest(),
            CreateResponse(EndpointSlot.A, 200, 12, "abc"),
            CreateResponse(EndpointSlot.B, 200, 12, "abc"));

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public void Classify_WhenBothSuccessHashesDiffer_ReturnsDifferent()
    {
        RequestPairResult result = RequestPairResult.Classify(
            CreateRequest(),
            CreateResponse(EndpointSlot.A, 200, 12, "abc"),
            CreateResponse(EndpointSlot.B, 200, 13, "def"));

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
    }

    [TestMethod]
    public void Classify_WhenOnlyOneEndpointSucceeds_ReturnsStatusCodeMismatch()
    {
        RequestPairResult result = RequestPairResult.Classify(
            CreateRequest(),
            CreateResponse(EndpointSlot.A, 200, 12, "abc"),
            CreateResponse(EndpointSlot.B, 500, 12, "abc"));

        Assert.AreEqual(RequestPairOutcome.StatusCodeMismatch, result.Outcome);
    }

    [TestMethod]
    public void Summarize_WhenPairResultsContainMixedOutcomes_ReturnsExpectedCounts()
    {
        RequestPairResult[] results = new[]
        {
            new RequestPairResult("equal.json", RequestPairOutcome.Equal),
            new RequestPairResult("different.json", RequestPairOutcome.Different),
            new RequestPairResult("status.json", RequestPairOutcome.StatusCodeMismatch),
            new RequestPairResult("non-success.json", RequestPairOutcome.BothNonSuccess),
            new RequestPairResult("failed.json", RequestPairOutcome.ExecutionFailed, errorMessage: "Timeout."),
        };

        RunResultSummary summary = RequestPairResult.Summarize(results, new RunDetailReference("details/index.json"));

        Assert.AreEqual(5, summary.TotalPairs);
        Assert.AreEqual(1, summary.EqualPairs);
        Assert.AreEqual(1, summary.DifferentPairs);
        Assert.AreEqual(1, summary.StatusCodeMismatchPairs);
        Assert.AreEqual(1, summary.BothNonSuccessPairs);
        Assert.AreEqual(1, summary.ErrorPairs);
        Assert.IsNotNull(summary.DetailIndexReference);
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but got {ex.GetType().Name}.");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    private static RequestItem CreateRequest() => new RequestItem("request.json", "application/json", 14);

    private static ResponseArtifactMetadata CreateResponse(
        EndpointSlot endpoint,
        int statusCode,
        long contentLength,
        string sha256) =>
        new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference($"artifact-{endpoint}", "application/json"),
            statusCode,
            "application/json",
            contentLength,
            sha256);
}
