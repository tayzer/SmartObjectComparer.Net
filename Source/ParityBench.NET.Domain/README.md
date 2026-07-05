# ParityBench.NET.Domain

Pure domain contracts for V2.

## Owns

- Run identities, lifecycle state, progress, and result summaries.
- Request batch, request item, endpoint, artifact, and detail references.
- Comparison options, ignore rules, smart ignore rules, and mask rules.
- Contract profile selections and payload format vocabulary.
- Static report DTOs that are storage-neutral.

## Boundaries

- Has no project references.
- Must not depend on files, HTTP, serializers, UI, logging, DI, or host concerns.
- Types should remain immutable or effectively immutable unless a future slice explicitly needs otherwise.

## Tests

Covered by `Tests/ParityBench.NET.Domain.Tests`.
