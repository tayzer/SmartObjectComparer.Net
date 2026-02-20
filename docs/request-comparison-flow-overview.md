# Request Comparison Flow & Performance Overview

## End-to-End Flow

1. **Input collection (Web or CLI):**
   - Web UI gathers files, endpoint URLs, headers, model/config, timeout/concurrency and submits API calls.
   - CLI gathers equivalent flags/options and stages files into the same temp batch structure used by the core services.

2. **Batch upload and staging:**
   - `POST /api/requests/batch` receives multipart files.
   - Files are written to temp storage (`ComparisonToolRequests/<batchId>`) using parallel async copy.
   - Optional cache-key lookup allows reusing previously uploaded batches.

3. **Job creation and kickoff:**
   - `POST /api/requests/compare` validates request, creates a job record, publishes initial progress, and starts background execution.
   - Job status/result endpoints support polling and result retrieval.
   - Cancel endpoint signals cancellation via job-scoped token.

4. **Job execution phases (core pipeline):**
   - **Parsing:** parse request files + optional sidecar headers (`.headers.json`).
   - **Executing:** for each request file, call endpoint A and endpoint B.
   - **Classifying:** classify each pair as:
     - `BothSuccess` (both 2xx)
     - `StatusCodeMismatch`
     - `BothNonSuccess`
     - `OneOrBothFailed`
   - **Comparing:**
     - `BothSuccess` pairs use domain-model folder comparison pipeline.
     - Non-success pairs use raw-text comparison with status/body diffs.
     - Failed pairs become explicit error results.

5. **Result finalization:**
   - Merge/sort file-pair results.
   - Attach execution metadata and outcome summary.
   - Store final `MultiFolderComparisonResult` by job ID.
   - Publish `Completed`/`Failed`/`Cancelled` progress update.

6. **Progress delivery:**
   - SignalR pushes phase/percent updates to subscribed clients.
   - UI keeps lightweight fallback polling in case real-time updates are interrupted.

## Implemented Performance Measures

- **Bounded concurrency:**
  - Upload path uses capped parallelism.
  - Execution path uses `Parallel.ForEachAsync` with configurable per-job `MaxConcurrency`.

- **Async + sequential file I/O:**
  - Temp request/response files are read/written asynchronously with `SequentialScan` hints.
  - Responses are persisted to disk per request pair for large-scale runs.

- **Buffer reuse on upload path:**
  - Shared `ArrayPool<byte>` buffer reuse minimizes per-file allocation churn during multipart upload copy.

- **Request/result flow designed for scale:**
  - Deterministic temp file paths and lightweight in-memory job/result metadata.
  - Large file lists can be persisted as `_filelist.json` to avoid over-large response payloads.

- **Progress throttling + low-chatter updates:**
  - High-frequency execution progress is throttled server-side (250ms interval).
  - Fallback polling interval is reduced frequency (2s) while SignalR handles primary updates.

- **Core comparison-engine optimizations reused by request flow:**
  - Batch processing and adaptive parallelism in directory comparison.
  - Shared comparison result caching with expiration/eviction/memory-limit controls.
  - XML serializer caching (thread-local + mode-aware) and deserialization safeguards.
  - Startup temp-folder cleanup and configured thread-pool minimums.

## Notable Trade-off

- During request execution, each request body and each endpoint response is currently materialized as `byte[]` before final disk write for that request pair.
- This is robust and simple, but can create per-request memory spikes for very large payloads/responses.
