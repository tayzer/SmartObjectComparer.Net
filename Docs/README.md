# Documentation

Everything current lives in `Guides/` and `Architecture/`. Anything under `Archive/` is frozen history — kept for provenance, never cited as current behaviour.

## Start here

| If you want to… | Read |
|---|---|
| Run your first comparison | [Getting Started](Guides/getting-started.md) |
| Understand how the system fits together | [High-Level Design](Architecture/high-level-design.md) — includes the system flow diagram |
| Stop noisy fields failing your runs | [Comparison Rules](Guides/comparison-rules.md) |
| Read and share results | [Reports and Results](Guides/reports-and-results.md) |
| Know where data lives and what gets deleted | [Retention and Workspace](Guides/retention-and-workspace.md) |
| Compare against a version that no longer runs | [Baseline vs Live](Guides/baseline-vs-live.md) |
| Compare two versions of your own API | [Building a Plugin](Guides/building-a-plugin.md) |
| Compare two endpoints with different contracts | [Building a Plugin for Different Contracts](Guides/building-a-different-contract-plugin.md) |
| Contribute code | [CONTRIBUTING.md](../CONTRIBUTING.md), [CODING_STANDARD.md](../CODING_STANDARD.md) |

## Guides

Task-oriented, one per feature area.

| Guide | Covers |
|---|---|
| [Getting Started](Guides/getting-started.md) | Build, fixture endpoints, a first run in the CLI and in the app, reading the outcome |
| [Comparison Rules](Guides/comparison-rules.md) | Comparison flags, ignore rules, smart ignores, mask rules, accepted-difference profiles, the Rules Studios |
| [Reports and Results](Guides/reports-and-results.md) | Run summaries, paged pair details, difference analysis, run history, the static Blazor report |
| [Retention and Workspace](Guides/retention-and-workspace.md) | Workspace layout, retention modes, non-success diagnostics overrides, configuration keys |
| [Baseline vs Live](Guides/baseline-vs-live.md) | Capturing a baseline package and replaying it against a new version |
| [Building a Plugin](Guides/building-a-plugin.md) | Two deployments of the same API: SDK, manifest, middleware, run profiles, secrets, worker isolation |
| [Building a Plugin for Different Contracts](Guides/building-a-different-contract-plugin.md) | When Endpoint A and Endpoint B don't share one contract and one side needs translating onto the other |
| [Adding a Custom Domain Profile](Guides/adding-a-custom-domain-profile.md) | **Deprecated** — the superseded compile-time contract-profile model |

## Architecture

| Document | Covers |
|---|---|
| [High-Level Design](Architecture/high-level-design.md) | System flow diagram, component map, extensibility model, comparison modes, memory model |
| [ADR process](Architecture/ADRs/README.md) | When an ADR is required and what it must contain |

### Decision records

| Date | Decision |
|---|---|
| [2026-07-27](Architecture/ADRs/2026-07-27-baseline-vs-live-comparison.md) | Baseline vs live comparison |
| [2026-07-22](Architecture/ADRs/2026-07-22-plugin-extensibility-and-worker-isolation.md) | Plugin extensibility and worker isolation |
| [2026-04-16](Architecture/ADRs/2026-04-16-static-report-sidecar-packaging.md) | Static report sidecar packaging |

## Elsewhere in the repository

- Per-project `README.md` files under `Source/` and `Tests/` — what each project owns and what it must not reference.
- [`Examples/ParityBench.NET.ManualRuns/README.md`](../Examples/ParityBench.NET.ManualRuns/README.md) — manual-run scenarios with suggested rules and expected outcomes.
- [`memories/repo/`](../memories/repo) — short operational summaries of the docs above, for agent context. Summaries, not sources.

## Archive

[`Archive/`](Archive) holds V1→V2 migration plans, superseded feature specs, and completed strangler-pattern work. Useful for "why is it like this", misleading as documentation. Nothing there is maintained.
