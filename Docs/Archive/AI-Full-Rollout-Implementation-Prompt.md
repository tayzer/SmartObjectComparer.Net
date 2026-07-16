# Full Rollout Implementation Prompt

## Context and Approved Decisions
Implement the full retention and staged pipeline rollout defined in Docs/NorthStar.md.

Approved decisions that are mandatory in implementation:
1. Defaults in NorthStar are approved as-is.
2. Non-success override TrimAll is allowed in every environment, including production.
3. Retention settings support both global defaults and per-run overrides.
4. Rollout type is full rollout. Do not implement a phased no-op or metadata-only rollout.

## Repository and Architecture References
Use these as source-of-truth constraints:
- Docs/NorthStar.md
- Source/ParityBench.NET.Application/Workflow/RequestComparisonWorkflowService.cs
- Source/ParityBench.NET.Application/Runs/ComparisonRunService.cs
- Source/ParityBench.NET.Application/Results/ComparisonRunResultService.cs
- Source/ParityBench.NET.Engine/BasicComparisonRunExecutor.cs
- Source/ParityBench.NET.Engine/CompareNetObjectsResponseComparer.cs
- Source/ParityBench.NET.Engine/FocusedRawContentBuilder.cs
- Source/ParityBench.NET.Workspaces/FileSystemRequestBatchStore.cs
- Source/ParityBench.NET.Workspaces/FileSystemRunArtifactStore.cs
- Source/ParityBench.NET.Workspaces/FileSystemRunDetailStore.cs
- Source/ParityBench.NET.Workspaces/FileSystemRunStore.cs
- Source/ParityBench.NET.Domain/Requests/RequestPairResult.cs
- Source/ParityBench.NET.Domain/Runs/RunOptions.cs
- Source/ParityBench.NET.Domain/Runs/ComparisonRun.cs
- Source/ParityBench.NET.Domain/Runs/RunResultSummary.cs

## Objective and Success Criteria
Deliver the complete NorthStar retention and staged pipeline behavior with deterministic, retention-aware, host-agnostic results.

Success criteria:
- Stages are explicit with clear ownership: planning, execution, compare, persistence, cleanup.
- Cleanup is the only retention owner and runs after durable append.
- Global retention defaults and per-run override are both supported.
- Non-success override supports KeepBounded, KeepAll, TrimAll.
- Result browsing, export, and report paths remain functional when artifacts are intentionally trimmed.
- Historical interpretation is stable through persisted runRetentionPolicyVersion.
- Deterministic ordering remains by manifest ordinal.

## Scope
### In Scope
- Full code-path implementation in Application, Engine, Workspaces, Domain, and result/report read paths.
- Configuration model, binding, option propagation, and precedence enforcement.
- Run-level and pair-level retention metadata persistence.
- Cleanup-stage retention enforcement and explicit deletion semantics.
- Metrics and observability for stage progress and retention effects.
- Automated tests that validate behavior and invariants.

### Out of Scope
- Phased rollout flags that disable behavior.
- Host-specific business logic drift from shared Application/Engine semantics.
- Reclassification or mutation of historical runs without explicit versioned policy semantics.
- Any changes unrelated to retention and staged pipeline rollout.

## Required Implementation Tasks
Execute in this order.

1. Configuration model and binding
- Add strongly typed retention configuration options with defaults from NorthStar.
- Bind configuration from ParityBench:Retention:* in all composition roots that execute runs.
- Add per-run retention override fields to request/run creation contracts.
- Validate input values early and fail fast on invalid mode or override values.

2. Domain/Application metadata additions
- Add run-level fields: runRetentionMode, runRetentionPolicyVersion, comparisonRulesSnapshotHash.
- Add pair-level fields: pairRetentionClass, artifactRetentionState by artifact family, retentionAppliedAt.
- Ensure these fields are included in persisted detail metadata and run snapshots.
- Keep backward-compatible read behavior for older schema payloads.

3. Artifact retention state model
- Define retention class mapping for outcomes: Equal, Different, ExecutionFailed, StatusCodeMismatch, BothNonSuccess.
- Define artifact family states: retained, trimmedByPolicy, missingUnexpectedly.
- Persist retention state separately from artifact reference nullability.
- Treat missingUnexpectedly as an explicit anomaly state, never as policy trim.

4. Executor split and stage ownership updates
- Split BasicComparisonRunExecutor internals into explicit stage collaborators while preserving IComparisonRunExecutor entry point.
- Stage 1 planning assigns and carries manifest ordinal.
- Stage 2 execution persists raw artifacts and emits lightweight execution records.
- Stage 3 compare consumes execution records and emits pair result metadata.
- Stage 4 persistence appends details in manifest ordinal order.
- Stage 5 cleanup applies retention policy after append completion.
- Ensure no retention deletions occur in execution or compare stages.

5. Cleanup retention enforcement and deletion semantics
- Implement retention matrix logic exactly per NorthStar defaults and overrides.
- Enforce non-success bounded retention with window and byte caps when override is KeepBounded.
- Enforce hard caps over time windows when both apply.
- Implement TrimAll non-success behavior as explicit immediate trim after durable append.
- Record retentionAppliedAt and artifactRetentionState for each artifact family decision.

6. Result browsing/export/report compatibility updates
- Keep summary, paging, export, and report operations metadata-first.
- Ensure paths remain functional when raw/canonical artifacts are trimmed intentionally.
- Prefer focused artifacts for preview when available.
- Surface explicit retention reason signaling: retained, trimmedByPolicy, missingUnexpectedly.

7. Observability and metrics
- Add stage-aware progress and lifecycle events for planning, execution, compare, persistence, cleanup.
- Add metrics for cleanup lag, trimmed artifact counts, retained diagnostic bytes, cap-trigger events.
- Emit deterministic counts aligned with manifest ordinal completion.

## Configuration Contract
Configuration keys:
- ParityBench:Retention:Mode
- ParityBench:Retention:NonSuccessOverride
- ParityBench:Retention:NonSuccessDiagnosticRetentionWindowDays
- ParityBench:Retention:NonSuccessDiagnosticRetentionMaxBytesPerRun
- ParityBench:Retention:NonSuccessDiagnosticRetentionMaxBytesWorkspace

Allowed values:
- Retention Mode: TrimmedEqualsAndIgnoredPaths, TrimmedEquals, TrimmedIgnoredPaths, None
- NonSuccessOverride: KeepBounded, KeepAll, TrimAll

Defaults:
- Mode: TrimmedEqualsAndIgnoredPaths
- NonSuccessOverride: KeepBounded
- NonSuccessDiagnosticRetentionWindowDays: 14
- NonSuccessDiagnosticRetentionMaxBytesPerRun: 5368709120
- NonSuccessDiagnosticRetentionMaxBytesWorkspace: 53687091200

Precedence and evaluation rules:
1. Per-run retention mode override replaces global Mode for successful outcomes.
2. NonSuccessOverride is evaluated independently from successful-outcome mode.
3. Hard storage caps win over time windows when both apply.
4. Cleanup stage is the only policy enforcement point.

## Concrete File Targets
Likely files to modify under Source:
- Source/ParityBench.NET.Application/Workflow/RequestComparisonWorkflowService.cs
- Source/ParityBench.NET.Application/Workflow/RequestComparisonRunRequest.cs
- Source/ParityBench.NET.Application/Runs/ComparisonRunService.cs
- Source/ParityBench.NET.Application/Runs/IComparisonRunExecutor.cs
- Source/ParityBench.NET.Application/Results/ComparisonRunResultService.cs
- Source/ParityBench.NET.Application/Requests/IRunDetailStore.cs
- Source/ParityBench.NET.Engine/BasicComparisonRunExecutor.cs
- Source/ParityBench.NET.Engine/FocusedRawContentBuilder.cs
- Source/ParityBench.NET.Workspaces/FileSystemRunDetailStore.cs
- Source/ParityBench.NET.Workspaces/FileSystemRunArtifactStore.cs
- Source/ParityBench.NET.Workspaces/FileSystemRunStore.cs
- Source/ParityBench.NET.Domain/Requests/RequestPairResult.cs
- Source/ParityBench.NET.Domain/Runs/RunOptions.cs
- Source/ParityBench.NET.Domain/Runs/ComparisonRun.cs
- Source/ParityBench.NET.Domain/Runs/RunResultSummary.cs

Likely new Source files:
- Source/ParityBench.NET.Domain/Runs/Retention/RetentionMode.cs
- Source/ParityBench.NET.Domain/Runs/Retention/NonSuccessRetentionOverride.cs
- Source/ParityBench.NET.Domain/Runs/Retention/ArtifactRetentionState.cs
- Source/ParityBench.NET.Domain/Runs/Retention/PairRetentionClass.cs
- Source/ParityBench.NET.Application/Runs/Retention/RetentionPolicyEvaluator.cs
- Source/ParityBench.NET.Application/Runs/Retention/RetentionConfiguration.cs
- Source/ParityBench.NET.Engine/Pipeline/ExecutionRecord.cs
- Source/ParityBench.NET.Engine/Pipeline/StageContracts.cs

Likely files to modify under Tests:
- Tests/ParityBench.NET.Application.Tests/...
- Tests/ParityBench.NET.Engine.Tests/BasicComparisonRunExecutorTests.cs
- Tests/ParityBench.NET.Engine.Tests/ContractProfileRunExecutorTests.cs
- Tests/ParityBench.NET.Workspaces.Tests/...
- Tests/ParityBench.NET.Infrastructure.Tests/...
- Tests/ParityBench.NET.UI.Tests/...

## Testing Requirements
### Unit and integration expectations
- Add unit tests for retention mode and non-success override evaluation logic.
- Add unit tests for precedence rules and cap-over-window behavior.
- Add integration tests validating append-before-delete sequencing.
- Add integration tests validating cleanup as sole retention owner.
- Add integration tests validating deterministic ordering by manifest ordinal.
- Add integration tests validating metadata-first read paths after trim.

### Minimum test matrix by outcome class and mode
Minimum required matrix coverage:
- Outcomes: Equal, Different, ExecutionFailed, StatusCodeMismatch, BothNonSuccess.
- Successful-outcome modes: TrimmedEqualsAndIgnoredPaths, TrimmedEquals, TrimmedIgnoredPaths, None.
- Non-success overrides: KeepBounded, KeepAll, TrimAll.

Required minimum cases:
1. Equal and Different under each successful-outcome mode.
2. ExecutionFailed, StatusCodeMismatch, BothNonSuccess under each non-success override.
3. KeepBounded with both time-window-eligible and cap-exceeded cases.
4. Workspace cap pressure scenario proving workspace cap enforcement.
5. Per-run mode override proving replacement of global mode.

### Report/export/read-path verification when artifacts are trimmed
- Verify run summary load works when raw artifacts are trimmed.
- Verify run detail paging works when raw/canonical artifacts are trimmed.
- Verify JSON/CSV export uses detail metadata and does not fail due to trimmed artifacts.
- Verify report generation still completes and labels retention state correctly.

## Validation Commands
Run these commands and include results in final implementation notes:

- dotnet build ComparisonTool.sln
- dotnet test Tests/ParityBench.NET.Application.Tests/ParityBench.NET.Application.Tests.csproj
- dotnet test Tests/ParityBench.NET.Engine.Tests/ParityBench.NET.Engine.Tests.csproj
- dotnet test Tests/ParityBench.NET.Workspaces.Tests/ParityBench.NET.Workspaces.Tests.csproj
- dotnet test Tests/ParityBench.NET.Infrastructure.Tests/ParityBench.NET.Infrastructure.Tests.csproj
- dotnet test Tests/ParityBench.NET.UI.Tests/ParityBench.NET.UI.Tests.csproj

## Safety Invariants
These must never be violated:
- append-before-delete
- cleanup sole retention owner
- deterministic ordering by manifest ordinal
- historical interpretation stability via runRetentionPolicyVersion

## Required Deliverables from the implementing agent
Provide all of the following:
1. Files changed
2. Behavior summary
3. Test evidence
4. Residual risks

Execution standard:
- Implement directly against Source and Tests with no placeholder scaffolding.
- Keep behavior deterministic across CLI, Web, Desktop, and tests.
- Do not defer required behavior behind temporary feature flags.
