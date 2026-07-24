# Plugin Extensibility and Worker-Process Isolation

- Date: 2026-07-22
- Status: Approved (partially implemented — see Verification approach)

## Context

ParityBench.NET was extensible only at compile time. A client "contract profile" (`Source/ParityBench.NET.ClientCustomerLookupExample`) was a project in the solution, project-referenced by every host, wired through `AddClientCustomerLookupExample(...)`, and enforced as a host reference by `ProjectBoundaryTests`. Adding a client meant editing and rebuilding the product.

Three limits followed:

- `IContractProfile` was a fixed two-method shape (`PrepareRequestAsync`, `NormalizeResponseAsync`). Anything beyond a plain field mapping — auth chains, retries, request enrichment, result post-processing — had to be smuggled into a `requestPreparation` lambda.
- Runs were selected via code-registered presets (`IRequestComparisonPresetRegistry`), so a saved run configuration was not a first-class, editable artifact.
- Client code ran in the host process, so a client dependency conflict or crash could take the desktop app down.

The goal: a client installs the desktop app once and loads a versioned plugin **package**. The plugin provides implementation and type knowledge; a saved JSON **run profile** selects and configures it; the app provides installation, configuration, execution and reporting. Plugin code runs in a separate **worker process**.

## Decision

Introduce a plugin architecture with four pillars, and (decided in planning) **replace** the contract-profile model rather than wrap it.

1. **`ParityBench.PluginSdk`** — the only assembly a client plugin compiles against. It holds the plugin contracts (`IParityBenchPlugin`, `IPluginBuilder`, `IComparisonDefinition<T>`, `IComparisonMiddleware`, `PluginConfigurationSchema`, `PluginManifest`, profile/environment templates) plus the neutral value types shared with plugins (payload formats, comparison rules, request/endpoint models). It takes no product dependency. The neutral types were physically moved out of `Domain` into the SDK with their namespaces unchanged, so `Domain` project-references the SDK and no other consumer changed.

2. **A phased middleware pipeline** — `IComparisonMiddleware` is a Chain of Responsibility, but **phase-scoped** (`Input | Request | Transport | Response | Mapping | Comparison | ResultProcessing`). The builder buckets steps by phase and concatenates buckets in fixed phase order; a step orders only *within* its phase, so an invalid pipeline (mapping before transport) cannot be expressed. `Input…Mapping` run per endpoint slot (A and B concurrently); `Comparison…ResultProcessing` run once per pair. The product ships built-in middleware for the standard path; plugins add steps. `ComparisonRunExecutor`'s two-pool channel design is preserved — the endpoint chain is split across the pools (`Input…Response` in the network pool while the response stream is open, `Mapping` in the compare pool off the persisted artifact), so materialized comparison objects never queue between pools and large runs stay memory-bounded.

3. **Run profiles + secrets** — a `RunProfile` is a JSON file (`<workspace>/config/profiles/*.json`) referencing stable logical ids (plugin id, comparison id, step ids, `secret://` references) — never assembly-qualified type names. `ISecretStore` resolves `secret://scope/name` through a chain: environment variable → DPAPI-encrypted workspace file → in-memory. Secret values are resolved only at run start and never written back to the profile, run snapshot, or report.

4. **Worker-process isolation** — client plugin code executes only in `ParityBench.NET.Worker`, a child process launched per run. Host↔worker communicate over a per-run named pipe with newline-delimited JSON frames; the worker writes artifacts and paged details to the same workspace, so only the run summary crosses the pipe (large-run memory stays bounded). Plugins load into a collectible `AssemblyLoadContext` per package resolving against the package's own `.deps.json`; the SDK is shared from the default context so contract types unify. A crash, hang, or dependency conflict fails the run with the worker's stderr captured — the host is unaffected. Worker execution is opt-in per host via `Worker:Enabled` config.

## Rationale

- Moving the neutral value types into the SDK with unchanged namespaces gave a zero-churn split — the plugin contract surface exists without a repo-wide rename.
- Phase-scoping the pipeline is what makes plugin ordering safe: clients get real flexibility (any number of steps, before/after `next`, short-circuit) without the ability to express an ordering the engine cannot honor.
- Keeping payloads on disk and passing only the summary across the pipe preserves the property that made large runs viable in the first place; isolation did not cost the memory model.
- Sharing the SDK from the default load context is the one non-negotiable of the isolation design — without it, a plugin's `IComparisonMiddleware` would not be assignable to the interface the pipeline expects.
- The secret chain resolves order-as-policy: an environment variable overrides the persisted store, so CI and tests run any profile without touching an operator's machine, and the DPAPI file keeps installed-desktop secrets at rest under the current user.

## Trade-offs

- Out-of-process execution adds a process launch, a pipe, and a serialization boundary per run — negligible for real runs, but it is why worker execution is opt-in rather than always-on.
- The migration is staged: the SDK, pipeline, plugin loading, worker, profiles, secrets, and the reference plugin are in place and green, but the legacy `IContractProfile` / preset / response-model path still exists and still backs the "no plugin selected" raw structured-comparison used by fixtures and the current UI. The two paths coexist (the executor branches on `RunOptions.PluginComparison`) until the legacy path is retired in its own sequenced change.
- Worker mode currently targets the plugin path only — the worker composition does not register the legacy in-box example, so legacy `--preset` runs should stay in-process until the legacy path is removed.

## Alternatives considered

- **Adapter, then migrate** — wrap `IContractProfile` as built-in middleware and retire it later. Rejected in planning in favor of a clean replacement target, though the actual deletion is being sequenced to avoid destabilizing the still-green tree.
- **Keep both permanently** — offer the pipeline only as an advanced opt-in seam over `IContractProfile`. Rejected: it would freeze the two-method shape as the common path and defeat the point.
- **In-process plugin loading (no worker)** — simpler, but a client dependency conflict or crash would take the host down, which is the exact failure the desktop app must avoid.
- **Presets alongside profiles** — keep the code-registered preset registry and add profiles as a UI feature. Rejected: a saved run configuration should be a first-class, hand-editable, client-checkinable artifact referencing logical ids.

## Impacted projects or files

New: `Source/ParityBench.PluginSdk`, `Source/ParityBench.NET.Plugins`, `Source/ParityBench.NET.Worker`, `Source/ParityBench.ClientCustomerLookupPlugin`, and their test projects.

Changed: `Source/ParityBench.NET.Engine` (pipeline, built-in middleware, executor rewire), `Source/ParityBench.NET.Application` (`Plugins/`, `Profiles/`, `Secrets/`, `Runs/Worker/`, workflow request), `Source/ParityBench.NET.Infrastructure` (DPAPI store, worker launcher), `Source/ParityBench.NET.Workspaces` (run-profile store, `PluginComparisonSelectionDto`), `Source/ParityBench.NET.Domain/Runs/RunOptions.cs`, `Source/ParityBench.NET.Composition/WorkspaceServiceCollectionExtensions.cs`, and the Cli/Web/Desktop hosts (`--run-profile`, `Worker:Enabled` opt-in).

## Verification approach

- Full solution build and `dotnet test` (374 tests green as of this ADR).
- Isolation proof (`ParityBench.NET.Plugins.Tests`): two packages load into separate collectible contexts with distinct types while SDK interfaces unify.
- Crash containment (`ParityBench.NET.Worker.Tests`): the real worker launched end-to-end; a run selecting a missing plugin is reported failed with the host still alive.
- Reference-plugin end-to-end (`ParityBench.ClientCustomerLookupPlugin.Tests`, `ParityBench.NET.Cli.Tests`): the real package is discovered from its manifest, loaded from disk, drives a token exchange + SOAP→JSON mapping + canonical comparison, once through the executor directly and once through the CLI `--run-profile` path.
- **Outstanding**: retire the legacy `IContractProfile` / preset / response-model path (migrating the raw structured-comparison model registry first), add the plugin/profile UI, and the built-in-sample plugin conversion. See the implementation plan for the sequenced remainder.

## Supersedes or superseded by

- Supersedes the compile-time contract-profile extensibility model documented in `Docs/Guides/adding-a-custom-domain-profile.md` (now rewritten as `Docs/Guides/building-a-plugin.md`).
- Not superseded by another ADR.
