# ParityBench.NET.Composition

The single shared DI composition root. Every host (Cli, Web, Desktop) wires itself through this project so the service graph is maintained in one place.

## Owns

- `AddParityBenchWorkspaceServices(...)` — stores, run execution, comparison registries, plugin loading, retention configuration, and use cases common to every host.
- `AddParityBenchUiServices(...)` — the extra services only the Blazor hosts need (accepted-differences store, job service, view-data sources). Not used by the CLI.
- `UseWorkerProcessExecution(...)` — opt-in swap of in-process execution for out-of-process execution in `ParityBench.NET.Worker`.
- Plugin directory resolution: configured directories if any, otherwise the app's own `plugins` folder plus the workspace's.
- The `configureRequestComparisonFixtures` hook, which lets a host contribute extra endpoints/presets into the same registries before they are captured as singletons.

## Boundaries

- References every concrete layer (Application, Domain, Engine, Infrastructure, Plugins, UI, Workspaces) — that is the point of a composition root, and it is the only non-host project allowed to.
- Must not contain behaviour. Registration and configuration binding only; anything with logic belongs in the layer it configures.
- The worker process must **not** call `UseWorkerProcessExecution` — it would try to spawn a worker of its own.

## Tests

Wiring is covered indirectly by every host test project, and directly by the runtime-configuration and architecture fitness tests in `Tests/ParityBench.NET.Fitness.Tests`.
