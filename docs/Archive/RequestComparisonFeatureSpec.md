# Feature Spec: Request Folder Comparison

## Summary
Add a request-based comparison flow that uploads a folder of POST request payloads, calls two endpoints with custom headers, stores both responses, and reuses the existing analysis pipeline to compare results at scale (40k+ requests).

## Goals
- Accept a folder of request payloads and execute POST requests to two endpoints.
- Support custom headers (global and optional per-request override).
- Compare the two response sets using existing comparison + analysis pipeline.
- Handle 40k+ requests efficiently with bounded concurrency and disk-backed storage.
- Provide progress reporting and resumable status checks.

## Non-goals
- Support for GET/PUT/DELETE (POST only for initial release).
- Real-time streaming UI of each request/response pair.
- Cross-run caching of responses.

## Requirements
### Functional
- Upload a request folder (batch upload) and create a comparison job.
- Execute POST requests to two configured endpoints with headers.
- Persist responses in temp storage using a deterministic path scheme.
- Use existing comparison/analysis to generate results.
- Expose job status, progress, and final results.

### Non-functional
- Must support >= 40k requests without OOM.
- Use bounded concurrency and streaming I/O.
- Handle large response bodies with disk streaming.
- Stable throughput and responsive progress updates.

## Request Folder Format
Each file in the folder is one request. The file name (relative path) becomes the logical request ID.

### Base format (body-only)
- File contents are the POST body.
- MIME type determined by file extension (e.g., .json => application/json, .xml => application/xml, default text/plain).

### Optional per-request headers (sidecar)
For any request file `<name>.json`, a sidecar `<name>.headers.json` may exist with:
```json
{
  "headers": {
    "X-Correlation-Id": "...",
    "Authorization": "..."
  }
}
```

### Global headers
Provided via UI configuration. Per-request headers override global headers by key.

## API Contract (Draft)
### Upload requests
- `POST /api/requests/batch` (multipart/form-data)
  - files: request payloads and optional `*.headers.json` sidecars
  - response: `{ uploaded, files|batchId }`

### Start comparison job
- `POST /api/compare/requests`
  - body:
```json
{
  "requestBatchId": "abcd1234",
  "endpointA": "https://api-a/...",
  "endpointB": "https://api-b/...",
  "headersA": {"X-Env": "A"},
  "headersB": {"X-Env": "B"},
  "timeoutMs": 30000,
  "maxConcurrency": 64
}
```
  - response: `{ jobId }`

### Job status
- `GET /api/compare/requests/{jobId}/status`
  - response: `{ status, completed, total, message }`

### Job result
- `GET /api/compare/requests/{jobId}/result`
  - response: same result type as existing folder comparison

## Data Model (Draft)
```csharp
public record RequestComparisonJob(
    string JobId,
    string RequestBatchId,
    Uri EndpointA,
    Uri EndpointB,
    IReadOnlyDictionary<string, string> HeadersA,
    IReadOnlyDictionary<string, string> HeadersB,
    int TimeoutMs,
    int MaxConcurrency,
    DateTimeOffset CreatedAt,
    RequestComparisonStatus Status
);
```

## Execution Flow
1. Upload request folder -> batch ID.
2. Start comparison job.
3. For each request file:
   - Read body stream from disk.
   - Merge headers (global + sidecar).
   - POST to Endpoint A and Endpoint B.
   - Persist responses to temp storage under:
     - `.../jobId/endpointA/<relativePath>`
     - `.../jobId/endpointB/<relativePath>`
4. Invoke `DirectoryComparisonService.CompareDirectoriesAsync` on the two response folders.
5. Return comparison + analysis result.

## Tasks
1. Define request file schema + headers behavior — Estimate: S
2. Add batch upload endpoint for requests — Estimate: M
3. Implement request execution pipeline with bounded concurrency — Estimate: M
4. Persist responses and call comparison/analysis — Estimate: M
5. Add UI flow (upload + endpoint config + progress) — Estimate: M
6. Add tests (parsing, header merge, pipeline, integration) — Estimate: M

## Acceptance Criteria
- Can upload a request folder and run comparison against two endpoints.
- Custom headers are applied globally and per-request override works.
- 40k requests complete without OOM and with progress updates.
- Comparison results match existing analysis output format.
- Errors/timeouts are captured and surfaced in results.

## Rollout Plan
- Behind a feature flag: `RequestComparisonEnabled`.
- Stage rollout in dev -> staging -> production.
- Monitor latency, error rates, and temp storage growth.

## Risks and Mitigations
- **Endpoint throttling**: use configurable `maxConcurrency` and backoff.
- **Large payloads**: stream to disk, avoid loading full content into memory.
- **Long-running jobs**: ensure status endpoint + cancellation support.
- **Temp storage bloat**: scheduled cleanup by job age.
