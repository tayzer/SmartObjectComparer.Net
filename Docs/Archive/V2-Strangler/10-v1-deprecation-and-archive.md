# Slice 10: V1 Deprecation And Archive

## Goal

Retire the old flow only after V2 proves report parity, host workflow parity, and full behavior parity.

This slice marks V1 request-comparison paths as deprecated, removes temporary bridge adapters, updates documentation, and archives or removes old projects and components that are no longer needed.

## User-Visible Behavior

V2 becomes the default path. Users should not lose behavior they relied on in V1.

Any remaining V1 access should be clearly marked as deprecated or unavailable.

## Architecture Areas

- V1 deprecation markers.
- Legacy adapter removal.
- Documentation updates.
- Host default switching.
- Test suite cleanup.
- Project archive or removal decisions.

## V1 Parity Expectations

Before V1 is deprecated, V2 must cover all agreed V1 behavior:

- Request comparison.
- Core options.
- Alternate contracts.
- Non-success and failure behavior.
- Cancellation.
- Large-run behavior.
- Static bundled reports.
- Web, Desktop, and CLI workflows running through V2.

## Performance Considerations

The V2 default should preserve the performance improvements established in earlier slices. Removing V1 should not reintroduce full-body buffering, shared mutable run state, or host-local lifecycle control.

## Completion Criteria

- V2 is the default flow.
- V1 parity matrix is complete.
- Temporary V1 bridge adapters are removed.
- Documentation clearly describes the V2 architecture as current.
- Old projects/components are archived or removed according to the agreed repository policy.

## Non-Goals

- Do not deprecate V1 before parity is proven.
- Do not keep duplicate flows indefinitely.
- Do not remove tests that still protect V2 behavior unless equivalent V2 tests exist.

