# Slice 1: Run Model And Orchestration Shape

## Goal

Define the V2 comparison-run vocabulary and lifecycle.

This slice establishes the conceptual model that all later slices use: run identity, lifecycle states, options, progress, summaries, details, artifacts, cancellation, and terminal outcomes.

## User-Visible Behavior

Users should eventually see a run as a stable operation with clear progress, status, result summary, and detail access.

This slice may not expose the full user flow yet, but it defines the shape that Web, Desktop, CLI, and reports will share.

## Architecture Areas

- Domain run identity and status.
- Immutable run options.
- Application use-case boundaries.
- Progress and run-event vocabulary.
- Summary and detail read models.
- Artifact references that can later be stored by `ParityBench.NET.Workspaces`.

## V1 Parity Expectations

The V2 run model must be able to represent the important states and metadata currently exposed by V1 jobs:

- Pending or created.
- Executing.
- Comparing.
- Analyzing.
- Completed.
- Failed.
- Cancelled.
- Request counts and progress messages.
- Result metadata needed by reports and UI.

## Performance Considerations

The run model should support lazy result loading from the start. Summaries should not require loading all raw response bodies or all pair details.

## Completion Criteria

- V2 has documented run identity, lifecycle, options, events, summaries, details, and artifacts.
- The model can represent V1 terminal states and progress concepts.
- The model is independent of Web, Desktop, CLI, and temp folder paths.

## Non-Goals

- Do not implement full endpoint execution.
- Do not migrate alternate contracts.
- Do not define final workspace layout in detail; that belongs to the Workspaces slice.
- Do not require host integration.

