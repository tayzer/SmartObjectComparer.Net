# Slice 8: Static Report Bundle And Host Flow Integration

## Goal

Use the shared V2 result surface from Slice 7 as the foundation for static bundled reports and full host workflows.

This slice comes after the result UI exists, so Web, Desktop, and bundled reports can share the same result components and view contracts instead of maintaining separate report models.

## User-Visible Behavior

Users continue to run comparisons from Web, Desktop, and CLI with familiar workflows once V2 is selected.

Static reports remain standalone artifacts, but they should render the same V2 result surface as the interactive hosts, backed by report-side data files and lazy raw-content sidecars.

## Architecture Areas

- Web gateway or API adapter.
- Desktop in-process gateway.
- CLI command adapter.
- Shared UI input mapping.
- Static report data source.
- Static report asset packaging.
- Progress transport.
- Cancellation wiring.
- Feature flag or explicit V2 selection during rollout.

## V1 Parity Expectations

Hosts should preserve:

- Existing CLI command names and important flags.
- Existing Web request-comparison workflow.
- Existing Desktop in-process workflow.
- Progress and cancellation behavior.
- Report output expectations.
- Result inspection workflow.

## Performance Considerations

Hosts should not own execution performance policy. They should call V2 use cases, display progress, and load summaries/details. Shared presentation state and components belong in `ParityBench.NET.UI`.

Static report generation should avoid embedding every large body in the bootstrap payload. Raw and focused content should remain lazy sidecar data where possible.

Long-running work should be managed by the V2 application/runner model, not host-local fire-and-forget behavior.

## Completion Criteria

- Each host can run the V2 flow.
- Host and shared UI input maps to V2 run options.
- Progress and cancellation work through V2 contracts.
- Static bundled reports render the shared V2 result surface.
- User workflows remain compatible.
- V2 can be selected safely before becoming default.

## Non-Goals

- Do not rewrite host UI for its own sake.
- Do not let hosts or shared UI depend on Engine or Infrastructure internals.
- Do not remove V1 host paths until V2 parity is complete.