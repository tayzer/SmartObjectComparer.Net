# Jira Backlog: Request Folder Comparison

## Epic: Request Folder Comparison (POST, 40k+ scale)
**Goal**: Enable uploading a request folder, calling two endpoints with custom headers, and reusing existing analysis to compare responses at scale.

---

### RC-101 — Define request file schema and header precedence
**Type**: Story | **Estimate**: 2 pts

**Description**
Finalize request file format, content-type mapping, header precedence (global vs per-request), and sidecar header file structure.

**Acceptance Criteria**
- Schema documented for request files and optional `*.headers.json`.
- Header precedence rules documented.
- Content-type inference rules documented.

---

### RC-102 — Add request batch upload endpoint
**Type**: Story | **Estimate**: 5 pts

**Description**
Create `POST /api/requests/batch` mirroring existing batch upload behavior, supporting large folders and returning batch ID for large sets.

**Acceptance Criteria**
- Uploads 10k files in batches successfully.
- Returns `files` for small sets, `batchId` for large sets.
- Validates file types and size limits.

---

### RC-103 — Implement job creation endpoint
**Type**: Story | **Estimate**: 5 pts

**Description**
Add `POST /api/compare/requests` to create a request comparison job with endpoint config and headers.

**Acceptance Criteria**
- Returns `jobId`.
- Validates endpoint URLs and required payload fields.
- Persists job metadata.

---

### RC-104 — Implement job status endpoint
**Type**: Story | **Estimate**: 3 pts

**Description**
Add `GET /api/compare/requests/{jobId}/status` with progress reporting.

**Acceptance Criteria**
- Returns status, completed, total, message.
- Handles unknown job IDs gracefully.

---

### RC-105 — Implement request execution pipeline
**Type**: Story | **Estimate**: 8 pts

**Description**
Create bounded-concurrency runner that reads request files from disk, merges headers, and calls both endpoints.

**Acceptance Criteria**
- Supports configurable `maxConcurrency`.
- Applies per-request and global headers with deterministic precedence.
- Honors per-request timeout.

---

### RC-106 — Persist responses to disk
**Type**: Story | **Estimate**: 5 pts

**Description**
Store responses in temp directories for endpoint A/B using stable mappings based on request file paths.

**Acceptance Criteria**
- File mapping is deterministic and collision-free.
- Response data is written via streaming (no full-buffer loads).

---

### RC-107 — Compare responses via existing pipeline
**Type**: Story | **Estimate**: 5 pts

**Description**
Invoke existing comparison and analysis pipeline over the two response folders.

**Acceptance Criteria**
- Uses `DirectoryComparisonService.CompareDirectoriesAsync`.
- Semantic analysis appears in metadata as per current folder comparison.

---

### RC-108 — UI: Request folder upload panel
**Type**: Story | **Estimate**: 5 pts

**Description**
Add request folder upload UI, reuse batch upload JS, and show progress.

**Acceptance Criteria**
- Request folder upload works for 5k+ files.
- Progress and status shown during upload.

---

### RC-109 — UI: Endpoint config + custom headers
**Type**: Story | **Estimate**: 5 pts

**Description**
Add input controls for endpoint A/B URLs and headers (global + optional per-request).

**Acceptance Criteria**
- Supports adding/removing headers per endpoint.
- Validates URL fields.

---

### RC-110 — UI: Run comparison and view results
**Type**: Story | **Estimate**: 5 pts

**Description**
Wire the request-based job flow to run and display results using existing components.

**Acceptance Criteria**
- User can run request comparison and see results in current UI components.
- Errors surfaced with actionable messages.

---

### RC-111 — Add unit tests for header merging and request parsing
**Type**: Story | **Estimate**: 3 pts

**Acceptance Criteria**
- Tests validate header precedence and parsing.
- Tests validate content-type inference.

---

### RC-112 — Add integration tests with mocked endpoints
**Type**: Story | **Estimate**: 8 pts

**Acceptance Criteria**
- Simulated endpoints receive correct headers/body.
- Comparison result generated from mocked responses.

---

### RC-113 — Load test (10k requests)
**Type**: Task | **Estimate**: 5 pts

**Acceptance Criteria**
- 10k request run completes without OOM.
- Throughput and error rates reported.

---

### RC-114 — Feature flag + cleanup policy
**Type**: Story | **Estimate**: 3 pts

**Acceptance Criteria**
- Feature gated by `RequestComparisonEnabled`.
- Temp storage cleanup scheduled by age.

---

## Definition of Done (for this epic)
- Feature flag enabled in dev only.
- 40k+ requests can be processed with bounded concurrency.
- Results match existing analysis output format.
- Tests and load results recorded.
