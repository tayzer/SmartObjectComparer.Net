# Slice 7: V2 Shared Result Surface And Host Shells

## Goal

Create the shared V2 result-viewing surface that Web, Desktop, and the future bundled Blazor report can all use.

This slice replaces the earlier idea of adapting V2 results into the V1 report pipeline. Reports remain a required product feature, but the static report should package the same V2 UI/result contracts used by the interactive hosts instead of depending on V1 projects.

## User-Visible Behavior

Users can open opt-in V2 Web and Desktop shells that browse historical V2 runs from a workspace.

The shells are intentionally read-only in this slice. They can list runs, show cheap summary counts, page pair details, inspect differences, and load bounded raw response previews on demand.

## Architecture Areas

- Result read contracts.
- Historical run listing.
- Lazy pair-detail paging.
- Bounded artifact preview loading.
- Shared Blazor result components.
- Application-backed UI data source.
- Opt-in V2 Web host shell.
- Opt-in V2 Desktop host shell.

## V1 Parity Expectations

V2 should preserve the report inspection shape users rely on:

- Historical run summary counts.
- Pair table navigation.
- Pair detail inspection.
- Difference metadata visibility.
- Raw response inspection without eager full-body loading.

This slice does not reuse V1 report projects or V1 report models.

## Performance Considerations

Historical browsing loads run snapshots and summary counts first. Pair details are loaded through paged queries. Raw response artifacts are opened only when a selected pair needs a preview, and previews are byte-bounded.

The shared UI must not depend on Engine, Workspaces, Infrastructure, host projects, or V1 projects. Hosts compose concrete V2 implementations through dependency injection.

## Completion Criteria

- V2 Application exposes result browsing use cases.
- V2 Workspaces can stream and page detail indexes.
- `ParityBench.NET.UI` contains reusable result-view components.
- `ParityBench.NET.Web` and `ParityBench.NET.Desktop` exist as opt-in host shells.
- Host shells render the shared result UI against V2 workspace data.
- Boundary tests prove V2 UI has no concrete-layer or V1 references.

## Next Slices

- Static bundled Blazor report: package the same `ParityBench.NET.UI` result components with a report-side data source and lazy sidecars.
- Full host flow integration: route create/run/cancel workflows through V2 in Web, Desktop, and CLI.
- V1 deprecation: remove or archive V1 only after V2 behavior parity is proven.

## Non-Goals

- Do not switch existing V1 Web or Desktop hosts to V2.
- Do not add a public V2 CLI command.
- Do not generate static report bundles yet.
- Do not reintroduce V1 project references into V2.