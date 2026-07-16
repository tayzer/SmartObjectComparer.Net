# Full Rollout Multi-PR Implementation Prompt

## Context and Non-Negotiable Decisions
Implement the full retention and staged pipeline rollout defined by NorthStar in multiple pull requests.

Alignment requirements:
- Keep behavior aligned with `Docs/AI-Full-Rollout-Implementation-Prompt.md` and `Docs/NorthStar.md`.
- Keep host behavior deterministic and consistent across CLI, Web, Desktop, and tests.

Approved decisions (mandatory):
1. NorthStar defaults are approved and must be implemented as-is.
2. Non-success override `TrimAll` is allowed in production.
3. Retention settings support both global defaults and per-run overrides.
4. Full rollout target remains mandatory (no phased no-op or metadata-only fallback path).

Execution rules for all PRs:
- Each PR must keep the solution buildable.
- Each PR must keep tests green for touched scopes.
- Do not defer required behavior behind temporary feature flags.

## Global Safety Invariants
These invariants must hold in every PR and in final behavior:
- Append before delete.
- Cleanup is the sole retention owner.
- Deterministic ordering by manifest ordinal.
- Historical interpretation stability through `runRetentionPolicyVersion`.
- Result/read/export/report paths remain functional when artifacts are intentionally trimmed.

## Explicit Configuration Contract (Must Match Single-Prompt Contract)
Configuration keys:
- `ParityBench:Retention:Mode`
- `ParityBench:Retention:NonSuccessOverride`
- `ParityBench:Retention:NonSuccessDiagnosticRetentionWindowDays`
- `ParityBench:Retention:NonSuccessDiagnosticRetentionMaxBytesPerRun`
- `ParityBench:Retention:NonSuccessDiagnosticRetentionMaxBytesWorkspace`

Allowed values:
- Retention mode: `TrimmedEqualsAndIgnoredPaths`, `TrimmedEquals`, `TrimmedIgnoredPaths`, `None`
- Non-success override: `KeepBounded`, `KeepAll`, `TrimAll`

Defaults:
- `Mode`: `TrimmedEqualsAndIgnoredPaths`
- `NonSuccessOverride`: `KeepBounded`
- `NonSuccessDiagnosticRetentionWindowDays`: `14`
- `NonSuccessDiagnosticRetentionMaxBytesPerRun`: `5368709120`
- `NonSuccessDiagnosticRetentionMaxBytesWorkspace`: `53687091200`

Precedence and evaluation rules:
1. Per-run retention mode override replaces global `Mode` for successful outcomes.
2. `NonSuccessOverride` is evaluated independently from successful-outcome mode.
3. Hard storage caps win over time windows when both apply.
4. Cleanup stage is the only policy enforcement point.

## PR Plan Overview (PR1, PR2, PR3, PR4)
- PR1: Schema + contracts + config foundations; backward-compatible reads; no destructive retention deletion.
- PR2: Executor stage decomposition and cleanup stage wiring; explicit stage ownership and append-before-delete path readiness.
- PR3: Retention enforcement and artifact deletion semantics; policy matrix implementation and metadata writes.
- PR4: Read/report/export compatibility and hardening; trimmed-artifact tolerance, labeling, observability, and full matrix tests.

## PR1: Schema + Contracts + Config
### Objective
Establish retention domain contracts, run/pair metadata fields, and configuration binding with strict validation and backward-compatible read handling.

### In scope
- Add retention enums/options/contracts.
- Add run-level metadata fields:
  - `runRetentionMode`
  - `runRetentionPolicyVersion`
  - `comparisonRulesSnapshotHash`
- Add pair-level metadata fields:
  - `pairRetentionClass`
  - `artifactRetentionState` per artifact family
  - `retentionAppliedAt`
- Bind and validate retention config in composition roots that execute runs.
- Add per-run override fields in request/run contracts.
- Ensure old persisted payloads remain readable with safe defaults.

### Out of scope
- Any artifact deletion/trimming behavior.
- Cleanup policy execution logic.
- Broad executor stage refactor.

### Concrete file targets
- `Source/ParityBench.NET.Domain/Runs/RunOptions.cs`
- `Source/ParityBench.NET.Domain/Runs/ComparisonRun.cs`
- `Source/ParityBench.NET.Domain/Runs/RunResultSummary.cs`
- `Source/ParityBench.NET.Domain/Requests/RequestPairResult.cs`
- `Source/ParityBench.NET.Application/Workflow/RequestComparisonRunRequest.cs`
- `Source/ParityBench.NET.Application/Workflow/RequestComparisonWorkflowService.cs`
- `Source/ParityBench.NET.Application/Runs/ComparisonRunService.cs`
- `Source/ParityBench.NET.Workspaces/FileSystemRunStore.cs`
- `Source/ParityBench.NET.Workspaces/FileSystemRunDetailStore.cs`
- `Source/ParityBench.NET.Application/Runs/Retention/RetentionConfiguration.cs` (new)
- `Source/ParityBench.NET.Domain/Runs/Retention/RetentionMode.cs` (new)
- `Source/ParityBench.NET.Domain/Runs/Retention/NonSuccessRetentionOverride.cs` (new)
- `Source/ParityBench.NET.Domain/Runs/Retention/ArtifactRetentionState.cs` (new)
- `Source/ParityBench.NET.Domain/Runs/Retention/PairRetentionClass.cs` (new)

### Required tests
- Unit tests for config binding and allowed-value validation.
- Unit tests for backward-compatible reads of older run/detail payloads.
- Unit tests for per-run override parsing and persistence.

### Merge gate / acceptance criteria
- All new retention contracts compile and serialize/deserialize.
- Config keys and defaults exactly match this prompt.
- Existing historical run/detail data reads without exceptions or semantic corruption.
- Build succeeds and touched-scope tests pass.

### Handoff notes to next PR
- PR2 can assume retention metadata contracts exist.
- PR2 must not alter PR1 config key names, defaults, or precedence contract.

## PR2: Executor Stage Decomposition + Cleanup Stage Wiring
### Objective
Refactor executor flow into explicit stages with clear ownership and wire a cleanup stage path that runs after durable append.

### In scope
- Decompose responsibilities into explicit stages:
  - Planning
  - Execution
  - Compare
  - Persistence
  - Cleanup
- Preserve `IComparisonRunExecutor` entry point while splitting internals.
- Ensure persistence appends by manifest ordinal.
- Wire cleanup invocation only after append completion.
- Prepare append-before-delete enforcement path (no policy deletion behavior yet).

### Out of scope
- Full retention policy matrix behavior.
- Non-success override enforcement logic.
- Artifact trimming/deletion semantics beyond structural wiring.

### Concrete file targets
- `Source/ParityBench.NET.Application/Runs/IComparisonRunExecutor.cs`
- `Source/ParityBench.NET.Engine/BasicComparisonRunExecutor.cs`
- `Source/ParityBench.NET.Engine/Pipeline/ExecutionRecord.cs` (new)
- `Source/ParityBench.NET.Engine/Pipeline/StageContracts.cs` (new)
- `Source/ParityBench.NET.Application/Runs/ComparisonRunService.cs`
- `Source/ParityBench.NET.Workspaces/FileSystemRunDetailStore.cs`

### Required tests
- Integration tests proving stage order and ownership boundaries.
- Integration tests proving append-before-cleanup invocation ordering.
- Tests proving deterministic detail append ordering by manifest ordinal.

### Merge gate / acceptance criteria
- Stage boundaries are explicit and test-verified.
- Cleanup stage is invoked post-append and is the only future retention hook.
- No retention deletion happens in planning/execution/compare stages.
- Build succeeds and touched-scope tests pass.

### Handoff notes to next PR
- PR3 should plug retention decisions into the cleanup stage only.
- PR3 must keep PR2 stage ownership unchanged.

## PR3: Retention Enforcement + Artifact Deletion Semantics
### Objective
Implement retention policy matrix and deletion semantics in cleanup, including non-success overrides and cap/window precedence.

### In scope
- Implement outcome-based retention policy matrix.
- Implement `KeepBounded`, `KeepAll`, and `TrimAll` non-success behavior.
- Enforce hard caps over windows when both constraints apply.
- Implement artifact family retention state writes:
  - `retained`
  - `trimmedByPolicy`
  - `missingUnexpectedly`
- Write `retentionAppliedAt` for each applied decision.
- Ensure delete operations execute only after durable append.

### Out of scope
- UI/report/export hardening changes beyond required metadata correctness.
- Unrelated performance optimizations.

### Concrete file targets
- `Source/ParityBench.NET.Application/Runs/Retention/RetentionPolicyEvaluator.cs` (new)
- `Source/ParityBench.NET.Engine/BasicComparisonRunExecutor.cs`
- `Source/ParityBench.NET.Workspaces/FileSystemRunArtifactStore.cs`
- `Source/ParityBench.NET.Workspaces/FileSystemRunDetailStore.cs`
- `Source/ParityBench.NET.Domain/Requests/RequestPairResult.cs`
- `Source/ParityBench.NET.Domain/Runs/RunResultSummary.cs`

### Required tests
- Unit tests for policy matrix decisions by outcome class.
- Unit tests for non-success override behavior (`KeepBounded`, `KeepAll`, `TrimAll`).
- Unit tests for cap-over-window precedence.
- Integration tests for append-before-delete enforcement.
- Integration tests for `retentionAppliedAt` and `artifactRetentionState` persistence.

### Merge gate / acceptance criteria
- Policy behavior matches default contract and override semantics.
- Deletion semantics only run through cleanup after durable append.
- Metadata fields are correctly persisted for all artifact families.
- Build succeeds and touched-scope tests pass.

### Handoff notes to next PR
- PR4 should assume retention metadata is authoritative and available for read paths.
- PR4 should not re-implement retention decisions in read/report/export services.

## PR4: Read/Report/Export Compatibility + Hardening
### Objective
Guarantee read/report/export behavior remains correct and resilient when artifacts are retained, trimmed by policy, or unexpectedly missing; add observability and complete matrix tests.

### In scope
- Make result/read/export/report flows metadata-first and trim-tolerant.
- Correctly label artifact state as:
  - `retained`
  - `trimmedByPolicy`
  - `missingUnexpectedly`
- Ensure previews prefer focused artifacts when available.
- Add stage/cleanup observability metrics and counters.
- Complete matrix tests across modes, outcomes, and overrides.

### Out of scope
- New retention policy types.
- Host-specific divergent behavior.

### Concrete file targets
- `Source/ParityBench.NET.Application/Results/ComparisonRunResultService.cs`
- `Source/ParityBench.NET.Engine/FocusedRawContentBuilder.cs`
- `Source/ParityBench.NET.Workspaces/FileSystemRunDetailStore.cs`
- `Source/ParityBench.NET.Workspaces/FileSystemRunStore.cs`
- Report/export/read-path services under `Source/ParityBench.NET.Application` and `Source/ParityBench.NET.Report` as needed
- Test projects under `Tests/ParityBench.NET.*.Tests`

### Required tests
- Matrix coverage:
  - Outcomes: `Equal`, `Different`, `ExecutionFailed`, `StatusCodeMismatch`, `BothNonSuccess`
  - Successful-outcome modes: `TrimmedEqualsAndIgnoredPaths`, `TrimmedEquals`, `TrimmedIgnoredPaths`, `None`
  - Non-success overrides: `KeepBounded`, `KeepAll`, `TrimAll`
- KeepBounded cap pressure tests:
  - Per-run cap exceeded
  - Workspace cap exceeded
  - Window-eligible but cap-trimmed precedence
- Export/report tests proving success when artifacts are trimmed.
- Tests proving explicit labeling of retained vs trimmedByPolicy vs missingUnexpectedly.

### Merge gate / acceptance criteria
- Read/report/export surfaces are trim-safe and semantically correct.
- Observability covers stage lifecycle and retention effects.
- Full required matrix tests pass.
- Build succeeds and touched-scope tests pass.

### Handoff notes to next PR
- Not applicable; PR4 completes rollout scope.

## Final Rollout Verification Checklist
Before considering rollout complete:
- [ ] All four PRs merged in sequence with no contract regressions.
- [ ] Global invariants validated by tests and code review.
- [ ] Config contract keys, defaults, and precedence exactly match this prompt.
- [ ] `runRetentionPolicyVersion` persisted and used for historical interpretation.
- [ ] Cleanup remains sole retention owner; no earlier-stage deletion behavior exists.
- [ ] Append-before-delete validated under concurrent execution conditions.
- [ ] Read/report/export paths verified for retained, trimmedByPolicy, and missingUnexpectedly cases.
- [ ] Required solution/test commands executed successfully for touched scopes in each PR.

## Required Final Deliverables from Implementing Agent
Provide all of the following in the final implementation report:
1. Files changed per PR (grouped by PR1-PR4).
2. Behavior summary per PR mapped to objectives and acceptance gates.
3. Test evidence per PR (commands run, pass/fail counts, notable matrix coverage).
4. Invariant verification evidence (append-before-delete, cleanup ownership, deterministic ordering, policy-version stability).
5. Residual risks and follow-up recommendations, if any.
