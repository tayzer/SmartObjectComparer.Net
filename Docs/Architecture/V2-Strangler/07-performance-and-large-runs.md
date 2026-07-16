# Slice 6: Performance And Large Run Behavior

## Goal

Improve V2 execution internals without changing user-facing results.

Slice 6 keeps the existing V2 comparison semantics, but removes avoidable transport/body buffering from the normal and alternate-contract execution paths. It also records cheap per-run execution metrics and tightens workspace detail persistence for larger runs.

## Implemented Behavior

- Normal endpoint responses continue to stream directly into `IRunArtifactStore`.
- Alternate-contract endpoint responses are persisted as raw artifacts first; successful pairs are then normalized by opening those artifacts as streams.
- Alternate request preparation and response normalization use `ContractPayload`, which exposes `OpenReadAsync` instead of carrying `byte[]` bodies.
- Infrastructure serializers now write generated JSON/XML payloads into caller-provided streams.
- Built-in alternate-contract profiles create file-backed temporary payloads and delete them when disposed.
- `RunResultSummary` can carry optional `RunExecutionMetrics` with total duration, stage durations, request count, max concurrency, and response bytes written.
- `FileSystemRunDetailStore` writes the detail index as a streamed JSON array instead of materializing a second DTO list.

## Boundaries

- Model deserialization and object comparison can still materialize typed object graphs; this slice focuses on removing avoidable raw body byte-array buffering.
- JSON/XML masking still uses the existing masking implementation and may materialize masked bodies when mask rules are configured.
- The detail index remains a single loadable JSON file; paged or indexed detail loading belongs to a later results/history slice.
- No V1 projects, hosts, CLI commands, reports, or user-facing flows switch to V2 in this slice.

## Verification

Run the solution build from the repo root:

```powershell
dotnet build ComparisonTool.sln -m:1 -v:minimal
```

Run V2 MSTest/MTP projects from the physical `Tests` directory so `Tests/global.json` opts into the Microsoft Testing Platform runner:

```powershell
dotnet test --project ParityBench.NET.Domain.Tests\ParityBench.NET.Domain.Tests.csproj --no-build -v:minimal
dotnet test --project ParityBench.NET.Application.Tests\ParityBench.NET.Application.Tests.csproj --no-build -v:minimal
dotnet test --project ParityBench.NET.Engine.Tests\ParityBench.NET.Engine.Tests.csproj --no-build -v:minimal
dotnet test --project ParityBench.NET.Workspaces.Tests\ParityBench.NET.Workspaces.Tests.csproj --no-build -v:minimal
dotnet test --project ParityBench.NET.Infrastructure.Tests\ParityBench.NET.Infrastructure.Tests.csproj --no-build -v:minimal
dotnet test --project ParityBench.NET.UI.Tests\ParityBench.NET.UI.Tests.csproj --no-build -v:minimal
```

## Completion Criteria

- Large no-mask responses are passed through to artifact persistence as streams.
- Alternate-contract raw responses are artifact-backed before normalization.
- Generated alternate request and normalized response payloads are stream-openable and disposed after use.
- Completed summaries include cheap execution metrics.
- Large detail indexes remain loadable and are written without an additional DTO-list allocation.
- Existing V2 behavior tests remain green.