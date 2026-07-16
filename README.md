# ParityBench.NET

ParityBench.NET is an A/B request-comparison and Expected/Actual testing platform. It fires the same requests at two endpoints (or compares two sets of result files), diffs the responses as domain objects rather than raw text, and surfaces the differences that actually matter.

Unlike text/XML diff tools, ParityBench.NET deserializes both sides into domain models before comparing, so it understands types, collections, and business-meaningful structure — not just line-by-line text drift.

## Status

This repository is mid-migration from an earlier prototype (V1, "ComparisonTool") to the current architecture (V2, "ParityBench.NET"). **V2 under [`Source/`](Source/) is the active, supported codebase.** V1 has been retired from active development and is frozen under [`archive/v1-comparisontool/`](archive/v1-comparisontool/) for reference.

See [`Docs/NorthStar.md`](Docs/NorthStar.md) for the target V2 architecture and [`Docs/Architecture/V2-Strangler/`](Docs/Architecture/V2-Strangler/) for the migration plan.

## Prerequisites

- .NET 10 SDK

## Building

```bash
dotnet restore ComparisonTool.sln
dotnet build ComparisonTool.sln -c Release
```

## Hosts

| Project | Purpose |
|---|---|
| [`Source/ParityBench.NET.Web`](Source/ParityBench.NET.Web) | Blazor Server web host |
| [`Source/ParityBench.NET.Desktop`](Source/ParityBench.NET.Desktop) | WPF + BlazorWebView desktop host (Windows) |
| [`Source/ParityBench.NET.Cli`](Source/ParityBench.NET.Cli) | Command-line host for request comparison |
| [`Source/ParityBench.NET.TestEndpoints`](Source/ParityBench.NET.TestEndpoints) | Fixture server with deterministic SOAP/XML/JSON endpoints, used for manual runs and E2E tests |
| [`Source/ParityBench.NET.ClientCustomerLookupExample`](Source/ParityBench.NET.ClientCustomerLookupExample) | Example client contract profile (SOAP + JSON with chained token auth) — see [`Docs/Features/ClientSoapJsonTokenProfileExample.md`](Docs/Features/ClientSoapJsonTokenProfileExample.md) |

Each `Source/` project has its own `README.md` describing what it owns and its boundaries.

## CLI usage

```bash
dotnet run --project Source/ParityBench.NET.Cli/ParityBench.NET.Cli.csproj -- request <request-directory> --endpoint-a <url> --endpoint-b <url>
```

Or via a registered preset (resolves request directory, endpoints, model, profile, and headers):

```bash
dotnet run --project Source/ParityBench.NET.Cli/ParityBench.NET.Cli.csproj -- request --preset client-soap-json-token
```

Full option reference:

```
request [<request-directory>] --endpoint-a <url> --endpoint-b <url> | --preset <preset-id>
  [--model Auto] [--profile <profile-id>] [--concurrency <n>] [--timeout <seconds>]
  [--content-type <type>] [--header <Name: Value>] [--header-a <Name: Value>] [--header-b <Name: Value>]
  [--report-output <directory>] [--report-assets <directory>] [--log-level <level>]
  [--log-durations] [--log-exceptions] [--persist-diagnostics] [--slow-path-threshold-ms <n>]
```

- `<request-directory>` is required unless `--preset` supplies one.
- `--endpoint-a` / `--endpoint-b` are required (absolute URLs) unless `--preset` supplies them; explicit flags override preset values.
- `--header` applies to both endpoints; `--header-a`/`--header-b` apply to one side only. All three are repeatable.

See [`Source/ParityBench.NET.Cli/README.md`](Source/ParityBench.NET.Cli/README.md) for details.

## Architecture

ParityBench.NET V2 is a staged, bounded, asynchronous pipeline: plan a run manifest, execute endpoint A/B concurrently with bounded concurrency, persist response artifacts immediately, compare persisted artifacts, append paged result metadata, then apply outcome-based retention cleanup. Hosts (Web, Desktop, CLI) are thin composition roots over shared application/engine/workspace services.

Full design: [`Docs/NorthStar.md`](Docs/NorthStar.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and [CODING_STANDARD.md](CODING_STANDARD.md).

## License

Apache-2.0 — see [LICENSE](LICENSE).
