# ParityBench.NET.Engine

Execution and comparison pipeline for V2.

## Owns

- Request pair execution orchestration inside `BasicComparisonRunExecutor`.
- Response comparison strategies, including hash-only and CompareNETObjects-backed comparison.
- Response masking before persistence and comparison.
- Contract profile request preparation and response normalization flow.

## Boundaries

- References `Application` and `Domain`.
- Uses Application ports for request batches, senders, artifacts, details, and profiles.
- Must not know filesystem layouts, host configuration, UI state, or V1 projects.

## Tests

Covered by `Tests/ParityBench.NET.Engine.Tests`.
