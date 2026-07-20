# Current-to-Target Architecture Decomposition

**Project:** Open-Source Local A/B Comparison Tool  
**Date:** 2026-07-04  
**Purpose:** Map the existing codebase toward a clean modular-monolith architecture without requiring a big-bang rewrite.

## Executive Summary

The current solution already has useful physical modules: Core, Domain, UI, Web, Desktop, CLI, Report, and tests. The main issue is not project layout. The main issue is responsibility ownership inside `ComparisonTool.Core` and the host-specific paths that drive request comparison.

The target architecture should be a hexagonal modular monolith:

```text
Hosts -> UI/Application -> Domain + Ports -> Engine/Infrastructure adapters
```

The first decomposition target should be request comparison. It has the strongest pressure from concurrency, temporary artifacts, per-run configuration, progress, cancellation, reporting, and multi-host behavior.

## Target Boundary Model

### Domain

Pure contracts and rules. No file system, HTTP, DI, logging, UI, or host concerns.

Owns:

- Run identity and lifecycle states.
- Request comparison configuration.
- Immutable comparison options.
- Rule definitions.
- Summary/detail result contracts.
- Endpoint labels and logical request identities.

Candidate types:

- `ComparisonRunId`
- `ComparisonRunStatus`
- `ComparisonRunOptions`
- `ComparisonRunPlan`
- `ComparisonRunSummary`
- `ComparisonRunDetail`
- `ComparisonRuleSet`
- `EndpointDefinition`
- `RequestWorkItem`

### Application

Use cases and orchestration policy. It coordinates ports but should not know path layouts, `HttpClient`, Blazor, WPF, or CLI details.

Owns:

- Start run.
- Cancel run.
- Query run status.
- List previous runs.
- Load run summary.
- Load run detail.
- Save/load named configurations.
- Translate progress events into run state.

Candidate handlers:

- `StartComparisonRunHandler`
- `CancelComparisonRunHandler`
- `GetRunStatusHandler`
- `ListRunSummariesHandler`
- `LoadRunDetailHandler`
- `SaveRunConfigurationHandler`

### Engine

Host-agnostic execution and comparison behavior.

Owns:

- Build request work items from a run plan.
- Execute A/B endpoint requests.
- Stream responses.
- Classify outcomes.
- Compare successful responses.
- Compare non-success responses as raw text.
- Apply masking.
- Run semantic/enhanced analysis.
- Aggregate final metrics.

Candidate services:

- `IComparisonRunExecutor`
- `IRequestPlanBuilder`
- `IEndpointPairExecutor`
- `IResponseClassifier`
- `IResponseComparer`
- `IRawTextDifferenceService`
- `IAnalysisRunner`
- `IResultAssembler`

### Workspace / Storage

File-system persistence behind repositories. The first implementation can wrap the current temp folders; the later implementation can use the workspace layout.

Owns:

- Config storage.
- Run directories.
- Raw response artifacts.
- Focused/raw sidecars.
- Summary and detail files.
- Cleanup policy.

Candidate ports:

- `IWorkspaceStore`
- `IConfigRepository`
- `IRunStore`
- `IArtifactStore`
- `IRunArtifactReader`

### Infrastructure

Adapters for external capabilities.

Owns:

- `HttpClient` and `SocketsHttpHandler` configuration.
- File-system implementation of workspace/artifact stores.
- JSON/XML serializers.
- Clocks and ID generation.
- Logging adapters.
- Report writers.
- Progress transports such as SignalR, in-process events, and console output.

Candidate adapters:

- `HttpEndpointClient`
- `FileSystemArtifactStore`
- `FlatFileWorkspaceStore`
- `SystemClock`
- `GuidRunIdGenerator`
- `SignalRRunEventPublisher`
- `InProcessRunEventPublisher`
- `ConsoleRunEventPublisher`

### Hosts

Thin entry points only.

Owns:

- DI composition.
- Host-specific UI, command-line, or platform adapters.
- Routing/window startup.
- Mapping user input to application requests.

Hosts:

- `ComparisonTool.Web`
- `ComparisonTool.Desktop`
- `ComparisonTool.Cli`
- `ComparisonTool.Report`

## Current-to-Target Responsibility Map

| Current Area | Current Responsibility | Target Owner | Migration Move |
| --- | --- | --- | --- |
| `RequestComparisonJobService` | Job state, execution phases, chunking, result metadata, analysis, temp paths, cleanup, progress | Application + Engine + Storage | Split into run coordinator, executor, result assembler, run store, artifact store, progress publisher |
| `RequestExecutionService` | Bounded A/B HTTP execution, response persistence, request-body reading | Engine + Infrastructure | Keep execution policy in Engine; move HTTP and streaming persistence behind `IEndpointClient` and `IArtifactStore` |
| `RequestFileParserService` | Reads staged request batch files and header sidecars | Engine or Application input adapter | Convert into request-plan builder input; keep sidecar parsing as a strategy/adapter |
| `DirectoryComparisonService` | Folder comparison, analysis, temp focused artifacts | Engine | Preserve as existing comparison adapter initially; later split comparison from artifact concerns |
| `ComparisonConfigurationService` | Mutable shared comparison options and ignore rules | Domain/Application options | Replace per-run mutation with immutable `ComparisonRunOptions` and `ComparisonRuleSet` passed into comparison operations |
| `RawTextComparisonService` | Non-success/raw response diffing | Engine | Keep as a strategy behind `IResponseComparer` for raw/non-success outcomes |
| `RawContentService` | Lazy full/focused raw content loading | Application + Storage + UI adapter | Move artifact lookup behind `IRunArtifactReader`; UI asks for detail content by run/detail ID |
| `BlazorReportWriter` and report bundle builder | Static report packaging and sidecars | Infrastructure report adapter | Keep behavior, make it consume `ComparisonRunSummary` and detail/artifact readers |
| `RequestComparisonApi` | Web endpoints, upload, job creation, task kickoff, cancellation dictionary | Web host + Application | Keep endpoints but route through application handlers and managed background runner |
| `InProcessRequestComparisonGateway` | Desktop staging, job start, polling, cancellation | Desktop host adapter | Keep as adapter, but call application use cases rather than core job service directly |
| `RequestCompareCommand` | CLI parsing, staging, job execution, reporting | CLI host + Application | Keep command contract; delegate execution to application use cases and report adapters |
| `RequestComparisonPanel.razor` | UI state, request construction, upload/stage, start, progress, result loading | UI + Application contracts | Keep component behavior, but depend on gateway/use-case contracts instead of deep core services |
| Temp folders under `Path.GetTempPath()` | Request batches, response jobs, comparison chunks | Storage | Encapsulate in `IArtifactStore`; later swap implementation to workspace runs |

## Proposed Dependency Shape

Current dependency direction is broadly:

```text
Web/Desktop/CLI/UI -> Core -> Domain
Report -> UI/Core
```

Target dependency direction:

```text
Web/Desktop/CLI
  -> UI/Application contracts
  -> Application
  -> Domain + Ports

Engine
  -> Domain + Application ports

Infrastructure
  -> Application ports + Engine ports

Report
  -> Application read models + artifact readers
```

The target does not require all projects to be split immediately. The first step can be namespace and service-boundary extraction inside `ComparisonTool.Core`. New projects should be introduced only when boundaries are stable enough to enforce.

## Target Run Flow

```text
1. Host gathers user input.
2. Host maps input to ComparisonRunOptions.
3. Application validates options and creates a run record.
4. Application builds ComparisonRunPlan.
5. A managed runner invokes IComparisonRunExecutor.
6. Executor emits RunEvents and writes artifacts through IArtifactStore.
7. Endpoint client streams responses to artifacts.
8. Response classifier routes pairs to structured comparer or raw-text comparer.
9. Result assembler writes detail files and aggregate summary.
10. Host displays progress and loads summaries/details through application read use cases.
```

## First Target Interfaces

These are design-level contracts. Exact DTO shape can be refined during implementation, but the responsibility split should remain stable.

```csharp
public interface IComparisonRunExecutor
{
    Task ExecuteAsync(
        ComparisonRunPlan plan,
        IRunEventPublisher events,
        CancellationToken cancellationToken);
}

public interface IRunStore
{
    Task<ComparisonRunId> CreateAsync(ComparisonRunOptions options, CancellationToken cancellationToken);
    Task UpdateStatusAsync(ComparisonRunId runId, ComparisonRunStatus status, CancellationToken cancellationToken);
    Task<ComparisonRunSummary?> LoadSummaryAsync(ComparisonRunId runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ComparisonRunSummary>> ListSummariesAsync(CancellationToken cancellationToken);
}

public interface IArtifactStore
{
    Task<Stream> OpenRequestBodyAsync(RequestWorkItem request, CancellationToken cancellationToken);
    Task<Stream> CreateResponseArtifactAsync(ComparisonRunId runId, RequestWorkItem request, EndpointSide side, CancellationToken cancellationToken);
    Task WriteDetailAsync(ComparisonRunId runId, ComparisonRunDetail detail, CancellationToken cancellationToken);
}

public interface IEndpointClient
{
    Task<EndpointResponseMetadata> SendAsync(
        EndpointDefinition endpoint,
        Stream requestBody,
        Stream responseDestination,
        CancellationToken cancellationToken);
}

public interface IRunEventPublisher
{
    Task PublishAsync(RunEvent runEvent, CancellationToken cancellationToken);
}
```

## Migration Slices

### Slice 1: Characterize Current Behavior

Goal: freeze behavior before introducing seams.

Actions:

- Add concurrent request-job tests with different ignore rules and options.
- Add tests around cancellation terminal state.
- Add tests for large response handling assumptions.
- Capture CLI compatibility expectations: options, output formats, and exit codes.
- Capture Web/Desktop equivalence for request options submitted to the core flow.

Done when:

- The highest-risk behavior is covered before extraction begins.
- Existing request-comparison integration tests still serve as parity anchors.

### Slice 2: Immutable Run Options

Goal: remove per-job mutation of shared comparison configuration.

Actions:

- Introduce `ComparisonRunOptions` and `ComparisonRuleSet`.
- Map current `CreateRequestComparisonJobRequest` and CLI options into those immutable options.
- Adapt comparison execution to receive run-specific options.
- Keep existing public Web/CLI/Desktop contracts unchanged.

Done when:

- Two concurrent jobs with different options can run without configuration bleed.
- Existing UI, CLI, and integration tests still pass.

### Slice 3: Extract Run Executor and Result Assembly

Goal: split `RequestComparisonJobService` without changing observable behavior.

Actions:

- Extract the execution loop into `IComparisonRunExecutor`.
- Extract final metadata and summary creation into a result assembler.
- Leave `RequestComparisonJobService` as a compatibility facade during this slice.
- Keep current temp storage behind existing paths until `IArtifactStore` is introduced.

Done when:

- Hosts still call the same facade or gateway.
- Executor can be tested without Web/Desktop/CLI.

### Slice 4: Introduce Artifact Store

Goal: hide temp path construction and prepare for workspace storage.

Actions:

- Introduce `IArtifactStore` for request batches, responses, comparison materialization, focused content, and detail artifacts.
- Implement `TempArtifactStore` first using current folder layout.
- Move direct `Path.GetTempPath()` usage out of request-comparison orchestration.
- Preserve report sidecar behavior.

Done when:

- Switching from temp-backed storage to workspace-backed storage is a DI/configuration decision, not an orchestration rewrite.

### Slice 5: Managed Run Lifecycle

Goal: replace host fire-and-forget execution with a controlled run runner.

Actions:

- Introduce a run queue/runner abstraction.
- Web host uses a background service.
- Desktop host can use an in-process runner adapter.
- CLI can invoke the runner synchronously and wait for completion.
- Cancellation is handled by run ID rather than host-local dictionaries.

Done when:

- Web graceful shutdown, cancellation, and error handling are centralized.
- Desktop and CLI keep their current user-facing behavior.

### Slice 6: Workspace Store

Goal: implement the high-level workspace model after execution boundaries are stable.

Actions:

- Add `FlatFileWorkspaceStore` for `.abproject`, `Configs/`, and `Runs/`.
- Map run summaries to `summary.json`.
- Map request details to deterministic detail artifacts.
- Update reporting reads to load summaries first and details lazily.
- Keep temp storage available for transitional/non-workspace runs if needed.

Done when:

- Historical run dashboard can read summaries without loading raw/detail content.
- CLI and UI can run against a user-selected workspace.

### Slice 7: Optional Channel Pipeline

Goal: add staged backpressure only if measurements justify it.

Actions:

- Introduce channels between planning, execution, comparison, and persistence stages.
- Keep bounded capacities configurable.
- Preserve the `IComparisonRunExecutor` contract.

Done when:

- Large runs show improved memory stability or throughput versus the simpler executor.
- Channel usage remains internal to Engine and does not leak into UI/host contracts.

## Public Behavior To Preserve

These should remain compatible unless explicitly reprioritized:

- Request batch execution against endpoint A and endpoint B.
- Endpoint labels, headers, content-type override, SOAPAction, and range selection.
- Ignore rules, smart ignores, collection-order handling, namespace handling, string options, and null/empty collection handling.
- Alternate-contract request/response normalization.
- Non-success response raw-text comparison and failed-pair rows.
- CLI command names, core flags, output formats, and exit codes.
- Static Blazor report packaging and lazy raw-content sidecars.
- Web/Desktop progress, cancellation, and final result exploration.

## Implementation Details To Replace

These are not product behavior and should be treated as replaceable:

- Singleton mutable comparison configuration for per-run settings.
- Fire-and-forget `Task.Run` job startup.
- Host-local cancellation dictionaries.
- Scattered temp path construction.
- Whole-response byte-array materialization.
- Duplicated request-comparison DI registrations.
- UI components injecting deep core orchestration services directly.

## Test Plan

Use existing tests as parity anchors and add missing architectural tests.

Existing anchors:

- Request comparison alternate-contract integration tests.
- Large-batch request comparison tests.
- Raw-text comparison tests.
- Response masking tests.
- Request file parser and batch stager tests.
- CLI request command tests.
- Blazor report bundle and serialization tests.

New tests before implementation refactors:

- Concurrent jobs with different ignore rules produce independent results.
- Concurrent jobs with different namespace/string/collection options produce independent results.
- Cancelling a run reaches a stable `Cancelled` terminal state and leaves readable metadata.
- Large responses are streamed or bounded rather than fully retained in memory.
- Web, Desktop, and CLI produce equivalent `ComparisonRunOptions` for the same logical request.
- Artifact store path safety prevents traversal and preserves duplicate relative request names.

## Decision Points

These do not need to be decided before the first decomposition slices:

- Whether to create new physical projects immediately or keep new boundaries inside `ComparisonTool.Core` temporarily.
- Whether to introduce `System.Threading.Channels`; this should be measurement-driven.
- Whether workspace storage replaces temp storage completely or coexists as a selected mode.
- Whether enhanced structural heuristics become configurable profiles or a plugin-style extension point.

Recommended defaults:

- Start with internal namespaces/folders before adding new projects.
- Do not introduce channels until `IComparisonRunExecutor` and `IArtifactStore` are stable.
- Keep public Web/CLI/Desktop behavior compatible during the first five slices.
- Treat workspace storage as Slice 6, not Slice 1.

## Acceptance Criteria For This Decomposition

- Every major current request-comparison responsibility has a target owner.
- The first implementation slice can be started without changing user-visible behavior.
- The plan identifies which behavior must be preserved and which implementation details can be replaced.
- The migration path reduces risk before introducing the full workspace model.
- Future implementation can be done slice-by-slice without a big-bang rewrite.
