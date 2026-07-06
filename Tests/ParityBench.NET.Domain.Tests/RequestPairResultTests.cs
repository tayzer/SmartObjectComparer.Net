using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Comparison;
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
    public void Classify_WhenStatusCodesMismatch_ReturnsStatusCodeMismatchWithStatusDifference()
    {
        RequestPairResult result = RequestPairResult.FromRawTextComparison(
            CreateRequest(),
            CreateResponse(EndpointSlot.A, 200, 12, "abc"),
            CreateResponse(EndpointSlot.B, 500, 12, "abc"),
            new[] { new ComparisonDifference("HttpStatus", "200", "500", "Status differs.") });

        Assert.AreEqual(RequestPairOutcome.StatusCodeMismatch, result.Outcome);
        Assert.AreEqual("HttpStatus", result.Differences[0].PropertyPath);
        Assert.IsNull(result.AreEqual);
        Assert.IsTrue(result.OutcomeMessage?.Contains("200", StringComparison.Ordinal) == true);
    }


    [TestMethod]
    public void Classify_WhenBothEndpointsAreNonSuccess_ReturnsBothNonSuccess()
    {
        RequestPairResult result = RequestPairResult.FromRawTextComparison(
            CreateRequest(),
            CreateResponse(EndpointSlot.A, 500, 12, "abc"),
            CreateResponse(EndpointSlot.B, 503, 12, "abc"),
            Array.Empty<ComparisonDifference>());

        Assert.AreEqual(RequestPairOutcome.BothNonSuccess, result.Outcome);
        Assert.AreEqual(0, result.DifferenceCount);
        Assert.IsNull(result.AreEqual);
        Assert.IsTrue(result.OutcomeMessage?.Contains("non-success", StringComparison.OrdinalIgnoreCase) == true);
    }


    [TestMethod]
    public void Create_WhenOutcomeMessageIsWhitespace_StoresNull()
    {
        RequestPairResult result = new RequestPairResult(
            "one.json",
            RequestPairOutcome.BothNonSuccess,
            outcomeMessage: " ");

        Assert.IsNull(result.OutcomeMessage);
    }


    [TestMethod]
    public void Summarize_WhenResultsContainStatusAndNonSuccess_CountsDedicatedBuckets()
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


    [TestMethod]
    public void Create_WhenExecutionMetricsAreProvided_PreservesMetricValues()
    {
        RunExecutionMetrics metrics = new RunExecutionMetrics(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(6),
            TimeSpan.FromMilliseconds(2),
            TimeSpan.FromMilliseconds(2),
            requestCount: 3,
            maxConcurrency: 2,
            responseBytesWritten: 42);

        RunResultSummary summary = RequestPairResult.Summarize(
            new[] { new RequestPairResult("equal.json", RequestPairOutcome.Equal) },
            executionMetrics: metrics);

        Assert.AreEqual(metrics, summary.ExecutionMetrics);
        Assert.AreEqual(3, summary.ExecutionMetrics?.RequestCount);
        Assert.AreEqual(42, summary.ExecutionMetrics?.ResponseBytesWritten);
    }


    [TestMethod]
    public void Create_WhenSummaryOmitsExecutionMetrics_RemainsBackwardCompatible()
    {
        RunResultSummary summary = RequestPairResult.Summarize(
            new[] { new RequestPairResult("equal.json", RequestPairOutcome.Equal) });

        Assert.IsNull(summary.ExecutionMetrics);
    }

    [TestMethod]
    public void StaticReportJson_WhenFocusedRawContentExists_RoundTripsMetadata()
    {
        RequestPairResult result = new RequestPairResult(
            "one.json",
            RequestPairOutcome.Different,
            CreateResponse(EndpointSlot.A, 200, 12, "abc"),
            CreateResponse(EndpointSlot.B, 200, 13, "def"),
            focusedResponseA: CreateResponse(EndpointSlot.A, 200, 8, "focused-a"),
            focusedResponseB: CreateResponse(EndpointSlot.B, 200, 9, "focused-b"),
            focusedRawContentIgnorePaths: new[] { "Customer.Token" });

        string json = System.Text.Json.JsonSerializer.Serialize(result, ParityBench.NET.Domain.Reports.StaticReportJsonOptions.Create());
        RequestPairResult? roundTripped = System.Text.Json.JsonSerializer.Deserialize<RequestPairResult>(json, ParityBench.NET.Domain.Reports.StaticReportJsonOptions.Create());

        Assert.IsNotNull(roundTripped);
        Assert.IsTrue(roundTripped.HasFocusedRawContent);
        Assert.AreEqual("artifact-A", roundTripped.FocusedResponseA?.Artifact.ArtifactId);
        Assert.AreEqual("artifact-B", roundTripped.FocusedResponseB?.Artifact.ArtifactId);
        CollectionAssert.Contains(roundTripped.FocusedRawContentIgnorePaths.ToList(), "Customer.Token");
    }


    [TestMethod]
    public void StaticReportJson_WhenFocusedFieldsAreMissing_LoadsBackwardCompatiblePair()
    {
        const string json = """
            {
              "relativePath": "one.json",
              "outcome": "Equal",
              "responseA": null,
              "responseB": null,
              "errorMessage": null,
              "areEqual": true,
              "differenceCount": 0,
              "differences": [],
              "outcomeMessage": null,
              "rawTextDifferences": []
            }
            """;

        RequestPairResult? result = System.Text.Json.JsonSerializer.Deserialize<RequestPairResult>(json, ParityBench.NET.Domain.Reports.StaticReportJsonOptions.Create());

        Assert.IsNotNull(result);
        Assert.IsFalse(result.HasFocusedRawContent);
        Assert.AreEqual(0, result.FocusedRawContentIgnorePaths.Count);
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

