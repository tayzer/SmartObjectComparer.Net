# ParityBench.NET

[![Build & Format & Test](https://github.com/tayzer/ComparisonTool/actions/workflows/ci.yml/badge.svg)](https://github.com/tayzer/ComparisonTool/actions/workflows/ci.yml)
[![License: Apache-2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)

**Prove that version B of an API still behaves like version A — across thousands of requests, without drowning in false positives.**

ParityBench.NET fires the same requests at two endpoints, projects both responses onto a shared in-memory model, and diffs them as objects rather than as text. You get a list of the differences that actually matter, not every timestamp and trace ID that happened to change.

## Why not a text diff?

A text or XML diff compares bytes. These two responses are identical in meaning and completely different as text:

```xml
<!-- Endpoint A (SOAP, v1) -->
<Customer><Id>4471</Id><Name>ACME Ltd</Name><TraceId>a91f…</TraceId></Customer>
```

```json
// Endpoint B (JSON, v2) — reordered, retyped, different transport
{ "name": "ACME Ltd", "id": 4471, "traceId": "c02e…" }
```

ParityBench deserializes both sides into the same canonical type first, so it knows `id` and `Id` are the same field, that `4471` and `"4471"` are the same value, that collection order may or may not be significant, and that `traceId` is noise you told it to ignore. What's left is signal.

## What you get

| | |
|---|---|
| **A/B endpoint comparison** | Same request to two endpoints, concurrently, with bounded concurrency and per-endpoint headers |
| **Baseline vs Live** | Capture version A once, replay it against B months later — A never has to be running. [Guide](Docs/Guides/baseline-vs-live.md) |
| **Plugin extensibility** | Add a new API pair as a versioned package dropped in a folder — no product rebuild. [Guide](Docs/Guides/building-a-plugin.md) |
| **Noise suppression** | Ignore rules, pattern-based smart ignores, field masking, accepted-difference profiles. [Guide](Docs/Guides/comparison-rules.md) |
| **Scale** | Thousands of request pairs at bounded memory — artifacts are persisted and re-read, never held in RAM. [Design](Docs/Architecture/high-level-design.md) |
| **Reports** | Paged result browsing in-app, plus a self-contained static Blazor report you can hand to someone else. [Guide](Docs/Guides/reports-and-results.md) |
| **Three hosts** | Desktop (WPF), Web (Blazor Server), and CLI — all over the same engine |
| **Process isolation** | Optionally run client plugin code in a separate worker process, so a plugin fault fails the run and not the app |

## Quick start

Prerequisites: **.NET 10 SDK** (Windows for the Desktop host; Web, CLI and tests are cross-platform).

```bash
dotnet restore ComparisonTool.sln
dotnet build ComparisonTool.sln -c Release
```

Try it against the built-in fixture endpoints — no external API needed. In one terminal:

```bash
dotnet run --project Source/ParityBench.NET.TestEndpoints --no-launch-profile --urls http://localhost:5056
```

In another, compare the sample XML/XML scenarios:

```bash
dotnet run --project Source/ParityBench.NET.Cli -- request Examples/ParityBench.NET.ManualRuns/xml-xml --endpoint-a http://localhost:5056/consumer-report/soap/a --endpoint-b http://localhost:5056/consumer-report/soap/b
```

Or drive the same run from a UI:

```bash
dotnet run --project Source/ParityBench.NET.Desktop
```

```bash
dotnet run --project Source/ParityBench.NET.Web
```

New here? Read [Getting Started](Docs/Guides/getting-started.md) — it walks the full first run and explains what you're looking at.

## Documentation

| | |
|---|---|
| [Getting Started](Docs/Guides/getting-started.md) | First run, end to end |
| [Comparison rules](Docs/Guides/comparison-rules.md) | Ignore rules, smart ignores, masking, accepted differences |
| [Reports and results](Docs/Guides/reports-and-results.md) | Reading results, run history, static reports |
| [Retention and workspace](Docs/Guides/retention-and-workspace.md) | Where data lives, what gets cleaned up |
| [Baseline vs Live](Docs/Guides/baseline-vs-live.md) | Comparing against a version that no longer runs |
| [Building a plugin](Docs/Guides/building-a-plugin.md) | Adding your own API pair |
| [High-level design](Docs/Architecture/high-level-design.md) | Architecture and system flow diagram |
| [Docs index](Docs/README.md) | Everything, including decision records |

## CLI

From a build tree, invoke as `dotnet run --project Source/ParityBench.NET.Cli -- <args>`. From a published output, the executable is `ParityBench.NET.Cli.exe`. Both are written below as `ParityBench.NET.Cli` for brevity.

```
ParityBench.NET.Cli request [<request-directory>]
    (--endpoint-a <url> --endpoint-b <url> | --preset <id> | --run-profile <id>)
    [--model <name>] [--profile <contract-profile-id>]
    [--concurrency <n>] [--timeout <seconds>] [--content-type <type>]
    [--header <Name: Value>] [--header-a <Name: Value>] [--header-b <Name: Value>]
    [--capture-baseline <name>] [--baseline <name>[@<version>]]
    [--report-output <directory>] [--report-assets <directory>]
    [--log-level <level>] [--log-durations] [--log-exceptions]
    [--persist-diagnostics] [--slow-path-threshold-ms <n>]

ParityBench.NET.Cli baseline list
ParityBench.NET.Cli baseline export <name>[@<version>] <file.pbbaseline>
ParityBench.NET.Cli baseline import <file.pbbaseline>
ParityBench.NET.Cli baseline delete <name>[@<version>]
```

- `<request-directory>` is required unless `--preset` or `--run-profile` supplies one.
- Endpoints are required as absolute URLs unless a preset or run profile supplies them. Explicit flags always win.
- `--run-profile` selects a saved plugin run profile (the current extensibility model); `--preset` selects a compile-time contract profile (legacy).
- `--header` applies to both endpoints, `--header-a` / `--header-b` to one side. All three are repeatable.

Full reference: [`Source/ParityBench.NET.Cli/README.md`](Source/ParityBench.NET.Cli/README.md).

## Repository layout

| Path | Contents |
|---|---|
| `Source/` | Product code — SDK, domain, application, engine, workspaces, plugins, worker, hosts |
| `Tests/` | One test project per source project, plus architecture fitness tests |
| `Docs/` | Guides, architecture, decision records |
| `Examples/` | Manual-run fixtures used by presets and end-to-end tests |
| `build/` | Shared MSBuild targets, including plugin packaging |
| `scripts/` | Publishing and reporting helpers |
| `archive/v1-comparisontool/` | Frozen V1 predecessor, not built by CI |

Every project under `Source/` and `Tests/` has its own `README.md` stating what it owns and what it must not reference. Start there before a cross-project change.

## Extending it

Adding support for your own API pair means writing a **plugin**: a class library compiled against `ParityBench.PluginSdk`, shipped as a package, discovered at run time, and selected by a saved **run profile**. It never requires rebuilding the product. See [Building a Plugin](Docs/Guides/building-a-plugin.md) and the [plugin-extensibility ADR](Docs/Architecture/ADRs/2026-07-22-plugin-extensibility-and-worker-isolation.md).

The earlier compile-time contract-profile model still functions for existing in-box profiles while migration completes — see [Adding a Custom Domain Profile](Docs/Guides/adding-a-custom-domain-profile.md), which is deprecated and kept for reference only.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and [CODING_STANDARD.md](CODING_STANDARD.md). Security issues: [SECURITY.md](SECURITY.md).

## License

Apache-2.0 — see [LICENSE](LICENSE).
