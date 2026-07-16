# Feature Spec: Non-Success (Non-200) Response Comparison

## Summary

Introduce status-code-aware classification of A/B request pairs so that non-200 responses are surfaced, diffed safely, and never crash the job via failed domain-model deserialization.

## Problem Statement

The Request Comparison feature calls two endpoints per request file and compares the responses by deserializing them to a domain model. **This breaks when either endpoint returns a non-200 response.**

### Current Behaviour

| Scenario | What Happens Today | Problem |
|---|---|---|
| A returns 200, B returns 500 | Both response bodies saved to disk and compared via domain-model deserialization | Deserialization of the 500 error body (HTML, plain text, JSON error object) **fails and crashes the job**, or produces a nonsensical diff |
| Both return 500 | Both error bodies saved and compared | May report "Identical" — hiding the fact that neither endpoint actually worked |
| A times out (exception) | `Success=false`, no file written for A; file written for B | B's response file is orphaned — `DirectoryComparisonService` pairs by filename, so B has no pair and appears as "only in B" |
| Both return 200 but one is a `text/html` error page (e.g. gateway timeout) | Bodies compared against domain model | Deserialization crash or garbage diff |

### Root Cause Chain

1. **`RequestExecutionService.ExecuteSingleRequestAsync`** — `Success = true` is set whenever the HTTP call completes without exception, regardless of status code. Status codes are recorded but **never inspected**.
2. **`RequestComparisonJobService.ExecuteJobAsync`** — Execution metadata only stores results where `!r.Success` (exceptions). Non-200 but "successful" calls are invisible in the metadata.
3. **`DirectoryComparisonService`** — Receives two response folders and deserializes everything. It has no concept of HTTP status codes, response validity, or error-body detection.

## Goals

- Prevent job crashes when endpoints return non-200 responses.
- Clearly surface HTTP status code mismatches as first-class comparison differences.
- Compare error response bodies as raw text instead of attempting domain-model deserialization.
- Provide per-request status code visibility in the UI.

## Non-Goals

- Detecting "soft errors" (200 status with error payload in body) — out of scope for initial implementation.
- Changing the existing domain-model comparison pipeline for successful (2xx) pairs.
- Retrying failed requests automatically.

## Design

### Request Pair Classification

A new classification step is inserted between the execution phase (Phase 2) and comparison phase (Phase 3):

```
┌─────────────────┐
│ ExecuteRequests  │  (existing — saves responses to disk + records status codes)
└────────┬────────┘
         │
         ▼
┌─────────────────────────┐
│ ClassifyExecutionResults │  (NEW — categorises each request pair)
└────────┬────────────────┘
         │
         ├── BothSuccess (200/200)        → domain-model comparison (existing pipeline)
         ├── StatusCodeMismatch (200/500)  → raw text diff + status code difference record
         ├── BothNonSuccess (500/500)      → raw text diff of error bodies
         └── OneOrBothFailed (exception)   → error-only record (existing behaviour)
```

### New Types

```csharp
/// <summary>
/// Classifies the HTTP outcome of an A/B request pair.
/// </summary>
public enum RequestPairOutcome
{
    /// <summary>Both endpoints returned 2xx — normal domain-model comparison.</summary>
    BothSuccess,

    /// <summary>One returned 2xx, the other did not — critical mismatch.</summary>
    StatusCodeMismatch,

    /// <summary>Both returned non-2xx — compare error bodies as raw text.</summary>
    BothNonSuccess,

    /// <summary>One or both threw exceptions (timeout, DNS, etc.).</summary>
    OneOrBothFailed
}
```

```csharp
/// <summary>
/// Wraps an execution result with its classified outcome.
/// </summary>
public record ClassifiedExecutionResult
{
    public required RequestExecutionResult Execution { get; init; }
    public required RequestPairOutcome Outcome { get; init; }
    public string? OutcomeReason { get; init; } // e.g. "A=200, B=500"
}
```

### Classification Logic

```csharp
public static RequestPairOutcome Classify(RequestExecutionResult result)
{
    if (!result.Success)
        return RequestPairOutcome.OneOrBothFailed;

    bool aOk = result.StatusCodeA is >= 200 and < 300;
    bool bOk = result.StatusCodeB is >= 200 and < 300;

    return (aOk, bOk) switch
    {
        (true, true)   => RequestPairOutcome.BothSuccess,
        (false, false) => RequestPairOutcome.BothNonSuccess,
        _              => RequestPairOutcome.StatusCodeMismatch
    };
}
```

### Orchestration Change (RequestComparisonJobService)

Between Phase 2 (execute) and Phase 3 (compare), add:

```csharp
var classified = executionResults
    .Select(r => new ClassifiedExecutionResult
    {
        Execution = r,
        Outcome = Classify(r),
        OutcomeReason = $"A={r.StatusCodeA}, B={r.StatusCodeB}"
    })
    .ToList();

var successPairs = classified.Where(c => c.Outcome == RequestPairOutcome.BothSuccess).ToList();
var mismatchPairs = classified.Where(c => c.Outcome == RequestPairOutcome.StatusCodeMismatch).ToList();
var bothErrorPairs = classified.Where(c => c.Outcome == RequestPairOutcome.BothNonSuccess).ToList();
var failedPairs = classified.Where(c => c.Outcome == RequestPairOutcome.OneOrBothFailed).ToList();

// Phase 3a: Domain-model comparison for successPairs (existing pipeline, unchanged)
// Phase 3b: Raw text diff for mismatchPairs + bothErrorPairs (new lightweight diff)
// Phase 3c: Error records for failedPairs (existing behaviour)
```

### Comparison Strategy Per Outcome

| Outcome | Comparison Strategy | Result Type |
|---|---|---|
| `BothSuccess` | Existing `DirectoryComparisonService` pipeline — domain-model deserialization + KellermanSoftware `CompareNetObjects` | Existing `FileComparisonResult` with typed differences |
| `StatusCodeMismatch` | Record status code difference as primary diff. Raw text diff of response bodies (line-by-line or DiffPlex). No deserialization. | `FileComparisonResult` with status code diff + text diffs |
| `BothNonSuccess` | Raw text diff of error bodies. No deserialization. | `FileComparisonResult` with text diffs |
| `OneOrBothFailed` | Error record only — no file comparison possible. | Error metadata record (existing) |

### Model Extensions

Extend `RequestExecutionResult` with response content types:

```csharp
/// <summary>Gets the Content-Type header from endpoint A's response.</summary>
public string? ContentTypeA { get; init; }

/// <summary>Gets the Content-Type header from endpoint B's response.</summary>
public string? ContentTypeB { get; init; }
```

Add status code info to per-file comparison results for UI consumption:

```csharp
// Added to FileComparisonResult or a wrapper
public int? StatusCodeA { get; init; }
public int? StatusCodeB { get; init; }
public RequestPairOutcome? PairOutcome { get; init; }
```

Add structured execution summary to `MultiFolderComparisonResult.Metadata`:

```csharp
comparisonResult.Metadata["ExecutionSummary"] = new
{
    TotalRequests = classified.Count,
    BothSuccess = successPairs.Count,
    StatusCodeMismatch = mismatchPairs.Count,
    BothNonSuccess = bothErrorPairs.Count,
    OneOrBothFailed = failedPairs.Count
};
```

## Tasks

| # | Task | Estimate | Acceptance Criteria |
|---|---|---|---|
| 1 | Add `RequestPairOutcome` enum and `ClassifiedExecutionResult` model to `RequestComparison/Models/` | S | Enum covers all four outcomes. Model wraps `RequestExecutionResult` + outcome + reason. |
| 2 | Add `ContentTypeA` / `ContentTypeB` to `RequestExecutionResult`; capture `Content-Type` header in `RequestExecutionService.SendRequestAsync` | S | Response content types recorded for every completed request. |
| 3 | Add classification helper method and integrate into `RequestComparisonJobService.ExecuteJobAsync` between Phase 2 and Phase 3 | M | Results grouped by outcome. Classification breakdown logged. |
| 4 | Implement raw text diff service for `StatusCodeMismatch` and `BothNonSuccess` pairs — line-by-line comparison without deserialization | M | Non-success response bodies compared as text. Diff result includes status codes and body differences. |
| 5 | Merge raw text diff results into `MultiFolderComparisonResult` alongside domain-model results | S | All pair types appear in the final result with their outcome clearly marked. |
| 6 | Add `ExecutionSummary` to `MultiFolderComparisonResult.Metadata` with structured counts per outcome | S | Metadata contains `BothSuccess`, `StatusCodeMismatch`, `BothNonSuccess`, `OneOrBothFailed` counts. |
| 7 | Surface `StatusCodeA`, `StatusCodeB`, `PairOutcome` in per-file comparison results | S | Status codes visible per file pair in the result payload. |
| 8 | Update UI to render status-code-mismatch pairs with warning badges and HTTP status pills | M | User can immediately spot which requests had non-200 responses and what the status codes were. |
| 9 | Add unit tests for classification logic and edge cases (204/200, 301/200, 500/500, timeout + 200) | M | Coverage for all `RequestPairOutcome` branches and boundary status codes. |

## Acceptance Criteria

- A job where one endpoint returns 500 for some requests **does not crash**.
- Status code mismatches are surfaced as **the primary difference** for those pairs.
- Non-200 response bodies are compared as raw text, not deserialized to domain models.
- The result payload includes per-request status codes and a summary of outcome categories.
- Existing behaviour for `BothSuccess` pairs is **unchanged**.
- Unit tests cover all four outcome classifications and edge cases.

## Rollout Plan

1. **Phase A** — Ship classification + metadata enrichment (tasks 1–3, 6). No UI change; richer data in results. Prevents job crashes.
2. **Phase B** — Ship raw text diff for non-success pairs (tasks 4–5, 7). Full comparison coverage for all outcome types.
3. **Phase C** — Ship UI enhancements (task 8). Surface the data to users with status badges.

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---:|---:|---|
| Raw text diff produces noisy results for large error bodies | Medium | Low | Truncate response preview to first 5 KB; show "error body too large" if exceeded |
| Some APIs return 200 with error in body (soft errors) | Medium | Medium | Out of scope for v1; document as future enhancement — detect JSON `"error"` / `"fault"` keys in 200 bodies |
| Text diff library adds a new dependency | Low | Low | Use line-split `String` comparison first; optionally add DiffPlex (lightweight, MIT) later |
| Per-file result model change breaks existing UI/serialization | Medium | Medium | Add new properties as nullable/optional so existing consumers are unaffected |
| `BothNonSuccess` reported as "Identical" hides real problems | Low | Medium | Always flag `BothNonSuccess` with a warning regardless of body equality |

## Dependencies

- No new NuGet packages required for v1 (line-by-line text diff can be implemented with `String.Split` + basic diff algorithm).
- Optional: [DiffPlex](https://github.com/mmanela/diffplex) (MIT license) for richer text diff output in Phase B.

## Files Affected

| File | Change |
|---|---|
| `ComparisonTool.Core/RequestComparison/Models/RequestFileInfo.cs` | Add `ContentTypeA`, `ContentTypeB` to `RequestExecutionResult` |
| `ComparisonTool.Core/RequestComparison/Models/` (new file) | `RequestPairOutcome` enum, `ClassifiedExecutionResult` record |
| `ComparisonTool.Core/RequestComparison/Services/RequestExecutionService.cs` | Capture `Content-Type` header from HTTP responses |
| `ComparisonTool.Core/RequestComparison/Services/RequestComparisonJobService.cs` | Classification step between Phase 2 and Phase 3; separate comparison pipelines per outcome |
| `ComparisonTool.Core/RequestComparison/Services/` (new file) | Raw text diff service for non-success pairs |
| `ComparisonTool.Core/Comparison/Results/` | Optional additions to `FileComparisonResult` for status codes |
| `ComparisonTool.Web/Components/` | UI updates for status code display |
| `ComparisonTool.Tests/Unit/` (new files) | Classification and text diff tests |
