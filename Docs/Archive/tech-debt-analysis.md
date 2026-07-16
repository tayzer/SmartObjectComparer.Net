# Technical Debt Analysis

**Project:** Open-Source Local A/B Comparison Tool  
**Date:** 2026-07-04  
**Scope:** Maintainability, code quality, evolvability, and refactor readiness for the current ComparisonTool codebase.

## Executive Summary

The current project has enough technical debt to justify targeted refactoring before adopting a broader workspace/channel/hosted-worker architecture. The evidence does not support a full rewrite. Most risk is concentrated in request-comparison orchestration, shared mutable configuration, host lifecycle management, and large-payload handling.

The recommended path is to address the highest-risk debt first, especially job configuration isolation and request execution memory behavior. After that, the team will be in a much better position to decide whether the larger high-level architecture should be implemented wholesale or only selectively.

## Ranked Debt Map

| Rank | Debt | Why It Matters | Suggested Treatment |
| ---: | --- | --- | --- |
| 1 | Shared mutable comparison configuration can leak across jobs | `IComparisonConfigurationService` and `IXmlDeserializationService` are registered as shared services, while request jobs apply per-run ignore rules and comparison options by mutating those services. Concurrent jobs with different options may produce nondeterministic results. | Fix before major architecture work. Move toward immutable per-run options or truly scoped comparison configuration. Add concurrent-job isolation tests. |
| 2 | `RequestComparisonJobService` is a god service | The service owns job state, progress, chunking, execution, comparison, materialization, metadata, analysis, and cleanup. This makes behavior harder to reason about and increases the cost of changing any one phase. | Split gradually into job store, execution runner, result assembler, artifact manager, and progress publisher. This is the best preparatory refactor for any future design. |
| 3 | Fire-and-forget job lifecycle | Web and Desktop start request-comparison jobs with `Task.Run`, and Web tracks cancellation in a static dictionary. This weakens lifecycle control, graceful shutdown, error handling, and future queueing. | Replace the Web path with a managed background queue/worker. Desktop can keep in-process execution but should share the same runner abstraction. |
| 4 | Large response/request memory pressure | Request bodies and endpoint responses are materialized as byte arrays before being written to disk. Raw-content and focused-content paths also use whole-file reads in places. | Stream responses to disk first. Add size thresholds and tests around large payloads. |
| 5 | Temp artifact handling is scattered | Temp paths and cleanup logic are spread across upload APIs, request execution, job service, host startup, and desktop staging. This makes future workspace storage harder to introduce safely. | Introduce an artifact-store abstraction, even if it initially wraps the existing temp-folder behavior. This also creates a bridge toward a workspace model. |
| 6 | Host registration duplication | Web, Desktop, and CLI each register request services and HTTP clients independently. This increases the chance that behavior diverges by host. | Create a shared `AddRequestComparisonServices(...)` registration method with host-specific extension points. This is low-risk and likely to pay off quickly. |
| 7 | Very large UI and CLI files slow evolution | Several important files are over 1,000 lines, including request UI, home UI, run details, and CLI command code. These files mix state, orchestration, validation, and rendering/reporting concerns. | Refactor opportunistically when touching those areas. Extract state/view-model logic and smaller child components or command helpers. |
| 8 | Domain-specific heuristics live in core analysis | Some enhanced structural analysis behavior contains hardcoded domain concepts and TODOs noting that the logic should be generalized. | Move these to configurable profiles/options before positioning the tool as broadly open-source and domain-neutral. |
| 9 | Tests are broad, but miss key architectural risks | There are useful unit, integration, and E2E tests, especially around request comparison, but the highest-risk architecture concerns are not fully covered. | Add characterization tests before refactoring. Prioritize concurrent jobs with different ignore rules, cancellation/shutdown, and large-payload behavior. |

## What Looks Healthy

- The solution already has a useful project split across Core, UI, Web, Desktop, CLI, Report, tests, and mock/test-data projects.
- The shared `IRequestComparisonGateway` direction is sound and helps separate UI flows from host-specific execution details.
- The request-comparison path has meaningful integration coverage, including alternate-contract behavior, large-batch paths, timing metadata, and materialization concurrency.
- The static Blazor report sidecar design is a good memory-conscious direction for report packaging.
- Current bounded concurrency via `Parallel.ForEachAsync` is not inherently debt. It is a reasonable implementation until the project needs stronger staged backpressure.

## Decision Guidance

### Fix Now

1. Make per-job comparison configuration isolated.
2. Stream request/response bodies where practical, especially endpoint responses.
3. Add tests that prove concurrent jobs cannot cross-contaminate comparison settings.
4. Centralize request-comparison service registration across Web, Desktop, and CLI.

### Fix During Refactor

1. Split `RequestComparisonJobService` into smaller phase-oriented services.
2. Introduce an artifact-store abstraction around temp storage.
3. Replace Web fire-and-forget jobs with a managed background queue/worker.
4. Extract request UI and CLI command logic into smaller units when making related changes.

### Leave Alone For Now

1. Do not replace `Parallel.ForEachAsync` solely for architectural purity.
2. Do not adopt `System.Threading.Channels` unless the request pipeline needs explicit backpressure between stages.
3. Do not force the full workspace model until storage/history/product requirements justify it.

### Only Refactor If Product Direction Requires It

1. Full user-selected workspace layout with `.abproject`, `Configs/`, and `Runs/`.
2. Historical dashboard that scans only summaries and lazy-loads per-request detail files.
3. CLI-first or service-first engine wrapper if the tool needs a stronger automation surface.

## Bottom Line

The current codebase is workable, but a few debt items carry real correctness and scalability risk. Addressing the top four items will make the system safer immediately and will reduce the cost of moving toward the high-level design later.
