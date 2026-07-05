# Slice 9: Full Host Flow Integration

## Goal

Route Web, Desktop, and CLI create/run/cancel workflows through V2 Application use cases while preserving the existing user workflows.

This slice comes after the shared result surface and static bundled report are in place, so hosts can create runs, show progress, cancel work, and browse results through the same V2 contracts.

## User-Visible Behavior

Users can opt into the V2 flow from each supported host. The expected workflow remains familiar: choose endpoints and options, stage request files, start a comparison, observe progress, cancel when needed, inspect results, and generate reports.

V1 remains available during rollout until V2 behavior parity is proven.

## Architecture Areas

- Web composition root and request-comparison flow mapping.
- Desktop composition root and BlazorWebView workflow mapping.
- CLI command adapter for V2 create/run/result/report operations.
- Host-owned input validation and option mapping into V2 Domain/Application contracts.
- Application progress and cancellation wiring.
- Workspace root selection and host-specific platform services.
- Static report bundle generation command or host action.

## V1 Parity Expectations

Hosts should preserve:

- Existing CLI command names and important flags where practical.
- Existing Web request-comparison workflow.
- Existing Desktop in-process workflow.
- Progress and cancellation behavior.
- Report output expectations.
- Result inspection behavior.

## Performance Considerations

Hosts should not own execution performance policy. They should call V2 use cases, display progress, and load summaries/details. Execution, comparison, persistence, and report writing remain in Engine, Workspaces, Infrastructure, and Application contracts.

Long-running work should be managed by the V2 application lifecycle rather than host-local fire-and-forget behavior.

## Completion Criteria

- Each host can run the V2 flow.
- Host input maps to V2 run options and rule contracts.
- Progress and cancellation work through V2 contracts.
- Hosts can browse results through the shared V2 result surface.
- Hosts can generate the V2 static bundled report.
- V2 can be selected safely before becoming the default.

## Non-Goals

- Do not rewrite host UI for its own sake.
- Do not let shared UI depend on Engine, Workspaces, Infrastructure, or host internals.
- Do not remove V1 host paths until V2 parity is complete.
