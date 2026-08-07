# ParityBench.NET.Fitness.Tests

Architecture and system-level fitness tests. These are the executable form of the rules the project READMEs state in prose — if a boundary is broken or a cross-cutting guarantee regresses, it fails here rather than in review.

## Covers

- **`ArchitectureFitnessTests`** — project reference boundaries. Domain stays framework-free, UI reaches no further than Application and Domain, only composition roots reference concrete layers, nothing references V1.
- **`RuntimeConfigurationFitnessTests`** — each host's configuration binds and validates at startup, including retention options and worker opt-in.
- **`ObservabilityFitnessTests`** — logging, duration, and diagnostics behaviour stays wired through the configured observability options.
- **`RunProgressFitnessTests`** — progress reporting reaches the caller monotonically and completes, including cancellation.
- **`ResultLifecycleFitnessTests`** — a run's stored artifacts, paged details, and summary stay consistent from creation through retention cleanup.
- **`LargeRunFitnessTests`** — bounded-memory behaviour on large runs; the guarantee that run cost does not scale with run size.
- **`ClientScenarioFitnessTests`** — the end-to-end client scenario, built directly rather than through host DI.
- **`ManualRunFixtureGeneratorTests`** — the generated fixture distribution stays stable.

## Boundaries

- May reference any project, including hosts, because its job is to assert across boundaries.
- Assertions should target rules, not implementations. A test here failing should mean a rule was broken, not that a method was renamed.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.Fitness.Tests\ParityBench.NET.Fitness.Tests.csproj -v:minimal
```
