# Slice 5: Non-Success, Errors, And Cancellation

## Goal

Complete V2 operational behavior for imperfect request-comparison runs without switching Web, Desktop, or CLI to V2.

This slice makes non-success HTTP responses inspectable, keeps execution failures as readable pair rows, and moves cancellation to a run-id-driven Application contract so hosts do not need to own local execution tokens.

## Implemented Project Shape

- `ParityBench.NET.Domain`
  - `RequestPairResult` now separates execution failure text (`ErrorMessage`) from non-error outcome text (`OutcomeMessage`).
  - Raw text result creation preserves `StatusCodeMismatch` and `BothNonSuccess` outcomes instead of reporting matching error bodies as `Equal`.
  - Raw text/status metadata is represented with existing V2-owned `ComparisonDifference` records.
- `ParityBench.NET.Application`
  - Adds `IRunCancellationRegistry` for active-run cancellation by `RunId`.
  - `ComparisonRunService` creates linked execution tokens for started runs, requests cancellation by run id, and unregisters runs after terminal completion.
- `ParityBench.NET.Engine`
  - Adds `RawTextResponseComparer`, a non-success-aware comparer wrapper used by `BasicComparisonRunExecutor`.
  - Both-2xx pairs still delegate to the existing hash/model comparer path.
  - Non-success persisted artifacts are compared as bounded raw text and stored as lightweight detail metadata.
- `ParityBench.NET.Infrastructure`
  - Adds `InMemoryRunCancellationRegistry` for current V2 composition and tests.
- `ParityBench.NET.Workspaces`
  - Persists `OutcomeMessage` and raw text differences in detail indexes.

## Runtime Behavior

1. Application starts a run and registers a linked cancellation token for the run id.
2. Engine executes requests and persists masked response artifacts as before.
3. The raw text comparer intercepts persisted pairs where at least one response is non-2xx.
4. `StatusCodeMismatch` pairs get an `HttpStatus` difference and bounded raw body line differences.
5. `BothNonSuccess` pairs get bounded raw body line differences but remain `BothNonSuccess`, even when the bodies match.
6. Endpoint exceptions still produce `ExecutionFailed` rows with readable `ErrorMessage` text.
7. Cancellation requested by run id cancels the linked execution token; the run reaches `Cancelled` and is unregistered.
8. Cancelled runs do not expose partial summaries or detail indexes in this slice.

## Raw Text Comparison Rules

- Read at most 5 KB from each persisted response artifact.
- Detect UTF BOMs and otherwise read as UTF-8.
- Normalize line endings before comparing.
- Emit line differences as `Body.Line[n]`.
- Emit `BodyPreview` metadata when either side is truncated.
- Limit emitted differences with `RunOptions.Comparison.MaxDifferences`.
- Do not add a new diff library or dependency in this slice.

## Cancellation Policy

Cancellation is an Application concern:

- `StartRunAsync` creates the active execution token through `IRunCancellationRegistry`.
- `CancelRunAsync` requests cancellation by `RunId`, saves `Cancelled`, and publishes a terminal event.
- If an executor throws after cancellation has been requested, Application treats the terminal state as `Cancelled`, not `Failed`.
- The registry is always completed after `Completed`, `Failed`, or `Cancelled` terminal handling.
- Partial result exposure, artifact cleanup, and historical cancelled-run inspection are deferred to later result/history slices.

## Tests

Coverage added or extended in this slice:

- Domain outcome-message validation, raw text result outcomes, and dedicated summary counts.
- Application run-id cancellation, cancellation after progress, cancellation after executor error, and registry cleanup.
- Engine raw text comparison for status mismatches, both-non-success bodies, matching error bodies, large body truncation, executor cancellation, and readable endpoint failures.
- Infrastructure cancellation registry token cancellation and cleanup.
- Workspaces detail index persistence for outcome messages and raw text differences.

## Non-Goals

- No host integration.
- No retry policy.
- No soft-error detection inside 2xx bodies.
- No partial summary/detail exposure for cancelled runs.
- No artifact cleanup policy for cancelled runs.
- No V1 project references from V2 projects.
