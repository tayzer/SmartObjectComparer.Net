# Request Comparison Delivery Plan

## One-line Summary
Deliver a request-based comparison pipeline that executes POST requests to two endpoints with custom headers and reuses existing analysis, optimized for 40k+ requests.

## Milestones and Tasks

### M1 — Design & Contracts (Estimate: 2–3 days)
1. Confirm request file format and header override strategy.
2. Finalize endpoint contracts and response shapes.
3. Update documentation and UX wireframes.

**Acceptance Criteria**
- Feature spec approved.
- API contract agreed.

### M2 — Backend Upload + Job API (Estimate: 4–6 days)
1. Add `POST /api/requests/batch` (multipart) modeled after existing batch upload.
2. Add job endpoints: create, status, result.
3. Add server-side validation (content type, file size, path sanitization).

**Acceptance Criteria**
- Request batch upload works for 10k files.
- Job can be created and status polled.

### M3 — Request Execution Pipeline (Estimate: 5–7 days)
1. Implement bounded concurrency runner.
2. Add per-request timeout and retry strategy (optional).
3. Merge headers (global + sidecar) with deterministic precedence.
4. Stream request bodies and response bodies to disk.

**Acceptance Criteria**
- 40k requests run without OOM.
- Responses are persisted in endpoint A/B folders with stable mapping.

### M4 — Comparison + Analysis Integration (Estimate: 3–5 days)
1. Reuse `DirectoryComparisonService.CompareDirectoriesAsync`.
2. Store semantic analysis in metadata, as done for folder uploads.

**Acceptance Criteria**
- Results appear in the same UI components as current folder comparison.

### M5 — UI Flow (Estimate: 4–6 days)
1. Add request folder upload panel (uses existing batch upload JS).
2. Add endpoint configuration inputs + headers editor.
3. Add progress and cancellation support.

**Acceptance Criteria**
- User can upload requests, configure endpoints, run, and see results.

### M6 — Tests & Hardening (Estimate: 4–6 days)
1. Unit tests: header merge, request parsing, status progression.
2. Integration tests: mocked endpoints + comparison results.
3. Load test with 10k synthetic requests.

**Acceptance Criteria**
- Test suite green.
- Load test meets throughput target.

## Estimates Summary
- Total: 22–33 dev-days (team size dependent)

## Rollout Strategy
- Feature flag: `RequestComparisonEnabled`.
- Deploy to staging, run load tests, then production.

## Dependencies
- Existing batch upload infrastructure.
- Temp storage availability for large response sets.
- Endpoint SLAs and rate limits.

## Risks
- **Rate limiting**: handle backoff and concurrency tuning.
- **Large responses**: disk I/O bottlenecks; requires streaming.
- **Job duration**: may require background processing and cancellation.
