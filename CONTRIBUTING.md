# Contributing

Thanks for your interest in contributing.

## Prerequisites

- .NET 10 SDK (see `Directory.Build.props` / `global.json` for the exact pinned version)
- Windows (the Desktop host uses WPF/BlazorWebView; Web, CLI, and tests are cross-platform)

## Getting the code building

```bash
dotnet restore ComparisonTool.sln
dotnet build ComparisonTool.sln -c Release
```

This solution contains the active V2 codebase only (`Source/`, `Tests/`). The
original V1 codebase is frozen under [`archive/v1-comparisontool/`](archive/v1-comparisontool/)
and is not part of this solution — see its own
[README](archive/v1-comparisontool/README.md) if you need to build it.

## Running tests

```bash
dotnet test ComparisonTool.sln
```

Each `Source/ParityBench.NET.*` project has a matching test project under
`Tests/ParityBench.NET.*.Tests`. Test conventions (framework, naming, structure)
are documented in [CODING_STANDARD.md](CODING_STANDARD.md).

## Project layout

- `Source/` — active V2 code (application services, engine, hosts: Web, Desktop, CLI, TestEndpoints)
- `Tests/` — V2 tests, one project per `Source/` project
- `Examples/` — manual-run fixtures used by CLI presets and E2E tests
- `Docs/` — architecture, feature, and planning documentation
- `archive/v1-comparisontool/` — frozen V1 predecessor, not built by CI

Each project under `Source/` has its own `README.md` describing what it owns
and its boundaries — start there before making cross-project changes.

## Making a change

1. Fork/branch from `master`.
2. Keep PRs focused; describe the change and the reasoning behind it.
3. Run `dotnet build` and `dotnet test` locally before opening a PR.
4. Follow [CODING_STANDARD.md](CODING_STANDARD.md) for style, naming, and testing conventions.
5. CI (`.github/workflows/ci.yml`) runs build, CodeQL, and tests on every PR against `master`.

## Reporting bugs / requesting features

Open a GitHub issue. For security vulnerabilities, see [SECURITY.md](SECURITY.md)
instead of a public issue.
