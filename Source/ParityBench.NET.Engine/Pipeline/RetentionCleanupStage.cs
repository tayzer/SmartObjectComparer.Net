using System.Collections.Concurrent;

using Microsoft.Extensions.Options;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Engine.Pipeline;

public sealed class RetentionCleanupStage : IRunCleanupStage
{
    private readonly IRunArtifactStore runArtifactStore;
    private readonly IRunDetailStore runDetailStore;
    private readonly RetentionPolicyEvaluator retentionPolicyEvaluator;
    private readonly RetentionConfiguration retentionConfiguration;

    public RetentionCleanupStage(
        IRunArtifactStore runArtifactStore,
        IRunDetailStore runDetailStore,
        RetentionPolicyEvaluator retentionPolicyEvaluator,
        IOptions<RetentionConfiguration> retentionConfiguration)
    {
        this.runArtifactStore = runArtifactStore;
        this.runDetailStore = runDetailStore;
        this.retentionPolicyEvaluator = retentionPolicyEvaluator;
        this.retentionConfiguration = retentionConfiguration.Value;
    }

    public async Task<CleanupStageResult> CleanupAsync(
        ComparisonRun run,
        CleanupStageContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);
        if (!context.DurableAppendCompleted)
        {
            throw new InvalidOperationException("Cleanup requires durable append completion.");
        }

        DateTimeOffset appliedAt = DateTimeOffset.UtcNow;
        List<RetentionCandidate> orderedCandidates = context.PersistedRecords
            .OrderBy(record => record.ManifestOrdinal)
            .Select(record => new RetentionCandidate(record.ManifestOrdinal, record.Result))
            .ToList();

        IReadOnlyDictionary<string, long> currentRunReferencedArtifactSizes = await BuildCurrentRunArtifactSizeMapAsync(orderedCandidates, run.Id, cancellationToken).ConfigureAwait(false);
        long totalWorkspaceArtifactBytes = await runArtifactStore.GetTotalArtifactBytesAsync(cancellationToken).ConfigureAwait(false);
        long currentRunReferencedBytes = currentRunReferencedArtifactSizes.Values.Sum();
        long workspaceBytesOutsideCurrentRun = Math.Max(0, totalWorkspaceArtifactBytes - currentRunReferencedBytes);

        IReadOnlyList<RetentionPolicyEvaluationItem> policyItems = orderedCandidates
            .Select(candidate => new RetentionPolicyEvaluationItem(
                candidate.ManifestOrdinal,
                candidate.Result.PairRetentionClass,
                candidate.Result.PairRetentionClass is PairRetentionClass.ExecutionFailed or PairRetentionClass.StatusCodeMismatch or PairRetentionClass.BothNonSuccess
                    ? CalculateDiagnosticBytes(candidate.Result, run.Id, currentRunReferencedArtifactSizes)
                    : 0))
            .ToList();

        RetentionPolicyEvaluationRequest evaluationRequest = new RetentionPolicyEvaluationRequest(
            run.RunRetentionMode,
            retentionConfiguration.NonSuccessOverride,
            retentionConfiguration.NonSuccessDiagnosticRetentionWindowDays,
            retentionConfiguration.NonSuccessDiagnosticRetentionMaxBytesPerRun,
            retentionConfiguration.NonSuccessDiagnosticRetentionMaxBytesWorkspace,
            workspaceBytesOutsideCurrentRun,
            run.CreatedAt,
            appliedAt,
            policyItems);

        IReadOnlyDictionary<int, RetentionPolicyDecision> decisions = retentionPolicyEvaluator
            .Evaluate(evaluationRequest)
            .ToDictionary(decision => decision.ManifestOrdinal);

        RequestPairResult[] retainedResults = new RequestPairResult[orderedCandidates.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, orderedCandidates.Count),
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = GetArtifactOperationConcurrency() },
            async (index, token) =>
            {
                RetentionCandidate candidate = orderedCandidates[index];
                RetentionPolicyDecision decision = decisions[candidate.ManifestOrdinal];
                retainedResults[index] = await ApplyRetentionDecisionAsync(run.Id, candidate.Result, decision, token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        await runDetailStore.SaveDetailsAsync(run.Id, retainedResults, cancellationToken).ConfigureAwait(false);
        return Summarize(retainedResults);
    }

    private async Task<RequestPairResult> ApplyRetentionDecisionAsync(
        RunId runId,
        RequestPairResult result,
        RetentionPolicyDecision decision,
        CancellationToken cancellationToken)
    {
        ArtifactRetentionState rawA = await ResolveArtifactStateAsync(
            GetRawArtifactReference(runId, EndpointSlot.A, result),
            decision.RetainRawArtifacts,
            cancellationToken).ConfigureAwait(false);
        ArtifactRetentionState rawB = await ResolveArtifactStateAsync(
            GetRawArtifactReference(runId, EndpointSlot.B, result),
            decision.RetainRawArtifacts,
            cancellationToken).ConfigureAwait(false);
        ArtifactRetentionState canonicalA = await ResolveArtifactStateAsync(
            GetCanonicalArtifactReference(runId, EndpointSlot.A, result),
            decision.RetainCanonicalArtifacts,
            cancellationToken).ConfigureAwait(false);
        ArtifactRetentionState canonicalB = await ResolveArtifactStateAsync(
            GetCanonicalArtifactReference(runId, EndpointSlot.B, result),
            decision.RetainCanonicalArtifacts,
            cancellationToken).ConfigureAwait(false);
        ArtifactRetentionState focusedA = await ResolveArtifactStateAsync(
            result.FocusedResponseA?.Artifact,
            decision.RetainFocusedArtifacts,
            cancellationToken).ConfigureAwait(false);
        ArtifactRetentionState focusedB = await ResolveArtifactStateAsync(
            result.FocusedResponseB?.Artifact,
            decision.RetainFocusedArtifacts,
            cancellationToken).ConfigureAwait(false);

        PairArtifactRetentionState retentionState = new PairArtifactRetentionState(
            rawA,
            rawB,
            canonicalA,
            canonicalB,
            focusedA,
            focusedB);

        return result.WithRetention(retentionState, decision.RetentionAppliedAt);
    }

    private async Task<ArtifactRetentionState> ResolveArtifactStateAsync(
        ArtifactReference? artifact,
        bool retain,
        CancellationToken cancellationToken)
    {
        if (artifact is null)
        {
            return ArtifactRetentionState.Retained;
        }

        if (retain)
        {
            bool exists = await runArtifactStore.ExistsAsync(artifact, cancellationToken).ConfigureAwait(false);
            return exists ? ArtifactRetentionState.Retained : ArtifactRetentionState.MissingUnexpectedly;
        }

        _ = await runArtifactStore.DeleteIfExistsAsync(artifact, cancellationToken).ConfigureAwait(false);
        return ArtifactRetentionState.TrimmedByPolicy;
    }

    private async Task<IReadOnlyDictionary<string, long>> BuildCurrentRunArtifactSizeMapAsync(
        IReadOnlyList<RetentionCandidate> candidates,
        RunId runId,
        CancellationToken cancellationToken)
    {
        HashSet<string> uniqueArtifactIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RetentionCandidate candidate in candidates)
        {
            AddArtifactIdIfPresent(uniqueArtifactIds, GetRawArtifactReference(runId, EndpointSlot.A, candidate.Result));
            AddArtifactIdIfPresent(uniqueArtifactIds, GetRawArtifactReference(runId, EndpointSlot.B, candidate.Result));
            AddArtifactIdIfPresent(uniqueArtifactIds, GetCanonicalArtifactReference(runId, EndpointSlot.A, candidate.Result));
            AddArtifactIdIfPresent(uniqueArtifactIds, GetCanonicalArtifactReference(runId, EndpointSlot.B, candidate.Result));
            AddArtifactIdIfPresent(uniqueArtifactIds, candidate.Result.FocusedResponseA?.Artifact);
            AddArtifactIdIfPresent(uniqueArtifactIds, candidate.Result.FocusedResponseB?.Artifact);
        }

        ConcurrentDictionary<string, long> sizes = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(
            uniqueArtifactIds,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = GetArtifactOperationConcurrency() },
            async (artifactId, token) =>
            {
                ArtifactReference reference = new ArtifactReference(artifactId, null);
                sizes[artifactId] = await runArtifactStore.GetArtifactSizeAsync(reference, token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return sizes;
    }

    private static long CalculateDiagnosticBytes(
        RequestPairResult result,
        RunId runId,
        IReadOnlyDictionary<string, long> sizeMap)
    {
        long total = 0;
        total += GetSize(GetRawArtifactReference(runId, EndpointSlot.A, result), sizeMap);
        total += GetSize(GetRawArtifactReference(runId, EndpointSlot.B, result), sizeMap);
        total += GetSize(GetCanonicalArtifactReference(runId, EndpointSlot.A, result), sizeMap);
        total += GetSize(GetCanonicalArtifactReference(runId, EndpointSlot.B, result), sizeMap);
        total += GetSize(result.FocusedResponseA?.Artifact, sizeMap);
        total += GetSize(result.FocusedResponseB?.Artifact, sizeMap);
        return total;
    }

    private static long GetSize(
        ArtifactReference? artifact,
        IReadOnlyDictionary<string, long> sizeMap)
    {
        if (artifact is null)
        {
            return 0;
        }

        return sizeMap.TryGetValue(artifact.ArtifactId, out long size) ? size : 0;
    }

    private static ArtifactReference? GetRawArtifactReference(
        RunId runId,
        EndpointSlot endpoint,
        RequestPairResult result)
    {
        ResponseArtifactMetadata? response = endpoint == EndpointSlot.A ? result.ResponseA : result.ResponseB;
        if (response is null)
        {
            return null;
        }

        string artifactId = $"runs/{runId.Value}/artifacts/{endpoint}/{result.RelativePath}";
        return new ArtifactReference(artifactId, response.Artifact.ContentType);
    }

    private static ArtifactReference? GetCanonicalArtifactReference(
        RunId runId,
        EndpointSlot endpoint,
        RequestPairResult result)
    {
        ResponseArtifactMetadata? response = endpoint == EndpointSlot.A ? result.ResponseA : result.ResponseB;
        if (response is null)
        {
            return null;
        }

        if (!response.Artifact.ArtifactId.Contains("/canonical/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string artifactId = $"runs/{runId.Value}/artifacts/{endpoint}/canonical/{endpoint}/{result.RelativePath}";
        return new ArtifactReference(artifactId, response.Artifact.ContentType);
    }

    private static void AddArtifactIdIfPresent(
        ISet<string> ids,
        ArtifactReference? artifact)
    {
        if (artifact is null)
        {
            return;
        }

        ids.Add(artifact.ArtifactId);
    }

    private sealed record RetentionCandidate(
        int ManifestOrdinal,
        RequestPairResult Result);

    private static int GetArtifactOperationConcurrency() => Math.Clamp(Environment.ProcessorCount, 2, 8);

    private static CleanupStageResult Summarize(IEnumerable<RequestPairResult> results)
    {
        int retained = 0;
        int trimmed = 0;
        int missing = 0;
        foreach (RequestPairResult result in results)
        {
            Count(result.ArtifactRetentionState.RawResponseA, ref retained, ref trimmed, ref missing);
            Count(result.ArtifactRetentionState.RawResponseB, ref retained, ref trimmed, ref missing);
            Count(result.ArtifactRetentionState.CanonicalResponseA, ref retained, ref trimmed, ref missing);
            Count(result.ArtifactRetentionState.CanonicalResponseB, ref retained, ref trimmed, ref missing);
            Count(result.ArtifactRetentionState.FocusedResponseA, ref retained, ref trimmed, ref missing);
            Count(result.ArtifactRetentionState.FocusedResponseB, ref retained, ref trimmed, ref missing);
        }

        return new CleanupStageResult(retained, trimmed, missing);
    }

    private static void Count(ArtifactRetentionState state, ref int retained, ref int trimmed, ref int missing)
    {
        switch (state)
        {
            case ArtifactRetentionState.Retained: retained++; break;
            case ArtifactRetentionState.TrimmedByPolicy: trimmed++; break;
            case ArtifactRetentionState.MissingUnexpectedly: missing++; break;
        }
    }
}
