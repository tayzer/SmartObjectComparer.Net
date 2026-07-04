# Slice 5: Non-Success, Errors, And Cancellation

## Goal

Complete V2 operational behavior around imperfect runs.

This slice covers non-success responses, failed request rows, stable cancellation, partial-result metadata where appropriate, error reporting, and readable terminal states.

## User-Visible Behavior

Users receive predictable results when runs are cancelled, endpoints fail, responses return non-success statuses, or only part of a batch succeeds.

The final state should be clear and inspectable.

## Architecture Areas

- Response outcome classification.
- Raw-text comparison path.
- Error metadata.
- Cancellation model.
- Partial result policy.
- Terminal state handling.
- Progress event completion.

## V1 Parity Expectations

V2 should match V1 for:

- Both-success pairs.
- Both-non-success raw-text comparison.
- Status-code mismatch behavior.
- One-or-both-failed rows.
- Error messages captured in results.
- Cancellation terminal state.
- Progress event publication on failure or cancellation.

## Performance Considerations

Raw-text comparison should preserve bounded reads for large bodies. Error and cancellation handling should avoid leaving unreadable or inconsistent artifacts.

## Completion Criteria

- V2 has stable terminal states for completed, failed, and cancelled runs.
- Non-success responses produce expected raw-text results.
- Failed requests produce readable result rows.
- Cancellation is driven by run identity, not host-local state.
- Parity tests cover mixed outcomes and cancellation.

## Non-Goals

- Do not introduce retry policy unless it exists in V1 behavior.
- Do not redefine HTTP success rules beyond V1 parity.
- Do not require final Workspaces history features.

