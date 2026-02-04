# Request Comparison Parity Plan (Home Features)

## One-line Recommendation
Align Request Comparison with Home by adding model/config/analysis inputs and applying them per job, then render the same results and analysis panels.

## Rationale
- The request comparison UI/API currently ignores domain model selection, ignore rules, and analysis flags available on Home.
- Backend jobs run with default config and no per-job ignore rules, which causes parity gaps.
- Completed request comparisons are not rendered with the same results/analysis UI used on Home.

## Scope
**In scope**
- Domain model selection and serialization parity.
- Ignore rules and smart ignore rules parity.
- Semantic and enhanced structural analysis parity.
- Folder-of-requests upload and batching parity.
- Result rendering parity with Home tab.

**Out of scope**
- New comparison algorithms.
- Changes to existing domain models.
- New external storage backends.

## Actionable Plan

### P0 — Align UI Inputs (Estimate: M)
**Tasks**
1. Add model selector to Request Comparison tab (reuse the model list from Home).
2. Add `ComparisonConfigurationPanel` to Request Comparison tab.
3. Add toggles for semantic and enhanced structural analysis.

**Acceptance Criteria**
- Request Comparison tab exposes the same model/ignore/config options as Home.
- Selected options are preserved while a job is running.

### P1 — Extend Request Comparison API Contract (Estimate: S)
**Tasks**
1. Extend `CreateRequestComparisonJobRequest` to carry:
	 - `ModelName`
	 - `IgnoreRules`
	 - `SmartIgnoreRules`
	 - `IgnoreCollectionOrder`, `IgnoreStringCase`, `IgnoreXmlNamespaces`
	 - `EnableSemanticAnalysis`, `EnableEnhancedStructuralAnalysis`
2. Validate the new fields in the API.

**Acceptance Criteria**
- API accepts and validates all configuration fields.
- UI successfully submits the extended payload.

### P2 — Per-Job Configuration Application (Estimate: M–L)
**Tasks**
1. Apply ignore rules and smart ignores per job (avoid shared singleton state).
2. Ensure XML ignore properties are applied based on selected model.
3. Apply analysis flags when calling `CompareDirectoriesAsync`.

**Acceptance Criteria**
- Ignore rules affect request comparison results.
- Smart ignore rules filter differences in request comparisons.
- Semantic and enhanced structural analysis are computed when enabled.

### P3 — Result Rendering Parity (Estimate: S)
**Tasks**
1. Wire Request Comparison completion to the same result views used on Home.
2. Render semantic groups and enhanced structural analysis panels.

**Acceptance Criteria**
- Request Comparison results show summary, differences, and analysis panels.
- Panels appear only when their corresponding toggles are enabled.

### P4 — Folder Upload for Request Batches (Estimate: M)
**Tasks**
1. Add folder upload support for request files (multipart upload, preserve paths).
2. Add file size/type validation parity with Home.
3. Support large batch uploads and caching.

**Acceptance Criteria**
- Users can select a folder of requests and upload as a batch.
- Large batches complete with stable file ordering.

### P5 — Tests & Hardening (Estimate: M)
**Tasks**
1. Unit tests for config propagation and job execution path.
2. Integration tests for request comparison pipeline with mock endpoints.
3. Regression test for per-job configuration isolation.

**Acceptance Criteria**
- Tests prove parity with Home behavior for config and analysis.
- Concurrent jobs don’t override each other’s config.

## Proposed API Payload (Snapshot)
```json
{
	"requestBatchId": "string",
	"endpointA": "https://api-a.example.com",
	"endpointB": "https://api-b.example.com",
	"headersA": { "X-Header": "value" },
	"headersB": { "X-Header": "value" },
	"timeoutMs": 30000,
	"maxConcurrency": 64,
	"modelName": "ComplexOrderResponse",
	"ignoreCollectionOrder": false,
	"ignoreStringCase": false,
	"ignoreXmlNamespaces": true,
	"ignoreRules": [ { "propertyPath": "Body.Header", "ignoreCompletely": true } ],
	"smartIgnoreRules": [ { "type": "PropertyName", "value": "Id" } ],
	"enableSemanticAnalysis": true,
	"enableEnhancedStructuralAnalysis": true
}
```

## Risks and Dependencies
**Risks**
- Shared config service can cause cross-job interference if not isolated.
- Large batch uploads can stress memory and disk I/O.
- Long-running jobs need cancellation and status polling reliability.

**Dependencies**
- Existing comparison configuration services and smart ignore rules.
- Request comparison API endpoints and job service.
- UI components used on Home for results and analysis.

## Rollout Strategy
- Behind `FeatureFlags:RequestComparisonEnabled`.
- Staging validation with representative request batches.
- Production rollout with monitoring for job duration and failure rates.
