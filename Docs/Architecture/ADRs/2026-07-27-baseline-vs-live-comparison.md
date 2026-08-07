# Baseline vs Live Comparison

- Date: 2026-07-27
- Status: Approved

## Context

ParityBench.NET could only compare two endpoints that were both live at the same
moment: `RunOptions` carries `EndpointA` and `EndpointB`, and `ComparisonRunExecutor`
calls both for every request in the batch.

Enterprise upgrade and migration work breaks that assumption. Version A is available
now; version B is deployed weeks or months later; A is frequently decommissioned in
between. The behaviour still has to be verified, and by the time B exists there is
nothing left to compare it against.

The feature therefore has to capture the behaviour of an endpoint **once**, as a
reusable artifact, and later compare a live endpoint against that artifact using the
existing comparison rules, exclusions, masking and report.

## Decision

Add a second run mode built on one idea: **only endpoint execution changes; the
comparison never learns where a side came from.**

`RunOptions` gains an optional `BaselineBinding` (`LiveVsLive`, `CaptureBaseline`,
`BaselineVsLive` — null means the original behaviour). It drives three things in the
executor and nothing else:

| Mode | Slot A | Slot B |
|---|---|---|
| `LiveVsLive` | pipeline `Input→Response`, then `Mapping` | same |
| `CaptureBaseline` | executed and mapped, then written to the package | not executed |
| `BaselineVsLive` | comparison model loaded from the package; **no** phases run | pipeline as usual |

The pair phases (`Comparison`, `ResultProcessing`) then see two `ComparisonInstance`
values of the same CLR type, exactly as they do for a live pair.

Four supporting decisions:

1. **The stored expected side is the mapped comparison model, not the raw response.**
   Capture serializes `context.ComparisonInstance` to JSON; replay deserializes it
   straight into the comparison type. The expected result is therefore frozen at
   capture time and does not drift as the plugin's mapping evolves. The raw response
   is stored alongside it for inspection and provenance, never for comparison.

2. **A replayed slot skips `Input→Mapping` entirely** rather than being served through
   a substitute transport. No request is built, no token is exchanged, no call is
   made — which is the whole point, since the captured version is usually gone.

3. **Plugin comparisons only.** A stored comparison model only means something while
   a plugin comparison defines the type it belongs to, so the workflow service refuses
   capture or replay without one. The legacy raw/model-registry path stays live-vs-live.

4. **Packages are immutable and versioned.** `<workspace>/baselines/<id>/v<n>/` holds
   the manifest, the requests, the raw responses and the comparison models. Capturing
   again under an existing name reserves the next version; an import always lands on a
   fresh version. A version becomes visible only when `baseline.json` is written, which
   happens after the run that produced it has finished.

Provenance (captured-at, capture endpoint, plugin id/version, environment, tool
version, originating run) travels in the manifest and is surfaced in the report, with
a standing caveat that a replay happens at a different time and often in a different
environment, so a difference is not automatically a software regression. The report
escalates to a warning when the plugin version or environment differs between capture
and replay.

## Rationale

- Confining the change to endpoint execution is what keeps the comparison engine,
  ignore/smart-ignore/mask rules, focused raw content, retention and the report
  untouched — the feature reuses them rather than reimplementing them.
- Persisting a replayed slot's model under the same `canonical/<slot>/<path>` artifact
  naming a live mapped slot uses means retention classification and the report cannot
  tell the two apart, so no downstream code needed a baseline branch.
- Masking on replay uses the *run's* mask rules rather than the capture's: a mask added
  since the capture must apply to both sides, or every masked field would read as a
  difference.
- Freezing the mapped model (rather than re-mapping raw responses on replay) is what
  makes an approved expected result stable. The cost — a plugin mapping change shows up
  as a difference — is surfaced as a version warning rather than hidden.
- Capture writes nothing for a scenario the endpoint could not serve: a transport
  failure or non-2xx is reported in the run but kept out of the package, so a failed
  call can never become someone's expected result.

## Trade-offs

- A capture run reports its scenarios as `Equal` with zero differences: there is only
  one side, so nothing is compared. This avoids adding a sixth `RequestPairOutcome`
  value (and the summary/report/UI churn that follows one); the run is identified as a
  capture by the report's mode banner instead.
- Replay depends on the plugin still being able to *deserialize* the stored model. A
  breaking change to the comparison type invalidates old packages — the schema version
  and plugin version in the manifest make that diagnosable, not automatic.
- Packages hold three payloads per scenario (request, raw response, model), so a
  1000-scenario capture is roughly three times the size of the responses alone. Raw
  responses could be made optional later if that becomes a problem.
- A capture run's version number is assigned by the store when the run starts, so it is
  not on the binding; the report resolves the package a capture wrote by matching
  `CapturedFromRunId`.

## Alternatives considered

- **Re-map raw responses with the current plugin on replay.** Mapping changes would
  then affect both sides equally and never show as diffs, but the expected result would
  no longer be frozen — it would change as the plugin evolves, and replay would break
  once the plugin could no longer parse the old payload.
- **Serve the baseline through a substituted `IEndpointRequestSender`.** The cleanest
  seam on paper, and it would work for both comparison paths, but the replayed slot
  would still run the plugin's request steps — including token exchange against
  services that no longer exist.
- **Store both raw and model, and choose per run.** Most flexible, most surface area to
  build, test and explain; deferred rather than rejected.
- **A new `RequestPairOutcome.BaselineCaptured`.** More literal, but it ripples through
  the summary accumulator, retention classification, report analysis and every UI
  switch for a mode that produces no comparison at all.

## Impacted projects or files

New: `Source/ParityBench.NET.Domain/Baselines/`, `Source/ParityBench.NET.Application/Baselines/`,
`Source/ParityBench.NET.Engine/Baselines/`, `Source/ParityBench.NET.Workspaces/Stores/FileSystemBaselineStore.cs`,
`Source/ParityBench.NET.UI/Baselines/BaselineLibraryPanel.razor`,
`Source/ParityBench.NET.Cli/BaselineCommand.cs`, `Docs/Guides/baseline-vs-live.md`.

Changed: `RunOptions` (+`Baseline`), `ComparisonRunExecutor` (mode handling),
`CanonicalMappingMiddleware` (stashes the pre-mapping artifact), `ExecutionRecord`
(+`IsBaselineReplay`), `RequestComparisonWorkflowService` (replay staging and endpoint
synthesis), `StaticReportMetadata` / `StaticReportManifest` (schema 3 + provenance),
`RunWorkflow.razor`, `RunResult.razor`, `ParityBenchHome.razor`, the CLI request
command, and `WorkspaceServiceCollectionExtensions`.

## Verification approach

- `Tests/ParityBench.NET.Engine.Tests/BaselineCaptureAndReplayTests.cs` — capture and
  replay through the real file-system store: capture calls only the recorded endpoint,
  replay calls only the live one, a changed live response is reported different, an
  ignore rule still suppresses it, a request the package never saw fails the pair, a
  non-2xx capture writes nothing, and a run that fails while finalizing leaves no
  package behind.
- `Tests/ParityBench.ClientCustomerLookupPlugin.Tests` — the same round trip with the
  real reference plugin package: after capturing from the SOAP endpoint, the replay
  sends exactly one request, to the live JSON endpoint, with the plugin's token
  exchange still applied to that side only.
- `Tests/ParityBench.NET.Workspaces.Tests/FileSystemBaselineStoreTests.cs` — versioning
  never overwrites, in-progress captures stay invisible, zip export/import round trip,
  zip-slip and non-package archives refused, one corrupt manifest does not hide the
  library.
- `Tests/ParityBench.NET.Application.Tests/BaselineRunWorkflowTests.cs` — replay stages
  the package's requests, names the expected side after the package, resolves the
  latest version, and refuses a mismatched comparison or a missing plugin selection.
- `Tests/ParityBench.NET.Cli.Tests/BaselineCommandTests.cs` — the `--capture-baseline`
  and `--baseline` flags and the `baseline` command's parsing rules.

## Supersedes or superseded by

- Builds on `2026-07-22-plugin-extensibility-and-worker-isolation.md`; supersedes nothing.
- Not superseded by another ADR.
