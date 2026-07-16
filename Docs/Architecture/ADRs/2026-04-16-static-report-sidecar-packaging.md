# Static Report Sidecar Packaging

- Date: 2026-04-16
- Status: Approved

## Context

The CLI-generated Blazor report originally embedded the full bootstrap payload directly into `index.html` and also bundled raw request/response or file bodies into the same bootstrap object for Full File View. On large request-comparison runs, especially Jenkins-scale batches, that packaging caused excessive memory pressure during report generation and inflated the payload loaded by the report host.

The shared `ComparisonTool.UI` Full File View components already lazy-load raw content through `RawContentService`, but the static Blazor report could not reopen host file-system paths from the browser. The report therefore needs an HTTP-served packaging contract that preserves Full File View without forcing every raw body into the main bootstrap JSON.

## Decision

Use a sidecar-based packaging model for static Blazor reports.

- Write the report bootstrap payload to `report.data.json` at the report root.
- Write one `raw/{pairId}.json` sidecar per non-error pair that has source paths available for Full File View.
- Store the relative sidecar path on `FilePairComparisonResult.BundledRawContentPath`.
- Keep error-pair raw content embedded in the bootstrap payload for now so the existing error-detail flow remains unchanged.
- Load `report.data.json` and raw sidecars over HTTP in `ComparisonTool.Report` using scoped services.

## Rationale

This keeps the approved change focused on the memory-pressure hotspot while preserving current report behavior.

- Externalizing `report.data.json` removes the `index.html` string-injection duplication path.
- Moving non-error raw content to sidecars preserves static Full File View for structured and raw-text differences.
- Keeping error pairs embedded avoids widening the change into the current error-detail UI flow.
- Reusing the shared `RawContentService` abstraction keeps Web and Desktop behavior unchanged.

## Trade-offs

- The report now consists of more files, which can increase artifact-hosting overhead for very large runs.
- The storage model is temporarily mixed: error pairs are embedded, while non-error pairs use sidecars.
- The report requires HTTP serving for both the bootstrap payload and raw sidecars; opening from `file://` remains unsupported.

## Alternatives considered

- Keep all raw content embedded in the bootstrap payload.
  Rejected because it is the direct memory-pressure path that failed on large runs.
- Move all pairs, including errors, to sidecars immediately.
  Rejected for this slice because it would require changing the current error-detail loading flow in the report page.
- Use sharded or chunked raw-content manifests.
  Rejected for now because it adds complexity beyond the smallest approved fix.

## Impacted projects or files

- `ComparisonTool.Cli`
- `ComparisonTool.Core`
- `ComparisonTool.Report`
- `ComparisonTool.Tests`
- `Docs/Archive/blazor-report-implementation.md`

## Verification approach

- Focused MSTest slice for `BlazorReportBundleBuilderTests`, `RawContentServiceTests`, and `BlazorReportSerializationTests`.
- Focused build of `ComparisonTool.Report`.
- Follow-up validation on a large CLI-generated report under Jenkins or an equivalent HTTP-served artifact host.

## Supersedes or superseded by

- Supersedes the earlier inline-bootstrap assumption in `Docs/Archive/blazor-report-implementation.md`.
- Not superseded by another ADR.