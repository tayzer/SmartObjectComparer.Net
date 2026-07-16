# Slice 1: Run Model And Orchestration Shape

## Goal

Define the V2 comparison-run vocabulary and lifecycle.

This slice establishes the conceptual model that all later slices use: run identity, lifecycle states, options, progress, summaries, details, artifacts, cancellation, and terminal outcomes.

## Implemented Shape

Slice 1 introduces the first real V2 code in `ParityBench.NET.Domain` and `ParityBench.NET.Application`.

The Domain layer owns pure run concepts:

- `RunId`: validated non-empty run identity.
- `RunStatus`: `Created`, `Pending`, `Parsing`, `Executing`, `Comparing`, `Analyzing`, `Finalizing`, `Completed`, `Failed`, `Cancelled`.
- `RunProgress`: percent, message, optional completed and total item counts.
- `RunOptions`: request batch reference, endpoint A/B definitions, timeout, concurrency, and model name.
- `ComparisonRun`: immutable aggregate with explicit lifecycle transition methods.
- `RunResultSummary`: count-only summary designed for cheap loading.
- `ArtifactReference` and `RunDetailReference`: logical references for later workspace-backed artifacts.
- `RunEvent`: lifecycle/progress event emitted by Application.

The Application layer owns orchestration contracts:

- `IComparisonRunUseCases`: create, start, cancel, list, and load summary.
- `IRunStore`: save/load/list run snapshots and load summaries.
- `IComparisonRunExecutor`: future Engine execution boundary.
- `IRunProgressReporter`: executor-to-Application progress boundary.
- `IRunEventPublisher`: future host/adapter event publication boundary.
- `IRunIdGenerator`: run identity creation boundary.
- `ComparisonRunService`: lifecycle service that saves and publishes state transitions.

## Lifecycle Table

| Status | Meaning | Terminal |
| --- | --- | --- |
| `Created` | Run has been created and stored, but no execution has started. | No |
| `Pending` | Run is accepted for execution but not actively processing yet. | No |
| `Parsing` | Request inputs are being parsed or planned. | No |
| `Executing` | Endpoint A/B execution is underway. | No |
| `Comparing` | Responses are being compared. | No |
| `Analyzing` | Analysis is running over comparison output. | No |
| `Finalizing` | Summaries, metadata, and artifact references are being finalized. | No |
| `Completed` | Run finished successfully and has a summary. | Yes |
| `Failed` | Run failed and has an error message. | Yes |
| `Cancelled` | Run was cancelled before normal completion. | Yes |

Terminal runs cannot be restarted, advanced, completed again, failed again, or cancelled again.

## User-Visible Behavior

No host is switched to V2 in this slice.

The observable value is architectural: V2 now has a stable run lifecycle that later Web, Desktop, CLI, and report surfaces can share.

## Architecture Areas

- Domain run identity and status.
- Immutable run options.
- Application use-case boundaries.
- Progress and run-event vocabulary.
- Summary and detail read models.
- Artifact references that can later be stored by `ParityBench.NET.Workspaces`.

## V1 Parity Expectations

The V2 run model can represent the important states and metadata currently exposed by V1 jobs:

- Created or pending.
- Executing.
- Comparing.
- Analyzing.
- Completed.
- Failed.
- Cancelled.
- Request counts and progress messages.
- Result metadata needed by reports and UI.

## Performance Considerations

The run model supports lazy result loading from the start. `RunResultSummary` is count-only and may point to a detail index reference without loading raw response bodies or pair details.

Artifact and detail references are logical identifiers. Absolute paths and file-system layout remain future `ParityBench.NET.Workspaces` concerns.

## Completion Criteria

- V2 has implemented run identity, lifecycle, options, events, summaries, details, and artifacts.
- The model can represent V1 terminal states and progress concepts.
- Application orchestration ports exist for create, start, cancel, list, and load-summary use cases.
- The lifecycle service is tested with fake storage, execution, event publication, and ID generation.
- The model is independent of Web, Desktop, CLI, workspace paths, and temp folder paths.

## Non-Goals

- Do not implement full endpoint execution.
- Do not migrate alternate contracts.
- Do not add production Workspaces, Engine, Infrastructure, UI, or host implementations.
- Do not define final workspace layout in detail; that belongs to the Workspaces slice.
- Do not require host integration.