# Slice 2: Basic Request A/B Flow

## Goal

Rebuild the simplest useful V2 request-comparison path without switching any host to V2.

This slice proves the vertical path from staged request files to endpoint A/B execution, response artifact persistence, pair classification, detail metadata, and a cheap run summary.

## Implemented Shape

Slice 2 adds executable V2 behavior across Domain, Application, Engine, Workspaces, and Infrastructure.

The Domain layer owns request/result vocabulary:

- `RequestItem`: normalized relative request identity, content type, content length, and optional headers.
- `RequestBatchManifest`: staged request-batch metadata keyed by `RequestBatchReference`.
- `EndpointSlot`: endpoint `A` or `B`.
- `ResponseArtifactMetadata`: response artifact reference, status code, content type, length, and SHA-256.
- `RequestPairOutcome`: `Equal`, `Different`, `StatusCodeMismatch`, `BothNonSuccess`, `ExecutionFailed`.
- `RequestPairResult`: pair-level metadata plus summary counting.

The Application layer owns ports:

- `IRequestBatchStore`: stage/load request batches and open staged request body streams.
- `IRunArtifactStore`: persist response streams and return artifact metadata.
- `IRunDetailStore`: persist/load pair detail indexes without loading raw bodies.
- `IEndpointRequestSender`: send one request body to one endpoint and return a disposable streaming response.

The Engine layer owns the basic execution pipeline:

- `BasicComparisonRunExecutor` loads the manifest, reports lifecycle progress, executes request pairs with bounded request-pair concurrency, persists response artifacts, classifies pairs, saves detail metadata, and returns `RunResultSummary`.

The Workspaces layer owns file-system persistence:

- `FileSystemRequestBatchStore` stages `.json`, `.xml`, and `.txt` files and writes a manifest.
- `FileSystemRunStore` stores immutable run snapshots through JSON DTOs.
- `FileSystemRunArtifactStore` streams response bodies into workspace artifacts while computing SHA-256.
- `FileSystemRunDetailStore` stores pair detail indexes separately from raw response bodies.

The Infrastructure layer owns HTTP execution:

- `HttpClientEndpointRequestSender` sends POST requests with `HttpCompletionOption.ResponseHeadersRead`, applies headers, and returns a readable response stream.

## Lifecycle

A Slice 2 run follows the Slice 1 lifecycle:

| Phase | Responsibility |
| --- | --- |
| `Created` | Application creates and stores the run. |
| `Executing` | Application starts the run and Engine executes endpoint pairs. |
| `Parsing` | Engine loads the staged request manifest. |
| `Executing` | Engine sends endpoint A/B requests and persists response artifacts. |
| `Comparing` | Engine classifies status/hash outcomes. |
| `Finalizing` | Engine saves pair detail metadata and returns the summary. |
| `Completed` | Application stores the final summary. |

## Persistence Layout

The current workspace layout is intentionally simple and logical:

```text
request-batches/{batchId}/manifest.json
request-batches/{batchId}/requests/{relativeRequestPath}
runs/{runId}/run.json
runs/{runId}/artifacts/{A|B}/{relativeRequestPath}
runs/{runId}/details/index.json
```

Artifact and detail references remain logical workspace identifiers, not absolute paths.

## Classification Rules

Slice 2 uses raw response-body hash equality only:

- Both endpoints return 2xx and length/hash match: `Equal`.
- Both endpoints return 2xx and length/hash differ: `Different`.
- One endpoint returns 2xx and the other does not: `StatusCodeMismatch`.
- Both endpoints return non-2xx: `BothNonSuccess`.
- Either endpoint sender fails: `ExecutionFailed`.

Model-aware semantic comparison, raw non-success diffs, masking, alternate contracts, and advanced options remain later slices.

## Completion Criteria

- V2 can stage a small request directory and execute it through Application plus Engine without Web, Desktop, or CLI integration.
- Endpoint A and endpoint B are both called for each request.
- Response bodies are persisted as artifacts through Workspaces.
- Result summaries can be loaded without reading raw response bodies.
- Pair details can be loaded from a detail index without loading raw response bodies.
- Tests cover Domain classification, Workspaces persistence, Engine execution, Infrastructure HTTP streaming, and a non-hosted vertical integration flow.

## Non-Goals

- Do not migrate all request options.
- Do not migrate alternate contracts.
- Do not implement model-aware semantic comparison.
- Do not implement final report output.
- Do not switch Web, Desktop, or CLI to V2 by default.