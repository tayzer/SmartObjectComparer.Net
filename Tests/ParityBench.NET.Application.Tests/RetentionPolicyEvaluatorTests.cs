using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Application.Tests;

[TestClass]
public sealed class RetentionPolicyEvaluatorTests
{
    private readonly RetentionPolicyEvaluator evaluator = new RetentionPolicyEvaluator();

    [TestMethod]
    public void Evaluate_WhenUsingDefaultMode_MapsOutcomesToPolicyMatrix()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RetentionPolicyEvaluationRequest request = new RetentionPolicyEvaluationRequest(
            RetentionMode.TrimmedEqualsAndIgnoredPaths,
            NonSuccessRetentionOverride.KeepBounded,
            14,
            10_000,
            50_000,
            0,
            now,
            now,
            new[]
            {
                new RetentionPolicyEvaluationItem(0, PairRetentionClass.Equal, 0),
                new RetentionPolicyEvaluationItem(1, PairRetentionClass.Different, 0),
                new RetentionPolicyEvaluationItem(2, PairRetentionClass.ExecutionFailed, 100),
                new RetentionPolicyEvaluationItem(3, PairRetentionClass.StatusCodeMismatch, 100),
                new RetentionPolicyEvaluationItem(4, PairRetentionClass.BothNonSuccess, 100),
            });

        IReadOnlyList<RetentionPolicyDecision> decisions = evaluator.Evaluate(request);

        AssertDecision(decisions[0], false, false, false);
        AssertDecision(decisions[1], false, false, true);
        AssertDecision(decisions[2], true, true, true);
        AssertDecision(decisions[3], true, true, true);
        AssertDecision(decisions[4], true, true, true);
    }

    [TestMethod]
    public void Evaluate_WhenOverrideIsKeepAll_RetainsAllNonSuccessArtifacts()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RetentionPolicyEvaluationRequest request = CreateNonSuccessOverrideRequest(NonSuccessRetentionOverride.KeepAll, now);

        IReadOnlyList<RetentionPolicyDecision> decisions = evaluator.Evaluate(request);

        Assert.IsTrue(decisions.All(decision => decision.RetainRawArtifacts && decision.RetainCanonicalArtifacts && decision.RetainFocusedArtifacts));
    }

    [TestMethod]
    public void Evaluate_WhenOverrideIsTrimAll_TrimsAllNonSuccessArtifacts()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RetentionPolicyEvaluationRequest request = CreateNonSuccessOverrideRequest(NonSuccessRetentionOverride.TrimAll, now);

        IReadOnlyList<RetentionPolicyDecision> decisions = evaluator.Evaluate(request);

        Assert.IsTrue(decisions.All(decision => !decision.RetainRawArtifacts && !decision.RetainCanonicalArtifacts && !decision.RetainFocusedArtifacts));
    }

    [TestMethod]
    public void Evaluate_WhenOverrideIsKeepBounded_HonorsWindowAndCaps()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RetentionPolicyEvaluationRequest request = new RetentionPolicyEvaluationRequest(
            RetentionMode.None,
            NonSuccessRetentionOverride.KeepBounded,
            14,
            100,
            1_000,
            0,
            now,
            now,
            new[]
            {
                new RetentionPolicyEvaluationItem(0, PairRetentionClass.ExecutionFailed, 80),
                new RetentionPolicyEvaluationItem(1, PairRetentionClass.ExecutionFailed, 40),
            });

        IReadOnlyList<RetentionPolicyDecision> decisions = evaluator.Evaluate(request);

        AssertDecision(decisions[0], true, true, true);
        AssertDecision(decisions[1], false, false, false);
    }

    [TestMethod]
    public void Evaluate_WhenWindowWouldKeepButWorkspaceCapExceeded_CapWins()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RetentionPolicyEvaluationRequest request = new RetentionPolicyEvaluationRequest(
            RetentionMode.None,
            NonSuccessRetentionOverride.KeepBounded,
            14,
            1_000,
            100,
            60,
            now,
            now,
            new[]
            {
                new RetentionPolicyEvaluationItem(0, PairRetentionClass.StatusCodeMismatch, 50),
            });

        IReadOnlyList<RetentionPolicyDecision> decisions = evaluator.Evaluate(request);

        AssertDecision(decisions[0], false, false, false);
    }

    private static RetentionPolicyEvaluationRequest CreateNonSuccessOverrideRequest(
        NonSuccessRetentionOverride nonSuccessOverride,
        DateTimeOffset now) =>
        new RetentionPolicyEvaluationRequest(
            RetentionMode.None,
            nonSuccessOverride,
            14,
            100,
            1_000,
            0,
            now,
            now,
            new[]
            {
                new RetentionPolicyEvaluationItem(0, PairRetentionClass.ExecutionFailed, 10),
                new RetentionPolicyEvaluationItem(1, PairRetentionClass.StatusCodeMismatch, 10),
                new RetentionPolicyEvaluationItem(2, PairRetentionClass.BothNonSuccess, 10),
            });

    private static void AssertDecision(
        RetentionPolicyDecision decision,
        bool retainRaw,
        bool retainCanonical,
        bool retainFocused)
    {
        Assert.AreEqual(retainRaw, decision.RetainRawArtifacts);
        Assert.AreEqual(retainCanonical, decision.RetainCanonicalArtifacts);
        Assert.AreEqual(retainFocused, decision.RetainFocusedArtifacts);
    }
}
