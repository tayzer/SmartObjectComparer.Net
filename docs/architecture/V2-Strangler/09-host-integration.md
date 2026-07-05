# Slice 8: V2 Static Bundled Report

## Goal

Package completed V2 run results into a standalone Blazor WebAssembly report that renders the same shared `ParityBench.NET.UI` result surface introduced in Slice 7.

This slice replaces the earlier idea of adapting the V1 report project. Static reports are a core feature, but they should be another thin V2 host over the shared result components and V2 report-side data contracts.

## User-Visible Behavior

Users can receive a static report folder for a completed V2 run. The folder contains the published report host, `report.data.json`, paged detail files, lazy raw sidecars, and simple local server launchers.

The report is read-only. It opens the shared V2 result surface, loads the run summary first, pages pair details from sidecar JSON files, and reads bounded raw response previews only when a pair is selected.

## Architecture Areas

- `ParityBench.NET.Report` as a Blazor WebAssembly static host.
- `ParityBench.NET.Domain.Reports` contracts for report manifest, run snapshot, detail page metadata, schema version, and static detail pages.
- Report-side `IRunResultsViewDataSource` implementation backed by `HttpClient`.
- Infrastructure-owned `StaticReportBundleWriter` that packages published report assets, manifest data, detail pages, raw sidecars, launcher scripts, and a top-level redirector.
- Shared `ParityBench.NET.UI` result components reused unchanged.

## Data Layout

```text
report-output/
  index.html
  _framework/
  _content/
  report.data.json
  details/
    page-000000.json
    page-000001.json
  raw/
    {safeArtifactId}.body
  serve.cmd
  serve.sh
report-output.html
```

`report.data.json` contains schema version `1`, generated time, a run snapshot, summary counts, default detail page size `100`, and detail page metadata. Raw response bodies are never embedded into the manifest.

Detail page files contain rewritten artifact references that point at `raw/...` sidecars. Sidecar names are safe deterministic identifiers, not workspace paths.

## Performance Considerations

The report host loads `report.data.json` once, then lazily loads detail pages from `details/page-{index}.json`. Filtered detail queries scan page files incrementally and materialize only the requested result page. Raw sidecars are fetched only for selected pairs and read as bounded previews using `maxBytes + 1`.

The bundle writer stream-copies raw artifacts from `IRunArtifactStore.OpenReadAsync` to sidecars. It must not buffer or embed raw bodies into manifest JSON.

Focused raw sidecars are not introduced yet because V2 does not currently generate focused raw artifacts.

## Completion Criteria

- `ParityBench.NET.Report` renders the shared V2 result surface.
- The report host references only V2 `Domain` and `UI` projects.
- Static report DTOs use schema version `1` and default detail page size `100`.
- `StaticReportBundleWriter` writes published assets, manifest, paged details, raw sidecars, launcher scripts, and redirector HTML.
- Raw artifact references are rewritten to safe report sidecar paths.
- Boundary tests prove the report host and Infrastructure do not depend on V1 projects.

## Following Slice

Full host flow integration remains the next slice. Web, Desktop, and CLI create/run/cancel workflows should move to V2 only after the shared result surface and static report path are in place.

## Non-Goals

- Do not switch existing V1 Web, Desktop, or CLI flows to V2.
- Do not reference `ComparisonTool.Report`, `ComparisonTool.UI`, `ComparisonTool.Core`, or any other V1 project.
- Do not create a separate report-specific result UI.
- Do not add focused raw sidecars until V2 produces focused raw artifacts.
- Do not deprecate V1 until report parity and host workflow parity are proven.
