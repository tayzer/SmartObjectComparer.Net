# Request Comparison Progress UI - Implementation Plan

**Date:** 2026-02-04  
**Status:** Ready for Implementation

---

## Summary

Provide real-time, step-based progress updates for "Request Comparison" by streaming backend progress events to the UI via SignalR, then rendering a progress bar and activity log.

---

## Current Problem

The UI currently displays "Starting comparison..." and remains on that message for the entire duration of the comparison job. This happens because:

1. The backend (`RequestComparisonJobService.ExecuteJobAsync`) accepts an `IProgress<>` callback but the API layer (`RequestComparisonApi.CreateComparisonJob`) passes `null` for the progress parameter
2. The UI polls `/api/requests/compare/{jobId}/status` every 1 second but only receives coarse updates from `RequestComparisonJob.StatusMessage` which is updated infrequently
3. The existing `ComparisonProgress` class in `ComparisonTool.Core` tracks `Completed`/`Total`/`Status` but this data never reaches the UI

---

## Target Outcome

- Users see phase-by-phase progress with specific messages for each stage:
  - **Parsing** (0-5%): "Parsing request files..."
  - **Executing** (5-75%): "Executed 42 of 150 requests" (live updates per batch)
  - **Comparing** (75-100%): "Comparing responses..." with detailed sub-phases
- Updates appear within 500ms of backend events
- Activity log shows last 10 events with timestamps
- Final state is clearly "Completed" or "Failed" with error details

---

## Architecture Decision: SignalR Hub

**Decision:** Use SignalR for real-time push notifications  
**Rationale:**
- Already configured in `Program.cs` with appropriate settings
- Blazor Server has native SignalR integration
- More efficient than polling for high-frequency updates
- Can fall back to long-polling automatically

---

## Detailed Implementation Plan

### Phase 1: Backend - Progress Infrastructure

#### 1.1 Create Progress DTOs and Contracts

**File:** `ComparisonTool.Core/RequestComparison/Models/ProgressContracts.cs` (NEW)

Key types:
- `ComparisonPhase` enum: Initializing, Parsing, Executing, Comparing, Completed, Failed, Cancelled
- `ComparisonProgressUpdate` record: JobId, Phase, PercentComplete, Message, Timestamp, CompletedItems, TotalItems, ErrorMessage

#### 1.2 Create Progress Publisher Interface and Implementation

**File:** `ComparisonTool.Core/RequestComparison/Services/IComparisonProgressPublisher.cs` (NEW)

Interface with single method: `Task PublishAsync(ComparisonProgressUpdate update, CancellationToken cancellationToken = default)`

**File:** `ComparisonTool.Web/Services/SignalRProgressPublisher.cs` (NEW)

Implementation that injects `IHubContext<ComparisonProgressHub>` and sends updates to the job's SignalR group.

#### 1.3 Create SignalR Hub

**File:** `ComparisonTool.Web/Hubs/ComparisonProgressHub.cs` (NEW)

Hub with methods:
- `SubscribeToJob(string jobId)` - Adds connection to job group
- `UnsubscribeFromJob(string jobId)` - Removes connection from job group

#### 1.4 Update RequestComparisonJobService to Emit Progress

**File:** `ComparisonTool.Core/RequestComparison/Services/RequestComparisonJobService.cs` (MODIFY)

Key changes:
- Add `IComparisonProgressPublisher?` dependency (optional for backward compat)
- Create helper method `PublishProgressAsync()` for consistent updates
- Emit progress at each phase transition:
  - Phase 1: Parsing (0-5%)
  - Phase 2: Executing (5-75%) with per-request updates
  - Phase 3: Comparing (75-100%)
  - Phase 4: Complete/Failed/Cancelled
- Throttle updates to max 4/second during high-frequency phases

#### 1.5 Update RequestExecutionService with Finer Progress

**File:** `ComparisonTool.Core/RequestComparison/Services/RequestExecutionService.cs` (MODIFY)

Change progress reporting to fire more frequently during parallel execution rather than every 1%.

---

### Phase 2: Backend - API and DI Wiring

#### 2.1 Register Services in DI

**File:** `ComparisonTool.Web/Program.cs` (MODIFY)

Add:
- `builder.Services.AddSingleton<IComparisonProgressPublisher, SignalRProgressPublisher>()`
- `app.MapHub<ComparisonProgressHub>("/hubs/comparison-progress")`

#### 2.2 Update API to Use Progress Publisher

**File:** `ComparisonTool.Web/RequestComparisonApi.cs` (MODIFY)

- Inject `IComparisonProgressPublisher` into `CreateComparisonJob`
- Publish initial "Initializing" event before starting background task

---

### Phase 3: Frontend - SignalR Integration

#### 3.1 Create Progress Service for Blazor

**File:** `ComparisonTool.Web/Services/ComparisonProgressService.cs` (NEW)

Scoped service that:
- Manages `HubConnection` lifecycle
- Subscribes to job updates via `SubscribeToJobAsync(string jobId)`
- Exposes `event Action<ComparisonProgressUpdate>? OnProgressUpdate`
- Implements `IAsyncDisposable`

#### 3.2 Register Progress Service

**File:** `ComparisonTool.Web/Program.cs` (MODIFY)

Add: `builder.Services.AddScoped<ComparisonProgressService>()`

#### 3.3 Update RequestComparisonPanel.razor

**File:** `ComparisonTool.Web/Components/Comparison/RequestComparisonPanel.razor` (MODIFY)

Enhanced UI with:
- Phase icon and label
- Progress bar with percentage
- Status message
- Collapsible activity log showing last 10 events with timestamps
- Cancel button

Code-behind changes:
- Inject `ComparisonProgressService`
- Implement `IAsyncDisposable`
- Subscribe to SignalR on job start
- Handle progress updates with `InvokeAsync` + `StateHasChanged`
- Maintain `progressLog` list capped at 50 entries

---

## File Summary

| File | Action | Description |
|------|--------|-------------|
| `ComparisonTool.Core/RequestComparison/Models/ProgressContracts.cs` | CREATE | DTOs for progress updates |
| `ComparisonTool.Core/RequestComparison/Services/IComparisonProgressPublisher.cs` | CREATE | Publisher interface |
| `ComparisonTool.Web/Hubs/ComparisonProgressHub.cs` | CREATE | SignalR hub for progress |
| `ComparisonTool.Web/Services/SignalRProgressPublisher.cs` | CREATE | Hub-based publisher impl |
| `ComparisonTool.Web/Services/ComparisonProgressService.cs` | CREATE | Blazor client service |
| `ComparisonTool.Core/RequestComparison/Services/RequestComparisonJobService.cs` | MODIFY | Add progress publishing |
| `ComparisonTool.Core/RequestComparison/Services/RequestExecutionService.cs` | MODIFY | Finer progress updates |
| `ComparisonTool.Web/RequestComparisonApi.cs` | MODIFY | Wire publisher to job |
| `ComparisonTool.Web/Program.cs` | MODIFY | DI registration + hub mapping |
| `ComparisonTool.Web/Components/Comparison/RequestComparisonPanel.razor` | MODIFY | SignalR + enhanced UI |

---

## Tasks and Estimates

| # | Task | Size | Files | Acceptance Criteria |
|---|------|------|-------|---------------------|
| 1 | Create progress DTOs | S | ProgressContracts.cs | Compiles, DTO has all required fields |
| 2 | Create publisher interface | S | IComparisonProgressPublisher.cs | Interface defined with PublishAsync method |
| 3 | Create SignalR hub | S | ComparisonProgressHub.cs | Subscribe/Unsubscribe work |
| 4 | Create SignalR publisher | S | SignalRProgressPublisher.cs | Sends to correct group |
| 5 | Update job service | M | RequestComparisonJobService.cs | Emits progress at each phase |
| 6 | Update execution service | S | RequestExecutionService.cs | Reports per-batch progress |
| 7 | Wire DI + hub endpoint | S | Program.cs | Services resolve correctly |
| 8 | Update API to publish | S | RequestComparisonApi.cs | Initial event published |
| 9 | Create Blazor progress service | M | ComparisonProgressService.cs | Receives events, fires callback |
| 10 | Update UI component | M | RequestComparisonPanel.razor | Shows live updates + log |
| 11 | Integration testing | M | Manual testing | End-to-end progress works |

**Total Estimate:** ~6-8 hours of development

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| SignalR connection drops | Medium | Medium | Keep polling as fallback (reduced frequency) |
| Too many events overwhelm UI | Medium | Low | Throttle to max 4/sec, batch display updates |
| Progress percent jumps backward | Low | Medium | Only update if new percent >= current |
| Memory leak from event handlers | Medium | High | Implement IAsyncDisposable, unsubscribe on dispose |

---

## Testing Strategy

1. **Unit Tests**
   - SignalRProgressPublisher publishes to correct group
   - RequestComparisonJobService emits all phase transitions

2. **Integration Tests**
   - Start job -> subscribe -> receive updates -> complete

3. **Manual E2E Tests**
   - Upload files -> start comparison -> watch progress bar
   - Cancel mid-execution -> verify "Cancelled" state
   - Trigger error -> verify "Failed" state with message

---

## Rollout Plan

1. Implement backend infrastructure (Tasks 1-8)
2. Build and verify all tests pass
3. Implement frontend changes (Tasks 9-10)
4. Manual testing in development
5. Deploy to staging for validation
6. Enable in production

---

## Open Questions

- [ ] Should progress be persisted to database for historical review?
- [ ] Maximum number of log entries to retain per job?
- [ ] Should we add progress visualization to the job history view?
