# ParityBench.NET.Application

Application use cases and ports for V2.

## Owns

- Run orchestration through `ComparisonRunService`.
- Result browsing through `ComparisonRunResultService`.
- Host workflow use cases for staging, creating, starting, cancelling, and reporting runs.
- Ports for stores, execution, request sending, result access, event publishing, cancellation, and contract profiles.

## Boundaries

- References `Domain` only.
- Must not implement HTTP execution, filesystem persistence, serializers, UI, logging sinks, or host startup.
- Interfaces should describe use-case intent and keep concrete technology decisions out of the Application layer.

## Tests

Covered by `Tests/ParityBench.NET.Application.Tests`.
