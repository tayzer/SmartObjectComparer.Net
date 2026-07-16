# ComparisonTool V1 (archived)

This directory holds the original ComparisonTool codebase, frozen as of the V2 migration. It is kept for reference and history only — it is **not** built by CI and is not part of the main `ComparisonTool.sln`.

Active development happens under [`Source/`](../../Source) (ParityBench.NET, V2). See:

- [`Docs/NorthStar.md`](../../Docs/NorthStar.md) — target V2 architecture
- [`Docs/Architecture/V2-Strangler/10-v1-deprecation-and-archive.md`](../../Docs/Architecture/V2-Strangler/10-v1-deprecation-and-archive.md) — the migration slice that governs this archive

## Building V1 (if you need to)

The projects here still build standalone:

```bash
dotnet build archive/v1-comparisontool/ComparisonTool.Cli/ComparisonTool.Cli.csproj -c Release
```

They are excluded from `ComparisonTool.sln` and from CI. There is no supported release pipeline for V1.
