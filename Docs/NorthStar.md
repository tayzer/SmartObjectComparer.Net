# V2 Request Comparison North Star

## Purpose

This document defines the end-state architecture for V2 request comparison under `Source/`. It is the target design for large-scale request comparison across CLI, Web, Desktop, and test hosts.

The current V2 implementation already has the right outer boundaries: staged request batches, file-backed response artifacts, paged result details, and thin host composition roots. The remaining work is to make those boundaries explicit as a staged, bounded, asynchronous pipeline with a single retention owner and deterministic persistence rules.

## Problem To Solve

V2 must handle these conditions without changing result semantics across hosts:

- Large runs with thousands of request pairs.
- Large response bodies that cannot be treated as cheap in-memory payloads.
- Host-agnostic execution so CLI, Web, Desktop, and tests drive the same application workflow.
- High throughput with bounded memory use and bounded temporary disk growth.
- Deterministic results and deterministic result ordering even when execution is concurrent.
- Safe retention so useful evidence survives, but raw artifacts do not accumulate without policy.

The north star is not “stream everything forever” and it is not “keep every body in memory until comparison finishes.” It is a bounded pipeline that persists durable truth early, treats large bodies as artifacts, and applies cleanup only after result metadata is safely appended.

## Current V2 Controlling Surfaces

The current V2 control path lives in these surfaces under `Source/`:

- [`Source/ParityBench.NET.Application/Workflow/RequestComparisonWorkflowService.cs`](../Source/ParityBench.NET.Application/Workflow/RequestComparisonWorkflowService.cs) stages request inputs, creates runs, starts runs, cancels runs, and triggers report generation.
- [`Source/ParityBench.NET.Application/Runs/ComparisonRunService.cs`](../Source/ParityBench.NET.Application/Runs/ComparisonRunService.cs) owns run lifecycle, progress updates, persistence of run snapshots, cancellation, and lifecycle event publication.
- [`Source/ParityBench.NET.Engine/BasicComparisonRunExecutor.cs`](../Source/ParityBench.NET.Engine/BasicComparisonRunExecutor.cs) currently collapses planning, execution, comparison, artifact persistence, detail writing, and partial progress reporting into one executor.
- [`Source/ParityBench.NET.Engine/CompareNetObjectsResponseComparer.cs`](../Source/ParityBench.NET.Engine/CompareNetObjectsResponseComparer.cs) performs model-aware comparison from persisted artifacts rather than from in-memory byte arrays.
- [`Source/ParityBench.NET.Engine/FocusedRawContentBuilder.cs`](../Source/ParityBench.NET.Engine/FocusedRawContentBuilder.cs) derives focused, pruned response artifacts for UX from persisted bodies.
- [`Source/ParityBench.NET.Workspaces/FileSystemRequestBatchStore.cs`](../Source/ParityBench.NET.Workspaces/FileSystemRequestBatchStore.cs) stages request batches and persists request manifests.
- [`Source/ParityBench.NET.Workspaces/FileSystemRunArtifactStore.cs`](../Source/ParityBench.NET.Workspaces/FileSystemRunArtifactStore.cs) persists response artifacts and reopens them for later stages.
- [`Source/ParityBench.NET.Workspaces/FileSystemRunDetailStore.cs`](../Source/ParityBench.NET.Workspaces/FileSystemRunDetailStore.cs) writes paged pair-result metadata and exposes paged reads for browsing and export.
- [`Source/ParityBench.NET.Application/Results/ComparisonRunResultService.cs`](../Source/ParityBench.NET.Application/Results/ComparisonRunResultService.cs) loads summaries, pages through details, exports results, and reads artifact previews lazily.

## Current V2 Shape

Today, V2 already stages request files to a workspace manifest, creates a run snapshot, executes endpoint A and B concurrently per request, persists responses to the artifact store, compares those persisted artifacts, appends detail pages, and returns a `RunResultSummary`.

That is a good base, but most of the operational behavior still sits inside [`Source/ParityBench.NET.Engine/BasicComparisonRunExecutor.cs`](../Source/ParityBench.NET.Engine/BasicComparisonRunExecutor.cs). Chunking limits some pressure, but the architecture still treats execution, comparison, detail persistence, focused artifact generation, and eventual retention as one local code path rather than as explicit pipeline stages with clear ownership.

## North Star Architecture

The end-state V2 engine is a staged, bounded, asynchronous pipeline. Hosts remain thin composition roots. Application services own workflow and lifecycle. Engine stages own request execution and comparison. Workspace services own persistence. Cleanup owns retention.

### Stage 1: Manifest And Request Planning

Planning produces the immutable run manifest for the full comparison.

- Stage or reference the request batch.
- Resolve run options, comparison options, contract profile selection, and retention policy.
- Assign a stable manifest ordinal to every request pair.
- Record the intended artifact families for the run.
- Publish the initial planned request count and stage metadata.

The manifest ordinal is the ordering source of truth for all later result pages, exports, and reports. Path sorting is an implementation detail; ordering must not depend on which request happens to finish first.

### Stage 2: Execution

Execution is a bounded producer stage.

- Read planned requests in manifest order.
- Prepare endpoint-specific request bodies.
- Execute endpoint A and endpoint B with bounded concurrency.
- Persist raw response artifacts immediately.
- Emit lightweight execution records that reference artifacts, status codes, content types, hashes, sizes, and any execution error.

Execution does not decide retention and does not build final UX payloads. Its job is to turn live endpoint traffic into durable artifact references and execution metadata.

### Stage 3: Compare

Comparison is a bounded consumer of execution records.

- Classify failed, mismatch, and non-success cases from execution metadata.
- For standard comparison, compare raw persisted artifacts.
- For contract-profile comparison, normalize persisted raw artifacts into canonical artifacts first, then compare those canonical artifacts.
- Produce pair-result metadata, differences, outcome messages, and references to any focused UX artifacts.

Comparison may reopen artifacts many times. That is expected. It is the main reason artifact-backed handoff is a first-class design choice rather than a workaround.

### Stage 4: Detail And Result Persistence

Persistence is the durable truth boundary.

- Append request-pair results in manifest ordinal order.
- Page large result sets as they are written.
- Persist summary counts, execution metrics, and any derived analysis indexes.
- Persist enough metadata to browse or export results without reopening every raw artifact.

The durable truth for a completed run is the run snapshot plus the detail/result metadata. Raw and canonical artifacts are supporting evidence, not the primary history surface.

### Stage 5: Cleanup And Retention

Cleanup is a separate stage and the sole retention owner.

- Read persisted pair-result metadata and artifact references.
- Apply outcome-based retention rules.
- Delete or retain raw, canonical, and focused artifacts according to policy.
- Record retention state so later browsing can distinguish “not retained by policy” from “missing unexpectedly.”

Cleanup runs only after the corresponding pair result has been appended durably.

### Stage 6: Progress And Event Publication

Progress publication is a projection of run state, not an alternate control path.

- Publish lifecycle transitions from the application run service.
- Publish stage-aware progress from planning, execution, comparison, persistence, and cleanup.
- Report counts by manifest ordinal completion, not by host-specific UI assumptions.
- Keep host adapters free to map the same events to SignalR, in-process UI updates, or CLI console output.

## Why Artifact-Backed Handoff

Artifact-backed handoff is the correct default for V2 because the system must optimize for large bodies, determinism, resumability, and host independence.

Pure in-memory stream handoff looks attractive for a single process and small bodies, but it breaks down quickly for the workload V2 is intended to support:

- A compare stage often needs to reopen content after execution has finished.
- Contract-profile normalization naturally introduces a second persisted representation.
- Focused UX artifacts are derived after comparison and may need to read the same content again.
- Large concurrent runs should not pin many response bodies in memory while downstream work catches up.
- File-backed artifacts make failure analysis, report generation, and post-run browsing possible without re-executing the run.
- Hosts stay thin because they do not need process-local lifetime tricks to keep streams alive across stages.

The tradeoff is additional disk I/O and temporary artifact growth. That tradeoff is acceptable because the system can bound disk pressure with explicit cleanup policies, while unbounded memory pressure is much harder to control safely.

## Artifact Model

V2 uses distinct artifact families with different purposes.

### Raw Artifacts

Raw artifacts are the first persisted response bodies produced by execution.

- One artifact per endpoint response.
- Stored immediately after endpoint execution.
- Carry status code, content type, content length, and content hash metadata.
- Serve as the source material for raw comparison, normalization, preview, and debugging.

When masking rules are enabled, the persisted raw artifact is the retained safe form, not a byte-for-byte wire capture. Safe retention wins over perfect transport fidelity.

### Canonical Artifacts

Canonical artifacts are the normalized comparison bodies used for contract-profile comparison.

- Produced only when a contract profile requires response normalization.
- Derived from raw artifacts, not from transient in-memory bodies.
- Represent the comparison truth for contract-profile runs.
- May differ in format from the original response format.

The current executor already persists these under canonical artifact paths during contract-profile comparison. The north star keeps that behavior and makes it an explicit stage output.

### Focused Artifacts

Focused artifacts are pruned bodies optimized for browsing and report UX.

- Derived from raw or canonical artifacts after comparison rules are known.
- Remove ignored paths and low-value noise.
- Preserve enough nearby structure for a human to understand the difference.
- Exist to support UX, not to redefine comparison truth.

Focused artifacts become the preferred preview surface when full raw artifacts are not retained.

### Result And Detail Metadata

Result metadata is the durable history surface.

- Pair outcome, equality status, difference count, messages, and structured differences.
- Artifact references for raw, canonical, or focused bodies when retained.
- Page-oriented detail indexes for large runs.
- Run summary counts and execution metrics.
- Optional derived analysis artifacts such as report indexes.

Reports, exports, and result browsing should operate primarily from this metadata surface and only open artifacts lazily when a specific preview is requested.

## Retention Philosophy

Retention is outcome-based and intentionally asymmetric.

- Keep durable metadata for every request pair.
- Apply a run-wide retention mode to successful outcomes by default.
- Retain only the minimum artifact evidence needed for later understanding of the outcome.
- Prefer focused artifacts over full bodies for successful comparisons that differ.
- Keep raw evidence longer for failures and status problems by default, because those cases are harder to reconstruct from summaries alone.
- Apply retention after persistence, never during classification.

### Retention Modes

The run chooses one retention mode for successful outcomes. The default mode is `TrimmedEqualsAndIgnoredPaths`.

| Mode | Meaning |
| --- | --- |
| `TrimmedEqualsAndIgnoredPaths` (default) | `Equal`: trim raw, canonical, and focused artifacts after durable append. `Different`: retain focused artifacts only (with ignored paths removed), then trim raw and canonical artifacts. |
| `TrimmedEquals` | `Equal`: trim raw, canonical, and focused artifacts after durable append. `Different`: retain raw, canonical, and focused artifacts. |
| `TrimmedIgnoredPaths` | `Equal`: retain raw, canonical, and focused artifacts. `Different`: retain focused artifacts only (with ignored paths removed), then trim raw and canonical artifacts. |
| `None` | No policy-driven trimming for successful outcomes; retain raw, canonical, and focused artifacts. |

### Outcome Mapping

Default behavior maps outcome class to retention class as follows:

- `Equal` and `Different` use the run's default retention mode behavior.
- `ExecutionFailed`, `StatusCodeMismatch`, and `BothNonSuccess` keep diagnostic evidence for a bounded retention window by default.
- Non-success default retention is changed only by an explicit advanced non-success override switch.

### Default + Override Semantics

- A global default retention mode applies run-wide.
- A per-run retention mode override is supported and replaces the global default for that run.
- A separate, explicit advanced override controls non-success diagnostic retention behavior.
- Non-success override semantics are independent of the successful-outcome retention mode.

### Policy Defaults (V1)

To remove ambiguity, V1 defines concrete defaults for non-success diagnostic retention:

- `nonSuccessDiagnosticRetentionWindowDays`: `14`
- `nonSuccessDiagnosticRetentionMaxBytesPerRun`: `5368709120` (5 GiB)
- `nonSuccessDiagnosticRetentionMaxBytesWorkspace`: `53687091200` (50 GiB)

Configuration keys and semantics:

- `ParityBench:Retention:Mode` sets the run-wide successful-outcome retention mode.
- `ParityBench:Retention:NonSuccessOverride` controls non-success diagnostic retention behavior independently of successful-outcome mode.
- `ParityBench:Retention:NonSuccessDiagnosticRetentionWindowDays` sets the default bounded diagnostic time window for non-success evidence.
- `ParityBench:Retention:NonSuccessDiagnosticRetentionMaxBytesPerRun` sets the hard per-run diagnostic storage cap for non-success evidence.
- `ParityBench:Retention:NonSuccessDiagnosticRetentionMaxBytesWorkspace` sets the hard workspace-wide diagnostic storage cap for non-success evidence.

Allowed values for `NonSuccessOverride`:

- `KeepBounded` (default)
- `KeepAll`
- `TrimAll`

Precedence rules:

1. Per-run override for successful outcomes.
2. Non-success override evaluated independently.
3. Hard storage caps win over time windows when both apply.

This V1 default is versioned policy. Any revision to these defaults requires incrementing `runRetentionPolicyVersion` so historical run interpretation remains deterministic.

### Minimum Retention Metadata (Required)

The following retention metadata is mandatory and persisted with run and pair records.

Run-level required fields:

- `runRetentionMode`
- `runRetentionPolicyVersion`
- `comparisonRulesSnapshotHash` (or an equivalent immutable rules hash)

Pair-level required fields:

- `pairRetentionClass` (`Equal`, `Different`, `ExecutionFailed`, `StatusCodeMismatch`, `BothNonSuccess`)
- `artifactRetentionState` per artifact family (`retained`, `trimmedByPolicy`, `missingUnexpectedly`)
- `retentionAppliedAt` timestamp

### Default Policy Matrix

The default matrix below assumes `TrimmedEqualsAndIgnoredPaths` and no advanced non-success override.

| Outcome class | Detail metadata | Raw artifacts | Canonical artifacts | Focused artifacts | Default intent |
| --- | --- | --- | --- | --- | --- |
| `Equal` | Keep | Trim after append | Trim after append | Trim after append | Preserve durable truth and counts only. |
| `Different` | Keep | Trim after focused/detail append | Trim after focused/detail append | Keep (ignored paths removed) | Preserve human-usable evidence with bounded disk cost. |
| `ExecutionFailed` | Keep | Keep for bounded diagnostic window | Keep for bounded diagnostic window if produced | Optional | Preserve debugging evidence for transport, preparation, or processing failures. |
| `StatusCodeMismatch` | Keep | Keep for bounded diagnostic window | Keep for bounded diagnostic window if produced | Optional | Preserve response evidence because status divergence is operationally significant. |
| `BothNonSuccess` | Keep | Keep for bounded diagnostic window | Keep for bounded diagnostic window if produced | Optional | Treat as status/problem evidence rather than as ordinary equality/diff UX. |

Policy can be tuned by host or configuration, but tuning must not violate the invariants below.

## Invariants

These rules define the architecture, not just the first implementation.

- Append results before deletion. No artifact referenced by an in-flight pair may be deleted before its result metadata is durably appended.
- Cleanup is the sole owner of retention. Execution and comparison may create artifacts, but they do not decide what survives.
- Hosts remain thin composition roots. Web, Desktop, CLI, and tests compose services; they do not own run semantics.
- Rule changes must not mutate historical truth silently. Changing ignore rules, canonicalization rules, or focused-pruning rules requires a new run or an explicit re-derivation flow that records versioned outputs.
- Deterministic ordering is by manifest ordinal. Concurrent completion order must never become the user-visible ordering source.
- Result metadata is durable truth. Reports and exports must still function when some raw artifacts were intentionally deleted by policy.

## Implications For Report, Export, And Result Browsing

This design changes the expectations for the result surfaces.

- Run history and summaries load from run snapshots and paged detail metadata, not from scanning raw artifacts.
- JSON and CSV export operate from detail pages and should not require raw body retention.
- Report generation reads summary, detail pages, and derived indexes first, then opens retained artifacts lazily for previews.
- UI should prefer focused artifacts for human inspection and clearly label when full raw artifacts were deleted by policy.
- Browsing must distinguish three cases: artifact retained, artifact intentionally deleted by retention, and artifact unexpectedly unavailable.

This keeps reports usable for large runs even when raw retention is intentionally strict.

## Migration From The Current Executor

Migration should be incremental and keep the existing V2 application and host surfaces stable.

### Phase 1: Make Stage Boundaries Explicit

- Keep [`Source/ParityBench.NET.Engine/BasicComparisonRunExecutor.cs`](../Source/ParityBench.NET.Engine/BasicComparisonRunExecutor.cs) as the façade behind `IComparisonRunExecutor`.
- Extract explicit planning, execution, comparison, persistence, and cleanup collaborators.
- Add manifest ordinal to the request manifest and carry it through detail metadata.

### Phase 2: Separate Execution Records From Pair Results

- Introduce an execution-record contract that contains artifact references and endpoint outcome metadata only.
- Have comparison consume execution records rather than live endpoint responses.
- Keep current result semantics unchanged.

### Phase 3: Introduce Bounded Stage Queues

- Move from chunk-local orchestration to bounded asynchronous stage handoff.
- Bound execution-to-compare and compare-to-persist queues independently.
- Measure queue depth, artifact counts, and cleanup lag as first-class observability.

### Phase 4: Move Retention Into Cleanup

- Stop deleting or implicitly retaining artifacts inside comparison helpers.
- Add explicit retention state to detail metadata.
- Apply the policy matrix after append and summary persistence.

### Phase 5: Expand Result Surfaces Around Retention-Aware Metadata

- Make report/export/result browsing resilient when raw artifacts are absent by design.
- Prefer focused previews when available.
- Add explicit messaging for retention outcomes in host UIs and CLI output.

The migration path is evolutionary. Hosts should continue to call the same workflow services while the engine internals are split into clearer stage owners.

## Non-Goals

- Do not move business logic into Web, Desktop, or CLI hosts.
- Do not require a distributed queue or multi-process architecture for V2.
- Do not retain every raw body indefinitely just to preserve optional debugging paths.
- Do not reclassify historical runs automatically when comparison rules evolve.
- Do not make report/export depend on full raw artifact retention.

## Risks And Tradeoffs

- Artifact-backed staging increases disk I/O and creates temporary storage spikes if cleanup falls behind.
- Focused artifacts improve UX but add another derived representation that must be versioned conceptually with comparison rules.
- Deterministic manifest-order append may reduce some apparent throughput compared with purely opportunistic completion-order writes.
- Strong retention boundaries make the system safer, but they also force reports and browsers to handle intentionally missing raw artifacts well.

These are acceptable tradeoffs because V2 is optimizing for correctness, scale, host-agnostic behavior, and operational safety rather than for the smallest possible happy-path implementation.