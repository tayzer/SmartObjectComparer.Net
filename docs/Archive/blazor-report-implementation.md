# Blazor WASM Report Implementation Checklist

## Overview

Replace the React-based `ComparisonTool.ReportUI` with a Blazor WASM report (`ComparisonTool.Report`) that reuses the existing `ComparisonTool.UI` Razor components. The CLI serializes the full `MultiFolderComparisonResult` to `report.data.json`, writes per-pair raw-content sidecars for Full File View, and produces a self-contained report folder that opens in any browser without a server.

This eliminates the React/TypeScript toolchain dependency, removes data-shape duplication between C# and TypeScript, and lets the report share the exact same UI components as the desktop/web app.

## Architecture

```
CLI comparison run
  → MultiFolderComparisonResult + analysis results
  → Serialize to report.data.json
  → BlazorReportBundleBuilder:
      - Copy pre-built Blazor WASM publish output (_framework/, _content/, index.html)
      - Write `report.data.json` at the report root
      - Write `raw/{pairId}.json` sidecars for non-error Full File View content
  → Output: self-contained folder (index.html + report.data.json + raw/ + _framework/ + _content/)
  → Open in browser — Blazor WASM boots, ReportDataService fetches `report.data.json`, and RawContentService loads sidecars on demand for Full File View
```

Key points:
- `ComparisonTool.Report` is a standalone Blazor WASM project referencing `ComparisonTool.UI` and `ComparisonTool.Core`
- At runtime it fetches `report.data.json` via `ReportDataService`
- Static report Full File View uses raw-content sidecars referenced by each pair; XML and JSON payloads are pretty-formatted during CLI bundle generation for display.
- Error pairs remain embedded in the bootstrap payload for the current error-detail flow.
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
  - `<div id="app">Loading report...</div>`
- [x] Create `ComparisonTool.Report/Program.cs`
  - `WebAssemblyHostBuilder.CreateDefault(args)`
  - `AddUnifiedComparisonServices()` from `ComparisonTool.Core`
  - `AddMudServices()`
  - `AddBlazoredLocalStorage()`
  - Register scoped `HttpClient` with the report base address
  - Register stub/WASM service implementations
  - Register `ReportDataService` as scoped
- [x] Create `ComparisonTool.Report/_Imports.razor`
  - Match imports from `ComparisonTool.UI/_Imports.razor`
  - Add `@using ComparisonTool.Report.Services`
- [x] Create `ComparisonTool.Report/App.razor`
  - `<MudThemeProvider>`, `<MudPopoverProvider>`, `<MudDialogProvider>`, `<MudSnackbarProvider>`
  - `<Router>` with `<Found>` / `<NotFound>`
- [x] Create `ComparisonTool.Report/Services/ReportDataService.cs`
  - Reads `report.data.json` via `HttpClient`
  - Deserializes to `ReportBootstrapData` (or `MultiFolderComparisonResult` initially)
  - Exposes data as property/task for components to consume
- [x] Create `ComparisonTool.Report/Services/HttpBundledRawContentAccessor.cs`
  - Loads `raw/{pairId}.json` sidecars over HTTP for shared Full File View components
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
  - Serializes bootstrap data to `report.data.json`
  - Keeps error-pair raw content embedded for the current error-detail view
  - Assigns `BundledRawContentPath` references for non-error pairs with available source paths
  - Copies `_framework/`, `_content/`, and `index.html` to output directory unchanged
- [x] Verify round-trip serialization
  - Serialize a real `MultiFolderComparisonResult` from a CLI run
  - Deserialize in a test
  - Confirm all fields survive the round-trip (especially `Difference` objects)

---

## Phase 3: CLI Integration

Wire the Blazor report generation into the CLI commands.

- [x] Add MSBuild targets to `ComparisonTool.Cli.csproj` for building `ComparisonTool.Report`
  - `dotnet publish ComparisonTool.Report -c Release` as a pre-build step (conditional on `_framework/` existence)
  - Published output copied to `BlazorReportAssets/` in CLI output directory
  - Replaced React npm build targets with Blazor publish targets
- [x] Create `BlazorReportWriter` to produce Blazor report folder
  - Copies pre-published Blazor WASM assets to output directory
  - Writes `report.data.json` to the report root
  - Writes `raw/{pairId}.json` sidecars for non-error Full File View content
  - Generates `serve.cmd` and `serve.sh` launcher scripts for local HTTP serving
- [x] Replace React HTML output path entirely
  - `FolderCompareCommand` and `RequestCompareCommand` Html cases now use `BlazorReportWriter`
  - `--html-mode SingleFile` shows deprecation notice (Blazor always produces static-site folder)
- [x] Handle static-site output mode
  - `index.html` + `report.data.json` + `raw/` + `_framework/` + `_content/` + CSS/JS all relative-pathed
  - `<base href="./" />` for relative path resolution
  - Launcher scripts for local HTTP serving (file:// blocked by CORS for `fetch()`)
- [x] Add publish optimization to `ComparisonTool.Report.csproj`
  - `PublishTrimmed=true`, `BlazorEnableCompression=true`, `InvariantGlobalization=true`
- [x] Test end-to-end with a real CLI comparison run
  - Run comparison → generate Blazor report → verified report folder structure
  - Confirmed: `index.html` (57KB with injected JSON), `_framework/`, `_content/`, `serve.cmd`/`serve.sh`

---

## Phase 4: Polish & Cleanup

Finalize the implementation and remove legacy code.

- [ ] Handle large reports
  - Monitor `report.data.json` size and `raw/` sidecar file count on large request-comparison runs
  - Implement sharded raw-content manifests if per-pair sidecar count becomes an artifact-hosting bottleneck
  - Evaluate whether error pairs should also move to sidecars in a future cleanup
- [ ] Report-specific CSS tweaks
  - Hide interactive-only UI elements via CSS if not already handled in Razor
  - Ensure print-friendly styling
  - Match or improve on React report visual quality
- [x] Remove `ComparisonTool.ReportUI` (React project directory deleted)
- [x] Remove `ComparisonTool.ReportUI_temp` (backup directory deleted)
- [x] Remove React build targets from `ComparisonTool.Cli.csproj`
  - Removed npm/vite build steps (done in Phase 3)
  - Removed React embedded resource (done in Phase 3)
- [x] Remove dead React report code
  - Deleted `HtmlReportWriter.cs`, `HtmlReportBundleData.cs` (~1040 lines), `HtmlReportWriteResult.cs`
  - Deleted `HtmlReportMode.cs` and all `--html-mode` option wiring from commands
  - Deleted `HtmlReportBundleBuilderTests.cs` (4 dead tests)
  - Removed `HtmlMode`, `HtmlDefaultPageSize`, `HtmlDetailChunkSize` from `ReportContext`
- [ ] Update CI/CD pipeline
  - Remove Node.js/npm steps for ReportUI
  - Add `dotnet publish` for `ComparisonTool.Report` if not handled by MSBuild targets
- [x] Update documentation — README.md and UserGuide.md had no React references to remove

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Blazor WASM file size** | `_framework/` can be 5-15MB+ depending on trimming | Enable IL trimming + compression in publish. The report is opened locally, not served over slow networks, so this is acceptable. |
| **`Difference` serialization** | `KellermanSoftware.CompareNetObjects.Difference` has `object`-typed properties that don't round-trip cleanly with `System.Text.Json` | Custom `JsonConverter<Difference>` that serializes object properties as their string representation. Verified via round-trip tests. |
| **RawContentService gap** | `ComparisonTool.UI` components lazy-load Full File View content, but browsers cannot reopen host file-system paths from a static report | Use `BundledRawContentPath` sidecars for non-error pairs and keep error pairs embedded until the error-detail flow is migrated. |
| **MudBlazor providers** | Missing `MudThemeProvider` / `MudPopoverProvider` etc. causes silent rendering failures | `App.razor` must include all required MudBlazor provider components. Verified by visual inspection of rendered report. |
| **file:// protocol restrictions** | Browsers block `fetch()` / `XMLHttpRequest` from `file://` URLs, and the report now loads `report.data.json` and raw sidecars over HTTP | Keep the explicit file:// warning and local-server launch scripts. Jenkins artifacts work because Jenkins serves the report over HTTP. |
| **Component coupling to interactive services** | UI components may call services that are no-op in report mode, causing null refs or confusing UX | All stub services return safe defaults. UI components should null-check or use `[CascadingParameter]` for report-mode awareness. |
