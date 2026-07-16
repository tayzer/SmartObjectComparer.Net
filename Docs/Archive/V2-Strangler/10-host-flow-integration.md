# Slice 9: V2 Host Flow Integration

## Goal

Wire the first opt-in V2 create/run/cancel/report workflow through V2 Web, V2 Desktop, and the new `ParityBench.NET.Cli` host without replacing any existing `ComparisonTool.*` V1 host.

This slice comes after the shared V2 result surface and static bundled report are in place. Hosts now compose the V2 Application, Engine, Workspaces, Infrastructure, shared UI, and report writer into real workflows while V1 remains available.

## User-Visible Behavior

V2 Web and V2 Desktop show a request-comparison setup surface alongside the shared run history and result viewer. Users can enter a request directory, endpoint A/B URLs, timeout, concurrency, model name, content-type override, simple headers, core comparison toggles, and an optional report output path.

The V2 CLI adds this command shape:

```text
request <request-directory> --endpoint-a <url> --endpoint-b <url> [--model Auto] [--concurrency <n>] [--timeout <seconds>] [--content-type <type>] [--header <Name: Value>] [--header-a <Name: Value>] [--header-b <Name: Value>] [--report-output <directory>] [--report-assets <directory>]
```

The CLI stages the request directory, creates a run, executes it synchronously, prints summary counts, returns non-zero for failed or cancelled runs, and writes a V2 static bundled report when `--report-output` is supplied.

## Architecture Areas

- `RequestComparisonRunRequest` captures host input as immutable Application workflow input.
- `IRequestComparisonWorkflowUseCases` stages request directories, creates runs, starts runs, cancels runs, and generates reports.
- `IComparisonRunJobUseCases` starts Web/Desktop runs in-process without blocking the UI and prevents duplicate starts for the same run id.
- `IRequestBatchReferenceGenerator`, `GuidRunIdGenerator`, `GuidRequestBatchReferenceGenerator`, and `NoOpRunEventPublisher` provide host-safe concrete infrastructure.
- `SelectableResponseComparer` keeps `Auto` as raw/hash comparison and routes registered model names to model-aware comparison.
- `ReportAssetLocator` resolves published report assets from a configured path, `AppContext.BaseDirectory/BlazorReportAssets`, or the current directory.
- `ParityBench.NET.UI.Workflow` contains shared run-flow UI components and an Application-backed data source.
- `ParityBench.NET.Web`, `ParityBench.NET.Desktop`, and `ParityBench.NET.Cli` are composition roots only.

## Composition Rules

Shared UI references only V2 Domain and Application contracts. It does not reference Engine, Workspaces, Infrastructure, hosts, or V1 projects.

Web, Desktop, and CLI may reference concrete V2 layers because they are composition roots. No V2 project references `ComparisonTool.*` projects.

Hosts register only V2-owned built-in sample/expected response model types. V1 model types remain outside V2.

## Report Assets

Web, Desktop, and CLI include a publish-time target that publishes `ParityBench.NET.Report` into `BlazorReportAssets`. The static report writer can also use an explicitly configured report asset directory, which is useful for tests and local tooling.

Report generation remains V2-only: it uses the shared V2 result contracts, paged detail files, and lazy raw sidecars introduced in the static report slice.

## Completion Criteria

- V2 Web can create, start, cancel, browse, and generate reports for V2 runs.
- V2 Desktop can create, start, cancel, browse, and generate reports for V2 runs.
- `ParityBench.NET.Cli` can run the V2 request comparison command and optionally write a static report.
- `Auto` model mode works without V1 registrations.
- Unknown explicit model names fail before run creation.
- Boundary tests continue to prove V2 projects do not reference V1 projects.

## Non-Goals

- Do not replace existing V1 Web, Desktop, or CLI flows.
- Do not add full V1 CLI flag parity yet.
- Do not add named endpoint configuration parity yet.
- Do not deprecate or archive V1 projects yet.
- Do not introduce focused raw artifacts until V2 generates focused raw content.