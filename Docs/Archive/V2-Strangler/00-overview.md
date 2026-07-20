# V2 Controlled Strangler Migration Overview

> TODO: filename slice numbers in this directory don't all match the slice number in each file's own H1 heading (off by one in places, e.g. `09-host-integration.md` heading says "Slice 8"). Needs a renumbering pass — not fixed as part of the open-source cleanup, flagged here for follow-up.

## Purpose

V2 is a parallel implementation of the comparison flow using the target architecture. It does not replace V1 immediately. V1 remains the behavioral oracle while V2 grows slice by slice until it reaches full parity.

This is a controlled strangler migration. Each slice recreates a meaningful vertical part of existing behavior in V2, proves parity against V1, and leaves the system in a working state.

## Migration Rules

- V1 is frozen for new feature development.
- V1 may still receive critical fixes, characterization tests, and parity-support changes.
- V2 is the home for new architecture and future feature work.
- V2 follows the target architecture: Domain, Application, Engine, Workspaces, Infrastructure, shared UI, and thin Hosts.
- V2 implementation follows the engineering guidelines in `11-engineering-guidelines.md`.
- Each slice must deliver user-observable behavior, not only internal structure.
- Performance improvements are allowed when they preserve existing semantics.
- V1 is deprecated only after V2 proves full behavior parity.

## Target Architecture

V2 uses a hexagonal modular-monolith shape:

```text
Web / Desktop -> UI -> Application -> Domain
CLI ----------------> Application -> Domain

Application ports are implemented by:
  Engine
  Workspaces
  Infrastructure
```

The intended V2 project model is:

- `ParityBench.NET.Domain`: pure models, run state, result contracts, and rule definitions.
- `ParityBench.NET.Application`: use cases, pipeline orchestration, job lifecycle, and progress events.
- `ParityBench.NET.Engine`: HTTP execution, comparison pipeline, diff strategies, masking, and analysis.
- `ParityBench.NET.Workspaces`: file-system workspace implementation for configs, runs, summaries, and detail files.
- `ParityBench.NET.Infrastructure`: HttpClient adapters, serializers, report writers, logging, and system resource probes.
- `ParityBench.NET.UI`: shared Blazor components and view models.
- `ParityBench.NET.Web`, `ParityBench.NET.Desktop`, and `ParityBench.NET.Cli`: thin hosts for DI, platform services, UI or command entry points.
- `ParityBench.NET.Report`: static Blazor WebAssembly host for bundled reports over the shared V2 result UI.

Domain owns pure concepts and rules. Application owns use cases and orchestration policy. Engine owns host-agnostic execution and comparison behavior. Workspaces owns durable file-system layout. Infrastructure owns external technical adapters. UI owns shared presentation behavior. Hosts collect input, configure platform services, display progress, and load results. The static report host reads pre-packaged V2 report sidecars and does not execute runs. Hosts compose concrete adapters through DI; Application consumes contracts rather than host or adapter internals.

## Slice Completion Standard

A slice is complete only when:

- The intended behavior exists in V2.
- V1 parity has been checked for that slice.
- User-facing behavior has not regressed.
- V2 architecture boundaries are respected.
- V2 engineering guidelines are respected.
- Performance improvements preserve existing semantics.
- Any temporary V1 bridge is explicit and isolated.

## End State

The final system preserves the behavior users depend on today while moving to a cleaner architecture:

- Thin hosts.
- Shared UI components and view models.
- Application-level use cases.
- Immutable run options.
- Engine-owned execution and comparison.
- Workspace-owned configs, runs, summaries, and detail files.
- Infrastructure-owned HttpClient adapters, serialization, reporting, logging, and system probes.
- Streaming-friendly and bounded-memory performance.
- V1 safely deprecated after parity is complete.

