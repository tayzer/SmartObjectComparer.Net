using ParityBench.NET.Domain;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Baselines;

/// <summary>
/// Builds the provenance a run's report shows about the baseline it captured or
/// replayed. Shared by the live results view and the static report bundle so both
/// tell the same story about where the expected side came from.
/// </summary>
public static class BaselineProvenanceFactory
{
    public static async Task<BaselineReportProvenance?> CreateAsync(
        IBaselineStore? store,
        ComparisonRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Options.Baseline is not { } binding || binding.Mode == BaselineRunMode.LiveVsLive)
        {
            return null;
        }

        BaselinePackageManifest? manifest = store is null
            ? null
            : await ResolveManifestAsync(store, run, binding, cancellationToken).ConfigureAwait(false);

        return new BaselineReportProvenance(
            binding.Mode,
            manifest?.Id.Value ?? binding.BaselineId?.Value,
            manifest?.Name ?? binding.CaptureName,
            manifest?.Version ?? binding.Version,
            manifest?.CapturedAt,
            manifest?.CaptureEndpoint.ToString(),
            manifest?.CaptureEndpointLabel,
            manifest?.PluginId ?? run.Options.PluginComparison?.PluginId,
            manifest?.PluginVersion,
            manifest?.ComparisonId ?? run.Options.PluginComparison?.ComparisonId,
            manifest?.EnvironmentName,
            manifest?.CapturedFromRunId,
            manifest?.ToolVersion,
            run.Options.PluginComparison?.PluginVersion,
            run.Options.PluginComparison?.EnvironmentName,
            ToolVersion.Current,
            manifest?.Scenarios.Count ?? 0);
    }

    private static async Task<BaselinePackageManifest?> ResolveManifestAsync(
        IBaselineStore store,
        ComparisonRun run,
        BaselineBinding binding,
        CancellationToken cancellationToken)
    {
        if (binding.BaselineId is { } baselineId)
        {
            return await store
                .LoadManifestAsync(baselineId, binding.Version, cancellationToken)
                .ConfigureAwait(false);
        }

        // A capture run does not know its version up front — the store assigns it when
        // the run starts — so the package it wrote is found by the run that wrote it.
        foreach (BaselineSummary summary in await store.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            BaselinePackageManifest? candidate = await store
                .LoadManifestAsync(summary.Id, summary.Version, cancellationToken)
                .ConfigureAwait(false);

            if (candidate is not null
                && string.Equals(candidate.CapturedFromRunId, run.Id.Value, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }
}
