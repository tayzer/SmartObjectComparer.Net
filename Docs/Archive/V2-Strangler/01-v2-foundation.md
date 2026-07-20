# Slice 0: V2 Foundation

## Goal

Create the V2 architectural space and document the migration rules before any behavior is moved.

This slice establishes where V2 work belongs, how the projects or folders are organized, what dependencies are allowed, and how temporary V1 bridge code is isolated.

## User-Visible Behavior

No user-facing behavior changes in this slice.

The observable outcome is organizational: future work has a clear destination and the team has a shared rulebook for the migration.

## Architecture Areas

- V2 folder or project boundary.
- `ParityBench.NET.Domain`: pure models, run state, result contracts, and rule definitions.
- `ParityBench.NET.Application`: use cases, pipeline orchestration, job lifecycle, and progress events.
- `ParityBench.NET.Engine`: HTTP execution, comparison pipeline, diff strategies, masking, and analysis.
- `ParityBench.NET.Workspaces`: file-system workspace implementation for configs, runs, summaries, and detail files.
- `ParityBench.NET.Infrastructure`: HttpClient adapters, serializers, report writers, logging, and system resource probes.
- `ParityBench.NET.UI`: shared Blazor components and view models.
- `ParityBench.NET.Web`, `ParityBench.NET.Desktop`, and `ParityBench.NET.Cli`: thin hosts only.
- Dependency direction.
- Legacy adapter policy.
- Documentation structure.
- Parity-test strategy.
- V2 engineering guidelines.

## V1 Parity Expectations

V1 remains the source of truth. This slice does not attempt to replicate behavior yet.

The key parity outcome is procedural: every future V2 behavior slice must state what V1 behavior it is matching and how parity will be checked.

## Performance Considerations

This slice only documents performance goals. It does not introduce performance mechanisms.

The performance direction is:

- Prefer streaming over full-body buffering.
- Prefer immutable per-run options over shared mutable configuration.
- Prefer lazy detail loading for reports and historical results.
- Add staged processing only when large-run behavior justifies it.

## Completion Criteria

- V2 has a documented architectural boundary.
- The V2 project responsibility map matches the target design.
- Dependency direction is documented.
- V1 freeze rules are documented.
- Temporary legacy adapter rules are documented.
- The slice sequence is documented.
- Engineering guidelines for V2 implementation are documented.

## Non-Goals

- Do not create user-facing V2 behavior.
- Do not replace V1 services.
- Do not modify V1 behavior.
- Do not implement workspace storage behavior yet.

