# Slice 6: Performance And Large Run Behavior

## Goal

Improve V2 execution internals without changing user-facing results.

This slice focuses on large-run safety: streaming responses to artifacts, avoiding unnecessary full-body memory usage, bounded concurrency, lazy detail loading, and staged processing where useful.

## User-Visible Behavior

Users should see equivalent summaries, details, and reports, but V2 should handle large request batches and large responses more safely and predictably than V1.

## Architecture Areas

- Streaming endpoint execution.
- Artifact-backed response persistence.
- Bounded concurrency.
- Large-run planning.
- Lazy detail loading.
- Optional staged processing.
- Performance telemetry and run timings.

## V1 Parity Expectations

User-facing output remains equivalent:

- Same pair outcomes.
- Same summary counts.
- Same key metadata.
- Same report-visible details.
- Same cancellation and failure semantics.

Implementation may differ when the difference only improves memory, throughput, or storage behavior.

## Performance Considerations

V2 should prefer:

- Streaming response bodies to artifacts.
- Bounded memory use independent of response size.
- Summary-first result storage.
- Detail and raw-content lazy loading.
- Bounded worker counts.
- Channels or staged processing only when measurements justify them.

## Completion Criteria

- Large responses do not require full-body memory retention during normal execution.
- Large runs can be processed with bounded concurrency.
- Summary loading remains cheap.
- Performance tests prove improved memory or throughput behavior.
- User-visible results remain equivalent to V1.

## Non-Goals

- Do not add complex pipeline machinery solely for architecture purity.
- Do not sacrifice small-run simplicity.
- Do not change output semantics for performance reasons.

