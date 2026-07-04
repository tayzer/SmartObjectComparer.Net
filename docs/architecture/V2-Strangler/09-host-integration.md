# Slice 8: Host Integration

## Goal

Route Web, Desktop, and CLI through V2 once V2 behavior is proven.

This slice connects existing user entry points to the V2 application layer while preserving current user workflows.

## User-Visible Behavior

Users continue to run comparisons from Web, Desktop, and CLI with familiar workflows.

The host should feel the same unless V2 intentionally improves reliability, progress, cancellation, or performance without changing semantics.

## Architecture Areas

- Web gateway or API adapter.
- Desktop in-process gateway.
- CLI command adapter.
- Shared UI components and view models.
- Progress transport.
- Cancellation wiring.
- Host-level input mapping.
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

Long-running work should be managed by the V2 application/runner model, not host-local fire-and-forget behavior.

## Completion Criteria

- Each host can run the V2 flow.
- Host and shared UI input maps to V2 run options.
- Progress and cancellation work through V2 contracts.
- User workflows remain compatible.
- V2 can be selected safely before becoming default.

## Non-Goals

- Do not rewrite host UI for its own sake.
- Do not let hosts or shared UI depend on Engine or Infrastructure internals.
- Do not remove V1 host paths until V2 parity is complete.

