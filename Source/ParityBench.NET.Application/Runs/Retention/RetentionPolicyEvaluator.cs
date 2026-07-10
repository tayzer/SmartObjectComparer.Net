using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Application.Runs.Retention;

public sealed class RetentionPolicyEvaluator
{
    public IReadOnlyList<RetentionPolicyDecision> Evaluate(
        RetentionPolicyEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Dictionary<int, RetentionPolicyDecision> decisions = request.Items
            .ToDictionary(
                item => item.ManifestOrdinal,
                item => EvaluateSuccessfulOutcome(request.SuccessfulOutcomeMode, item, request.AppliedAt));

        IReadOnlyList<RetentionPolicyEvaluationItem> nonSuccessItems = request.Items
            .Where(item => item.PairRetentionClass is PairRetentionClass.ExecutionFailed or PairRetentionClass.StatusCodeMismatch or PairRetentionClass.BothNonSuccess)
            .OrderBy(item => item.ManifestOrdinal)
            .ToList();

        if (nonSuccessItems.Count > 0)
        {
            IReadOnlyDictionary<int, RetentionPolicyDecision> nonSuccessDecisions = EvaluateNonSuccessOutcomes(request, nonSuccessItems);
            foreach (KeyValuePair<int, RetentionPolicyDecision> pair in nonSuccessDecisions)
            {
                decisions[pair.Key] = pair.Value;
            }
        }

        return request.Items
            .OrderBy(item => item.ManifestOrdinal)
            .Select(item => decisions[item.ManifestOrdinal])
            .ToList();
    }

    private static RetentionPolicyDecision EvaluateSuccessfulOutcome(
        RetentionMode successfulOutcomeMode,
        RetentionPolicyEvaluationItem item,
        DateTimeOffset appliedAt)
    {
        if (item.PairRetentionClass is PairRetentionClass.ExecutionFailed or PairRetentionClass.StatusCodeMismatch or PairRetentionClass.BothNonSuccess)
        {
            return RetentionPolicyDecision.KeepAll(item.ManifestOrdinal, appliedAt);
        }

        return item.PairRetentionClass switch
        {
            PairRetentionClass.Equal => successfulOutcomeMode switch
            {
                RetentionMode.TrimmedEqualsAndIgnoredPaths => RetentionPolicyDecision.TrimAll(item.ManifestOrdinal, appliedAt),
                RetentionMode.TrimmedEquals => RetentionPolicyDecision.TrimAll(item.ManifestOrdinal, appliedAt),
                RetentionMode.TrimmedIgnoredPaths => RetentionPolicyDecision.KeepAll(item.ManifestOrdinal, appliedAt),
                RetentionMode.None => RetentionPolicyDecision.KeepAll(item.ManifestOrdinal, appliedAt),
                _ => RetentionPolicyDecision.KeepAll(item.ManifestOrdinal, appliedAt),
            },
            PairRetentionClass.Different => successfulOutcomeMode switch
            {
                RetentionMode.TrimmedEqualsAndIgnoredPaths => RetentionPolicyDecision.KeepFocusedOnly(item.ManifestOrdinal, appliedAt),
                RetentionMode.TrimmedEquals => RetentionPolicyDecision.KeepAll(item.ManifestOrdinal, appliedAt),
                RetentionMode.TrimmedIgnoredPaths => RetentionPolicyDecision.KeepFocusedOnly(item.ManifestOrdinal, appliedAt),
                RetentionMode.None => RetentionPolicyDecision.KeepAll(item.ManifestOrdinal, appliedAt),
                _ => RetentionPolicyDecision.KeepAll(item.ManifestOrdinal, appliedAt),
            },
            _ => RetentionPolicyDecision.KeepAll(item.ManifestOrdinal, appliedAt),
        };
    }

    private static IReadOnlyDictionary<int, RetentionPolicyDecision> EvaluateNonSuccessOutcomes(
        RetentionPolicyEvaluationRequest request,
        IReadOnlyList<RetentionPolicyEvaluationItem> nonSuccessItems)
    {
        Dictionary<int, RetentionPolicyDecision> decisions = new Dictionary<int, RetentionPolicyDecision>(nonSuccessItems.Count);
        switch (request.NonSuccessOverride)
        {
            case NonSuccessRetentionOverride.TrimAll:
                foreach (RetentionPolicyEvaluationItem item in nonSuccessItems)
                {
                    decisions[item.ManifestOrdinal] = RetentionPolicyDecision.TrimAll(item.ManifestOrdinal, request.AppliedAt);
                }

                return decisions;
            case NonSuccessRetentionOverride.KeepAll:
                foreach (RetentionPolicyEvaluationItem item in nonSuccessItems)
                {
                    decisions[item.ManifestOrdinal] = RetentionPolicyDecision.KeepAll(item.ManifestOrdinal, request.AppliedAt);
                }

                return decisions;
            default:
                return EvaluateKeepBounded(request, nonSuccessItems);
        }
    }

    private static IReadOnlyDictionary<int, RetentionPolicyDecision> EvaluateKeepBounded(
        RetentionPolicyEvaluationRequest request,
        IReadOnlyList<RetentionPolicyEvaluationItem> nonSuccessItems)
    {
        Dictionary<int, RetentionPolicyDecision> decisions = new Dictionary<int, RetentionPolicyDecision>(nonSuccessItems.Count);

        DateTimeOffset retentionWindowCutoff = request.AppliedAt.AddDays(-request.NonSuccessDiagnosticRetentionWindowDays);
        if (request.RunCreatedAt < retentionWindowCutoff)
        {
            foreach (RetentionPolicyEvaluationItem item in nonSuccessItems)
            {
                decisions[item.ManifestOrdinal] = RetentionPolicyDecision.TrimAll(item.ManifestOrdinal, request.AppliedAt);
            }

            return decisions;
        }

        long workspaceAvailableForRun = Math.Max(0, request.NonSuccessDiagnosticRetentionMaxBytesWorkspace - request.WorkspaceArtifactBytesOutsideCurrentRun);
        long effectiveCap = Math.Max(0, Math.Min(request.NonSuccessDiagnosticRetentionMaxBytesPerRun, workspaceAvailableForRun));

        long retainedBytes = 0;
        foreach (RetentionPolicyEvaluationItem item in nonSuccessItems)
        {
            long itemBytes = Math.Max(0, item.NonSuccessDiagnosticBytes);
            bool canRetain = itemBytes == 0 || retainedBytes + itemBytes <= effectiveCap;
            decisions[item.ManifestOrdinal] = canRetain
                ? RetentionPolicyDecision.KeepAll(item.ManifestOrdinal, request.AppliedAt)
                : RetentionPolicyDecision.TrimAll(item.ManifestOrdinal, request.AppliedAt);

            if (canRetain)
            {
                retainedBytes += itemBytes;
            }
        }

        return decisions;
    }
}

public sealed record RetentionPolicyEvaluationRequest(
    RetentionMode SuccessfulOutcomeMode,
    NonSuccessRetentionOverride NonSuccessOverride,
    int NonSuccessDiagnosticRetentionWindowDays,
    long NonSuccessDiagnosticRetentionMaxBytesPerRun,
    long NonSuccessDiagnosticRetentionMaxBytesWorkspace,
    long WorkspaceArtifactBytesOutsideCurrentRun,
    DateTimeOffset RunCreatedAt,
    DateTimeOffset AppliedAt,
    IReadOnlyList<RetentionPolicyEvaluationItem> Items);

public sealed record RetentionPolicyEvaluationItem(
    int ManifestOrdinal,
    PairRetentionClass PairRetentionClass,
    long NonSuccessDiagnosticBytes);

public sealed record RetentionPolicyDecision(
    int ManifestOrdinal,
    bool RetainRawArtifacts,
    bool RetainCanonicalArtifacts,
    bool RetainFocusedArtifacts,
    DateTimeOffset RetentionAppliedAt)
{
    public static RetentionPolicyDecision KeepAll(
        int manifestOrdinal,
        DateTimeOffset appliedAt) =>
        new RetentionPolicyDecision(manifestOrdinal, true, true, true, appliedAt);

    public static RetentionPolicyDecision TrimAll(
        int manifestOrdinal,
        DateTimeOffset appliedAt) =>
        new RetentionPolicyDecision(manifestOrdinal, false, false, false, appliedAt);

    public static RetentionPolicyDecision KeepFocusedOnly(
        int manifestOrdinal,
        DateTimeOffset appliedAt) =>
        new RetentionPolicyDecision(manifestOrdinal, false, false, true, appliedAt);
}
