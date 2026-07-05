# ParityBench.NET.Infrastructure

Concrete adapters and shared infrastructure helpers for V2.

## Owns

- HTTP endpoint sender based on `HttpClient`.
- JSON/XML response deserialization and response model registration.
- Contract profile implementations and payload serialization.
- Static report bundle writing and report asset location.
- Host-friendly generators and no-op adapters.
- Fixture response models used by manual and E2E runs.

## Boundaries

- References `Application` and `Domain`.
- Must not reference V2 hosts, V2 UI, V2 Workspaces, V2 Engine, or any V1 project.
- Concrete adapters should remain replaceable behind Application ports.

## Tests

Covered by `Tests/ParityBench.NET.Infrastructure.Tests`.
