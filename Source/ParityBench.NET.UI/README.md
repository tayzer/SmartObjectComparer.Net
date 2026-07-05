# ParityBench.NET.UI

Shared Blazor UI components for V2 hosts and reports.

## Owns

- Run workflow components for creating, starting, cancelling, and reporting V2 runs.
- Result browsing components for run history, summaries, detail pages, differences, and raw previews.
- UI data-source abstractions and Application-backed data-source implementations.
- UI-only parsing for manual workflow inputs such as headers and comparison rules.

## Boundaries

- References `Application` and `Domain` only.
- Must not reference Engine, Workspaces, Infrastructure, hosts, or V1 projects.
- Components should stay host-neutral so Web, Desktop, and static reports can share the same result surface.

## Tests

Covered by `Tests/ParityBench.NET.UI.Tests`.
