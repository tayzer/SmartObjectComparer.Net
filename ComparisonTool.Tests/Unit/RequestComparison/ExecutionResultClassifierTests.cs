using ComparisonTool.Core.RequestComparison.Models;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparisonTool.Tests.Unit.RequestComparison;

[TestClass]
public class ExecutionResultClassifierTests
{
    private static RequestExecutionResult CreateResult(bool success, int statusCodeA = 0, int statusCodeB = 0, string? error = null)
    {
        return new RequestExecutionResult
        {
            Request = new RequestFileInfo
            {
                RelativePath = "test.xml",
                FilePath = "/tmp/test.xml",
                ContentType = "application/xml",
            },
            Success = success,
            StatusCodeA = statusCodeA,
            StatusCodeB = statusCodeB,
            ErrorMessage = error,
        };
    }

    // --- Classify single result tests ---

    [TestMethod]
    public void Classify_BothReturn200_ReturnsBothSuccess()
    {
        var result = CreateResult(true, 200, 200);

        var outcome = ExecutionResultClassifier.Classify(result);

        outcome.Should().Be(RequestPairOutcome.BothSuccess);
    }

    [TestMethod]
    [DataRow(200, 201)]
    [DataRow(201, 200)]
    [DataRow(204, 299)]
    public void Classify_Both2xx_ReturnsBothSuccess(int statusA, int statusB)
    {
        var result = CreateResult(true, statusA, statusB);

        var outcome = ExecutionResultClassifier.Classify(result);

        outcome.Should().Be(RequestPairOutcome.BothSuccess);
    }

    [TestMethod]
    [DataRow(200, 500)]
    [DataRow(200, 404)]
    [DataRow(201, 503)]
    [DataRow(204, 400)]
    public void Classify_OneSuccess_OneNonSuccess_ReturnsStatusCodeMismatch(int statusA, int statusB)
    {
        var result = CreateResult(true, statusA, statusB);

        var outcome = ExecutionResultClassifier.Classify(result);

        outcome.Should().Be(RequestPairOutcome.StatusCodeMismatch);
    }

    [TestMethod]
    [DataRow(500, 200)]
    [DataRow(404, 201)]
    [DataRow(503, 204)]
    public void Classify_OneNonSuccess_OneSuccess_ReturnsStatusCodeMismatch(int statusA, int statusB)
    {
        var result = CreateResult(true, statusA, statusB);

        var outcome = ExecutionResultClassifier.Classify(result);

        outcome.Should().Be(RequestPairOutcome.StatusCodeMismatch);
    }

    [TestMethod]
    [DataRow(500, 500)]
    [DataRow(404, 404)]
    [DataRow(503, 500)]
    [DataRow(400, 422)]
    public void Classify_BothNonSuccess_ReturnsBothNonSuccess(int statusA, int statusB)
    {
        var result = CreateResult(true, statusA, statusB);

        var outcome = ExecutionResultClassifier.Classify(result);

        outcome.Should().Be(RequestPairOutcome.BothNonSuccess);
    }

    [TestMethod]
    public void Classify_SuccessFalse_ReturnsOneOrBothFailed()
    {
        var result = CreateResult(false, error: "Connection timed out");

        var outcome = ExecutionResultClassifier.Classify(result);

        outcome.Should().Be(RequestPairOutcome.OneOrBothFailed);
    }

    [TestMethod]
    [DataRow(301, 301)]
    [DataRow(302, 200)]
    public void Classify_3xx_TreatedAsNonSuccess(int statusA, int statusB)
    {
        var result = CreateResult(true, statusA, statusB);

        var outcome = ExecutionResultClassifier.Classify(result);

        // 3xx are not 2xx, so:
        // (301, 301) => BothNonSuccess
        // (302, 200) => StatusCodeMismatch
        if (statusA is >= 200 and < 300 || statusB is >= 200 and < 300)
        {
            outcome.Should().Be(RequestPairOutcome.StatusCodeMismatch);
        }
        else
        {
            outcome.Should().Be(RequestPairOutcome.BothNonSuccess);
        }
    }

    // --- ClassifyAll tests ---

    [TestMethod]
    public void ClassifyAll_MixedResults_ClassifiesCorrectly()
    {
        var results = new List<RequestExecutionResult>
        {
            CreateResult(true, 200, 200),
            CreateResult(true, 200, 500),
            CreateResult(true, 500, 500),
            CreateResult(false, error: "Timeout"),
        };

        var classified = ExecutionResultClassifier.ClassifyAll(results);

        classified.Should().HaveCount(4);
        classified[0].Outcome.Should().Be(RequestPairOutcome.BothSuccess);
        classified[1].Outcome.Should().Be(RequestPairOutcome.StatusCodeMismatch);
        classified[2].Outcome.Should().Be(RequestPairOutcome.BothNonSuccess);
        classified[3].Outcome.Should().Be(RequestPairOutcome.OneOrBothFailed);
    }

    [TestMethod]
    public void ClassifyAll_SetsOutcomeReasonForSuccessfulRequests()
    {
        var results = new List<RequestExecutionResult>
        {
            CreateResult(true, 200, 500),
        };

        var classified = ExecutionResultClassifier.ClassifyAll(results);

        classified[0].OutcomeReason.Should().Be("A=200, B=500");
    }

    [TestMethod]
    public void ClassifyAll_SetsOutcomeReasonForFailedRequests()
    {
        var results = new List<RequestExecutionResult>
        {
            CreateResult(false, error: "DNS resolution failed"),
        };

        var classified = ExecutionResultClassifier.ClassifyAll(results);

        classified[0].OutcomeReason.Should().Be("Failed: DNS resolution failed");
    }

    [TestMethod]
    public void ClassifyAll_EmptyList_ReturnsEmptyList()
    {
        var classified = ExecutionResultClassifier.ClassifyAll(Array.Empty<RequestExecutionResult>());

        classified.Should().BeEmpty();
    }

    // --- Summarize tests ---

    [TestMethod]
    public void Summarize_MixedResults_CountsCorrectly()
    {
        var results = new List<RequestExecutionResult>
        {
            CreateResult(true, 200, 200),
            CreateResult(true, 201, 204),
            CreateResult(true, 200, 500),
            CreateResult(true, 500, 500),
            CreateResult(false, error: "Timeout"),
            CreateResult(false, error: "DNS"),
        };

        var classified = ExecutionResultClassifier.ClassifyAll(results);
        var summary = ExecutionResultClassifier.Summarize(classified);

        summary.TotalRequests.Should().Be(6);
        summary.BothSuccess.Should().Be(2);
        summary.StatusCodeMismatch.Should().Be(1);
        summary.BothNonSuccess.Should().Be(1);
        summary.OneOrBothFailed.Should().Be(2);
    }

    [TestMethod]
    public void Summarize_AllSuccess_ZeroNonSuccessCounts()
    {
        var results = new List<RequestExecutionResult>
        {
            CreateResult(true, 200, 200),
            CreateResult(true, 201, 204),
        };

        var classified = ExecutionResultClassifier.ClassifyAll(results);
        var summary = ExecutionResultClassifier.Summarize(classified);

        summary.TotalRequests.Should().Be(2);
        summary.BothSuccess.Should().Be(2);
        summary.StatusCodeMismatch.Should().Be(0);
        summary.BothNonSuccess.Should().Be(0);
        summary.OneOrBothFailed.Should().Be(0);
    }

    [TestMethod]
    public void Summarize_EmptyList_AllZeros()
    {
        var classified = ExecutionResultClassifier.ClassifyAll(Array.Empty<RequestExecutionResult>());
        var summary = ExecutionResultClassifier.Summarize(classified);

        summary.TotalRequests.Should().Be(0);
        summary.BothSuccess.Should().Be(0);
        summary.StatusCodeMismatch.Should().Be(0);
        summary.BothNonSuccess.Should().Be(0);
        summary.OneOrBothFailed.Should().Be(0);
    }

    // --- Boundary / edge case tests ---

    [TestMethod]
    public void Classify_StatusCode199_TreatedAsNonSuccess()
    {
        var result = CreateResult(true, 199, 200);

        var outcome = ExecutionResultClassifier.Classify(result);

        outcome.Should().Be(RequestPairOutcome.StatusCodeMismatch);
    }

    [TestMethod]
    public void Classify_StatusCode300_TreatedAsNonSuccess()
    {
        var result = CreateResult(true, 200, 300);

        var outcome = ExecutionResultClassifier.Classify(result);

        outcome.Should().Be(RequestPairOutcome.StatusCodeMismatch);
    }

    [TestMethod]
    public void Classify_StatusCode299_TreatedAsSuccess()
    {
        var result = CreateResult(true, 299, 200);

        var outcome = ExecutionResultClassifier.Classify(result);

        outcome.Should().Be(RequestPairOutcome.BothSuccess);
    }
}
