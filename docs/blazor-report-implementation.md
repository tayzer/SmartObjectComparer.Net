# Blazor WASM Report Implementation Checklist

## Overview

Replace the React-based `ComparisonTool.ReportUI` with a Blazor WASM report (`ComparisonTool.Report`) that reuses the existing `ComparisonTool.UI` Razor components. The CLI will serialize the full `MultiFolderComparisonResult` to JSON, inject it into a Blazor WASM template, and produce a self-contained report folder that opens in any browser without a server.

This eliminates the React/TypeScript toolchain dependency, removes data-shape duplication between C# and TypeScript, and lets the report share the exact same UI components as the desktop/web app.

## Architecture

```
CLI comparison run
  → MultiFolderComparisonResult + analysis results
  → Serialize to ReportBootstrapData JSON
  → BlazorReportBundleBuilder:
      - Copy pre-built Blazor WASM publish output (_framework/, _content/, index.html)
      - Inject JSON into <script id="report-data"> in index.html (or write report.data.json)
  → Output: self-contained folder (index.html + _framework/ + _content/ + data)
  → Open in browser — Blazor WASM boots, ReportDataService reads JSON, renders report
```

Key points:
- `ComparisonTool.Report` is a standalone Blazor WASM project referencing `ComparisonTool.UI` and `ComparisonTool.Core`
- At runtime it reads embedded JSON (no API calls) via `ReportDataService`
- All interactive services (folder picker, request comparison, progress) are stubbed as no-ops
- File export uses JS interop `saveAsFile` for downloading filtered results

---

## Phase 1: Project Foundation

Create the `ComparisonTool.Report` Blazor WASM project and verify it builds.

- [x] Create `ComparisonTool.Report/ComparisonTool.Report.csproj`
  - Blazor WASM standalone (`Microsoft.NET.Sdk.BlazorWebAssembly`)
  - `net10.0`
  - References: `ComparisonTool.UI`, `ComparisonTool.Core`
  - PackageReferences: `MudBlazor`, `Blazored.LocalStorage`, `Microsoft.AspNetCore.Components.WebAssembly`
- [x] Add `ComparisonTool.Report` to `ComparisonTool.sln`
- [x] Create `ComparisonTool.Report/wwwroot/index.html`
  - MudBlazor CSS (`_content/MudBlazor/MudBlazor.min.css`)
  - `<script src="_framework/blazor.webassembly.js"></script>`
  - `<script id="report-data" type="application/json"></script>` placeholder for injected data
  - `<div id="app">Loading report...</div>`
- [x] Create `ComparisonTool.Report/Program.cs`
  - `WebAssemblyHostBuilder.CreateDefault(args)`
  - `AddUnifiedComparisonServices()` from `ComparisonTool.Core`
  - `AddMudServices()`
  - `AddBlazoredLocalStorage()`
  - Register stub/WASM service implementations
  - Register `ReportDataService` as singleton
- [x] Create `ComparisonTool.Report/_Imports.razor`
  - Match imports from `ComparisonTool.UI/_Imports.razor`
  - Add `@using ComparisonTool.Report.Services`
- [x] Create `ComparisonTool.Report/App.razor`
  - `<MudThemeProvider>`, `<MudPopoverProvider>`, `<MudDialogProvider>`, `<MudSnackbarProvider>`
  - `<Router>` with `<Found>` / `<NotFound>`
- [x] Create `ComparisonTool.Report/Services/ReportDataService.cs`
  - Reads JSON from `<script id="report-data">` via JS interop
  - Deserializes to `ReportBootstrapData` (or `MultiFolderComparisonResult` initially)
  - Exposes data as property/task for components to consume
- [x] Create stub services:
  - [x] `WasmFileExportService` — JS interop `saveAsFile` for downloading content
  - [x] `WasmScrollService` — JS interop `scrollToElement`
  - [x] `WasmFolderPickerService` — no-op, returns empty/null
  - [x] `WasmNotificationService` — no-op
  - [x] `WasmProgressSubscriber` — no-op
  - [x] `WasmRequestComparisonGateway` — no-op
- [x] Create `ComparisonTool.Report/Pages/ReportPage.razor`
  - Read-only version of `Home.razor` results section
  - Consumes `ReportDataService` to get comparison data
  - Renders `ComparisonTool.UI` shared components (results grid, detail views, etc.)
  - Hides run/configuration controls that don't apply to a static report
- [x] Verify `ComparisonTool.Report` project builds (`dotnet build`)

---

## Phase 2: Data Serialization

Ensure the full comparison result can be serialized to JSON and deserialized in the Blazor WASM app.

- [ ] Create `ReportBootstrapData` model
  - `MultiFolderComparisonResult` — the core comparison data
  - `EnhancedStructuralAnalysisResult` — structural analysis
  - `SemanticDifferenceAnalysis` — semantic diff analysis
  - Metadata: timestamp, CLI version, source paths, comparison options used
- [x] Create custom `JsonConverter` for `KellermanSoftware.CompareNetObjects.Difference`
  - Handle `object`-typed properties (`Object1`, `Object2`, `ParentObject1`, `ParentObject2`)
  - Serialize as string representations; deserialize back appropriately
  - Handle circular references / deep nesting gracefully
- [x] Add `EnhancedStructuralDifferenceAnalyzer` call in CLI after comparison completes
  - Wire into existing comparison pipeline
  - Populate `EnhancedStructuralAnalysisResult` on the bootstrap data
- [x] Create `BlazorReportBundleBuilder` in `ComparisonTool.Cli/Reporting/`
  - Takes `ReportBootstrapData` + path to pre-built Blazor WASM output
  - Serializes data to JSON
  - Injects into `index.html` `<script id="report-data">` tag (or writes `report.data.json`)
  - Copies `_framework/`, `_content/`, and modified `index.html` to output directory
- [x] Verify round-trip serialization
  - Serialize a real `MultiFolderComparisonResult` from a CLI run
  - Deserialize in a test
  - Confirm all fields survive the round-trip (especially `Difference` objects)

---

## Phase 3: CLI Integration

Wire the Blazor report generation into the CLI commands.

- [ ] Add MSBuild targets to `ComparisonTool.Cli.csproj` for building `ComparisonTool.Report`
  - `dotnet publish ComparisonTool.Report -c Release` as a pre-build or post-build step
  - Embed or copy the publish output to a known location (e.g., `artifacts/blazor-report/`)
- [ ] Update `HtmlReportWriter` to support Blazor output mode
  - Add a code path that calls `BlazorReportBundleBuilder` instead of (or in addition to) the React bundler
  - Output the self-contained Blazor report folder
- [ ] Add `--report-engine` option to CLI commands (or replace React entirely)
  - Option values: `blazor` (default), `react` (legacy, to be removed)
  - Or: just replace the React path entirely if we're confident
- [ ] Handle StaticSite mode
  - `index.html` + `_framework/` + `_content/` + `report.data.json`
  - Ensure all paths are relative so the folder can be opened from any location
  - Test opening `index.html` directly in browser (file:// protocol)
- [ ] Test end-to-end with a real CLI comparison run
  - Run comparison → generate Blazor report → open in browser → verify data renders

---

## Phase 4: Polish & Cleanup

Finalize the implementation and remove legacy code.

- [ ] Handle large reports
  - Evaluate inline JSON size limits (very large `<script>` tags may cause issues)
  - Implement chunked loading if needed (similar to React StaticSite approach)
  - Consider external `report.data.json` file with fetch on load as default strategy
- [ ] Report-specific CSS tweaks
  - Hide interactive-only UI elements via CSS if not already handled in Razor
  - Ensure print-friendly styling
  - Match or improve on React report visual quality
- [ ] Remove `ComparisonTool.ReportUI` (React project)
- [ ] Remove `ComparisonTool.ReportUI_temp`
- [ ] Remove React build targets from `ComparisonTool.Cli.csproj`
  - Remove npm/vite build steps
  - Remove React output copy steps
- [ ] Update CI/CD pipeline
  - Remove Node.js/npm steps for ReportUI
  - Add `dotnet publish` for `ComparisonTool.Report` if not handled by MSBuild targets
- [ ] Update documentation
  - `README.md` — remove React references, document Blazor report
  - `UserGuide.md` — update report generation instructions
  - `docs/desktop-migration-plan.md` — mark report migration complete

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Blazor WASM file size** | `_framework/` can be 5-15MB+ depending on trimming | Enable IL trimming + compression in publish. The report is opened locally, not served over slow networks, so this is acceptable. |
| **`Difference` serialization** | `KellermanSoftware.CompareNetObjects.Difference` has `object`-typed properties that don't round-trip cleanly with `System.Text.Json` | Custom `JsonConverter<Difference>` that serializes object properties as their string representation. Verified via round-trip tests. |
| **RawContentService gap** | `ComparisonTool.UI` components may depend on `RawContentService` to fetch file content on demand — not available in static report | Embed raw content in `ReportBootstrapData` if needed, or gracefully degrade (show "content not available in static report"). |
| **MudBlazor providers** | Missing `MudThemeProvider` / `MudPopoverProvider` etc. causes silent rendering failures | `App.razor` must include all required MudBlazor provider components. Verified by visual inspection of rendered report. |
| **file:// protocol restrictions** | Some browsers block `fetch()` / `XMLHttpRequest` from `file://` URLs | Blazor WASM boots from inline resources. Use inline `<script>` JSON rather than external fetch. Alternatively, document that a local HTTP server is needed (e.g., `python -m http.server`). |
| **Component coupling to interactive services** | UI components may call services that are no-op in report mode, causing null refs or confusing UX | All stub services return safe defaults. UI components should null-check or use `[CascadingParameter]` for report-mode awareness. |
