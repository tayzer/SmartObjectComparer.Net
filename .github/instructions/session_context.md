---
applyTo: '**'
lastUpdated: 2026-03-18T00:00:00Z
sessionStatus: active
---

# Current Session Context

## Latest Task
Implement accepted-difference persistence and bug tracking for structured comparison differences across Web and Desktop.

## Latest Task Status
```markdown
- [x] Add shared accepted-difference fingerprinting and JSON-backed persistence in Core
- [x] Register the persistence service in shared DI and host configuration
- [x] Extend DetailedDifferencesView with persisted classifications and noise filtering
- [x] Harden store access for cross-process reads/writes and malformed JSON
- [x] Add accepted-difference profile management and import/export panels in configuration UI
- [x] Refresh open detailed-difference views after profile imports/removals/clear-all
- [x] Preserve immutable detail-view search paths while rendering relative property labels
- [x] Enforce Known Bug ticket-ID invariant on profile imports
- [x] Add unit tests for fingerprinting, persistence reload, malformed payloads, and concurrent saves
- [x] Verify with `dotnet build ComparisonTool.sln`
- [x] Verify with `dotnet test ComparisonTool.Tests/ComparisonTool.Tests.csproj --filter AcceptedDifferenceServiceTests`
```

## Latest Session Notes
- Added a shared accepted-difference store under `ComparisonTool.Core/AcceptedDifferences` with stable fingerprints based on normalized property paths plus scrubbed value patterns.
- Relative accepted-difference store paths now resolve under the machine-level application data root, so Web and Desktop default to the same physical JSON store on the same machine.
- Detailed structured-difference UI now supports `Accepted Difference`, `Known Bug`, and `Fixed / Verified` classifications and can hide accepted or known-bug noise while keeping fixed regressions visible.
- Added reusable profile management/import-export UI in both file/folder and request comparison configuration panels.
- Configuration-side profile changes now refresh the currently selected detailed comparison without forcing a rerun.
- Store access now uses a sidecar lock file plus reload-before-write semantics to avoid lost updates across multiple service instances.

## Active Task
Migrate from Blazor Server web UI to Blazor Hybrid (WPF + BlazorWebView) desktop app

## Todo List Status
```markdown
### Phase 1: Foundation
- [x] Create platform service abstractions in ComparisonTool.Core/Abstractions
- [x] Create ComparisonTool.UI Razor Class Library (shared components target)
- [x] Create ComparisonTool.Desktop WPF project with BlazorWebView shell
- [x] Implement desktop services (file export, folder picker, notifications, scroll)
- [x] Implement in-process progress publisher/subscriber (replaces SignalR)
- [x] Implement in-process request comparison gateway (replaces HTTP APIs)
- [x] Update Directory.Packages.props with WebView.Wpf package
- [x] Add both new projects to solution file
- [ ] Move shared Razor components from Web to UI library
- [ ] Update Web project to reference UI library (keep Web working)
- [ ] Create web-side platform service implementations (IJSRuntime wrappers)
- [ ] Verify Desktop shell compiles and renders Home page
- [ ] Verify Web project still functions unchanged

### Phase 2: Component Migration (Complete)
- [x] Create Web Implementation Services
- [x] Move Home.razor, RequestComparisonPanel.razor, and Shared components to ComparisonTool.UI
- [x] Move Pages from Web/Components/Pages to UI
- [x] Move Comparison components from Web/Components/Comparison to UI
- [x] Move Shared components from Web/Components/Shared to UI
- [x] Move Layout from Web/Components/Layout to UI
- [x] Refactor IJSRuntime.InvokeVoidAsync("alert",...) calls to use INotificationService
- [x] Refactor IJSRuntime.InvokeVoidAsync("downloadFile"/"saveAsFile",...) to use IFileExportService
- [x] Refactor IJSRuntime.InvokeAsync<string>("browseFolder",...) to use IFolderPickerService
- [x] Refactor IJSRuntime.InvokeVoidAsync("scrollToElement",...) to use IScrollService
- [x] Refactor RequestComparisonPanel HTTP calls to use IRequestComparisonGateway
- [x] Refactor ComparisonProgressService usage to use IProgressSubscriber

*Note: The entire solution currently builds with 0 errors.*

### Phase 3: Request Comparison Desktop Path
- [x] Wire up request comparison flow end-to-end in Desktop
- [x] Test full request comparison lifecycle in Desktop

### Phase 4: Polish & Hardening
- [ ] Native file save dialogs for all exports
- [ ] Window state persistence
- [ ] Error handling and crash recovery
- [ ] Temp file lifecycle management
- [ ] Performance testing with large datasets
```

## Recent File Changes
- `ComparisonTool.Core/Abstractions/IFileExportService.cs`: New - platform-agnostic file export interface
- `ComparisonTool.Core/Abstractions/IFolderPickerService.cs`: New - platform-agnostic folder picker interface
- `ComparisonTool.Core/Abstractions/INotificationService.cs`: New - platform-agnostic alert/notification interface
- `ComparisonTool.Core/Abstractions/IScrollService.cs`: New - platform-agnostic scroll service interface
- `ComparisonTool.Core/Abstractions/IRequestComparisonGateway.cs`: New - replaces HTTP API for request comparison
- `ComparisonTool.Core/Abstractions/IProgressSubscriber.cs`: New - replaces SignalR for progress updates
- `ComparisonTool.UI/ComparisonTool.UI.csproj`: New - Razor Class Library for shared UI components
- `ComparisonTool.UI/_Imports.razor`: New - shared Razor imports
- `ComparisonTool.UI/wwwroot/js/app.js`: New - shared JS interop functions
- `ComparisonTool.Desktop/ComparisonTool.Desktop.csproj`: New - WPF host project
- `ComparisonTool.Desktop/App.xaml[.cs]`: New - WPF app with full DI setup
- `ComparisonTool.Desktop/MainWindow.xaml[.cs]`: New - BlazorWebView host window
- `ComparisonTool.Desktop/Main.razor`: New - root Blazor component for desktop
- `ComparisonTool.Desktop/wwwroot/index.html`: New - BlazorWebView host page
- `ComparisonTool.Desktop/Services/*.cs`: New - 7 desktop service implementations
- `Directory.Packages.props`: Added Microsoft.AspNetCore.Components.WebView.Wpf
- `ComparisonTool.sln`: Added ComparisonTool.UI and ComparisonTool.Desktop projects

## Key Technical Decisions
- Decision: WPF + BlazorWebView over MAUI for desktop host
- Rationale: Windows-only target, simpler setup, no MAUI overhead, direct WPF interop
- Date: 2026-03-04

- Decision: Shared UI library (ComparisonTool.UI) for component reuse
- Rationale: Both Web and Desktop hosts reference same components; avoids duplication
- Date: 2026-03-04

- Decision: Platform abstractions in Core, implementations in host projects
- Rationale: Clean dependency direction, testable, follows existing CLI pattern
- Date: 2026-03-04

- Decision: In-process services replace HTTP APIs and SignalR in Desktop
- Rationale: No network overhead; direct service calls; follows ConsoleProgressPublisher pattern from CLI
- Date: 2026-03-04

## Architecture
```
ComparisonTool.Desktop (WPF+BlazorWebView) ─┐
                                              ├─► ComparisonTool.UI (Razor Class Library)
ComparisonTool.Web (Blazor Server) ──────────┘         │
                                                       ▼
                                              ComparisonTool.Core (Business Logic + Abstractions)
```

## External Resources Referenced
- https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/

## Blockers & Issues
- None

## Environment Notes
- .NET 10.0 (SDK 10.0.103)
- MudBlazor 8.15.0
- Microsoft.AspNetCore.Components.WebView.Wpf 10.0.3

## Next Steps
Move Razor components from ComparisonTool.Web to ComparisonTool.UI, then wire up
Web platform service implementations so both hosts render the same UI.

## Todo List Status
```markdown
- [x] Verify reported CS1503 location and reproduce context
- [x] Patch search field callback wiring in DetailedDifferencesView
- [x] Re-run diagnostics to confirm error cleared
- [x] Finalize session context
```

## Recent File Changes
- `ComparisonTool.Core/Serialization/XmlDeserializationService.cs`: Made model serializer pre-caching non-fatal during registration (`RegisterDomainModel<T>` now catches and logs pre-cache exceptions, then falls back to lazy creation when model is actually used).
- `ComparisonTool.Cli/Commands/RequestCompareCommand.cs`: Added `using ComparisonTool.Core.Serialization;`, updated `-m` option description, added early model-name validation after service provider creation — gives clear "Unknown model / Available models" error before any comparison starts.
- `ComparisonTool.Cli/Commands/FolderCompareCommand.cs`: Added early model-name validation (identical pattern) after service provider creation.

## Key Technical Decisions
- Decision: Keep eager serializer pre-cache as a best-effort optimization only; never fail registration if one model serializer cannot pre-initialize.
- Rationale: Request/folder runs should only fail for the selected `-m` model; unrelated registered models (for example `SoapEnvelope`) must not break startup/execution.
- Date: 2026-03-02
- Decision: Validate model names BEFORE creating the comparison job, using `IXmlDeserializationService.GetRegisteredModelNames()`.
- Rationale: Previously unregistered names propagated deep into the pipeline producing per-file `ArgumentException` failures that were difficult to diagnose. The error the user saw ("ComplexOrderResponse cannot be serialized...") was a secondary/misleading error.
- Date: 2026-03-02

## Root Cause Analysis
1. `CreditReportDomain` is not defined or registered anywhere in the codebase.
2. `GetModelType("CreditReportDomain")` throws `ArgumentException` inside each file-pair comparison loop.
3. Per-pair errors were stored in `FilePairComparisonResult.ErrorMessage` — the user saw a confusing error mentioning `ComplexOrderResponse` (unrelated secondary failure path).
4. The `"Auto"` default was also broken — `"Auto"` is never registered, so it always failed at the same model-lookup stage.

## External Resources Referenced
- None (workspace-only investigation and fix).

## Blockers & Issues
- None

## Failed Approaches
- None

## Environment Notes
- .NET 10.0

## Next Session Priority
No active tasks.

## Session Notes
- Verified build: `dotnet build ComparisonTool.Cli/ComparisonTool.Cli.csproj` (0 errors).
- Verified request flow with local mock endpoints and `-m ComplexOrderResponse` runs successfully (differences reported, no `SoapEnvelope` constructor failure).
- Added upfront validation in both `RequestCompareCommand` and `FolderCompareCommand`.
- `dotnet build` passes (0 errors).
- Verified: `-m CreditReportDomain` → "Error: Unknown model name 'CreditReportDomain'. Available models: ComplexOrderResponse, SoapEnvelope."
- Verified: no `-m` (Auto) → "Error: A domain model name must be specified with -m. 'Auto' is not a valid model name. Available models: ComplexOrderResponse, SoapEnvelope."
- Verified: `-m ComplexOrderResponse` → passes validation, proceeds to comparison.

## Todo List Status
```markdown
- [x] Remove `--debug-non-success-bodies` and `--debug-artifacts-dir` from request CLI options
- [x] Remove debug artifact export pipeline/helpers from request command
- [x] Remove debug artifact metadata output from console/json reports
- [x] Update README request example to use `--disable-truncation`
- [x] Build `ComparisonTool.Cli` to verify compilation
```

## Recent File Changes
- `ComparisonTool.Cli/Commands/RequestCompareCommand.cs`: Removed legacy debug artifact options, execution branch, and helper types/methods.
- `ComparisonTool.Cli/Reporting/ConsoleReportWriter.cs`: Removed debug artifact directory/index lines from summary output.
- `ComparisonTool.Cli/Reporting/JsonReportWriter.cs`: Removed `debugArtifacts` JSON section and associated metadata helper methods.
- `README.md`: Replaced debug artifact flags in request example with `--disable-truncation` and updated troubleshooting note.

## Key Technical Decisions
- Decision: Fully retire debug-artifact export flags/logic now that inline report truncation can be disabled.
- Rationale: Avoid duplicate troubleshooting paths and keep CLI/report behavior simpler and consistent.
- Date: 2026-02-27

## External Resources Referenced
- None (workspace-only implementation).

## Blockers & Issues
- None

## Failed Approaches
- None

## Environment Notes
- .NET 10.0

## Next Session Priority
No active tasks.

## Session Notes
Removed artifact export pathway and references; troubleshooting now relies on `--disable-truncation` inline report output. `dotnet build ComparisonTool.Cli/ComparisonTool.Cli.csproj` succeeds (warnings only).

---
# Previous Session Archive

---
applyTo: '**'
lastUpdated: 2026-02-04T12:45:00Z
sessionStatus: complete
---

# Current Session Context

## Active Task
Fix build error caused by mismatched anonymous types in conditional expression

## Todo List Status
```markdown
- [x] 🔍 Locate conditional expression mismatch
- [x] 🛠️ Unify anonymous type shape in response
- [x] ✅ Build MockApi project
- [x] 📝 Update session context status
```

## Recent File Changes
- ComparisonTool.MockApi/Program.cs: Always include `diff` property in JSON response to keep anonymous type consistent

## Key Technical Decisions
- Decision: Always include `diff` property (null when not used)
- Rationale: Ensures conditional expression has a single anonymous type and compiles
- Date: 2026-02-03

## External Resources Referenced
- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/conditional-operator: Conditional operator type rules
- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/expressions#1220-conditional-operator: Specification details
- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-9.0/target-typed-conditional-expression: Target-typed conditional expression behavior

## Blockers & Issues
- None

## Failed Approaches
- None

## Environment Notes
- .NET 8.0

## Next Session Priority
No active tasks

## Session Notes
- Conditional JSON response now has a stable anonymous type shape
- Build succeeded for ComparisonTool.MockApi
applyTo: '**'
lastUpdated: 2026-02-03T19:10:00Z
sessionStatus: complete
---

# Current Session Context

## Active Task
Expose Request Comparison UI on homepage

## Todo List Status
```markdown
- [x] 🔍 Review Home page layout
- [x] 🧭 Add Request Comparison tab
- [x] ✅ Build to verify UI changes
```

## Recent File Changes
- `ComparisonTool.Web\Components\Pages\Home.razor`: Added tabs for File/Folder vs Request Comparison and feature flag gate

## Key Technical Decisions
- Decision: Add a MudTabs switcher to expose Request Comparison alongside existing file/folder comparison
- Rationale: Minimal UI change while preserving existing workflows
- Date: 2026-02-03

## External Resources Referenced
- None

## Blockers & Issues
- None

## Failed Approaches
- None

## Environment Notes
- .NET 8.0

## Next Session Priority
No active tasks

## Session Notes
- Build succeeded for ComparisonTool.Web (warnings only)---
applyTo: '**'
lastUpdated: 2026-02-03T18:45:00Z
sessionStatus: complete
---

# Current Session Context

## Active Task
Extend test data generator to support SoapEnvelope and add a toggle for domain selection

## Todo List Status
```markdown
- [x] 🔧 Add domain toggle + SoapEnvelope generator
- [x] 📝 Add Soap dataset docs and output structure
- [x] ✅ Build/test generator project
```

## Recent File Changes
- ComparisonTool.TestDataGenerator/Program.cs: Added domain toggle, SoapEnvelope generator, SOAP dataset docs, and namespace-aware serialization

## Key Technical Decisions
- Decision: Support `complex`, `soap`, or `both` via command-line `--domain` flag
- Rationale: Simple toggle without changing project structure
- Date: 2026-02-03

## External Resources Referenced
- https://learn.microsoft.com/en-us/dotnet/api/system.xml.serialization.xmlserializernamespaces?view=net-8.0: Namespace/prefix usage and serialization examples
- https://learn.microsoft.com/en-us/dotnet/api/system.xml.serialization.xmlserializer?view=net-8.0: Serialize overloads with namespaces
- Google search blocked (HTTP 451) when attempting to access search results via fetch

## Blockers & Issues
- None

## Failed Approaches
- Attempt: Google search via fetch_webpage
- Failure Reason: HTTP 451 response (blocked)
- Lesson: Use direct docs links when search is blocked

## Environment Notes
- .NET 8.0

## Next Session Priority
No active tasks

## Session Notes
- Build succeeded for ComparisonTool.TestDataGenerator---
applyTo: '**'
lastUpdated: 2026-02-03T18:30:00Z
sessionStatus: active
---

# Current Session Context

## Active Task
Extend test data generator to support SoapEnvelope and add a toggle for domain selection

## Todo List Status
```markdown
- [ ] 🔧 Add domain toggle + SoapEnvelope generator
- [ ] 📝 Add Soap dataset docs and output structure
- [ ] ✅ Build/test generator project
```

## Recent File Changes
- None yet

## Key Technical Decisions
- Decision: Support `complex`, `soap`, or `both` via command-line `--domain` flag
- Rationale: Simple toggle without changing project structure
- Date: 2026-02-03

## External Resources Referenced
- https://learn.microsoft.com/en-us/dotnet/api/system.xml.serialization.xmlserializernamespaces?view=net-8.0: Namespace/prefix usage and serialization examples
- https://learn.microsoft.com/en-us/dotnet/api/system.xml.serialization.xmlserializer?view=net-8.0: Serialize overloads with namespaces
- Google search blocked (HTTP 451) when attempting to access search results via fetch

## Blockers & Issues
- None

## Failed Approaches
- Attempt: Google search via fetch_webpage
- Failure Reason: HTTP 451 response (blocked)
- Lesson: Use direct docs links when search is blocked

## Environment Notes
- .NET 8.0

## Next Session Priority
Implement SoapEnvelope generator and domain selection flag in Program.cs

## Session Notes
- Need to keep XML namespaces for SoapEnvelope output via XmlSerializerNamespaces---
applyTo: '**'
lastUpdated: 2026-02-03T10:45:00Z
sessionStatus: complete
---

# Current Session Context

## Active Task
Fix DI lifetime error for RequestComparisonJobService

## Todo List Status
```markdown
- [x] 🔎 Investigate DI error and service lifetimes
- [x] 🛠️ Update RequestComparisonJobService to resolve scoped dependencies safely
- [x] ✅ Build to verify fix
```

## Recent File Changes
- `ComparisonTool.Core\RequestComparison\Services\RequestComparisonJobService.cs`: Use IServiceScopeFactory to resolve DirectoryComparisonService per job execution

## Key Technical Decisions
- Decision: Keep RequestComparisonJobService as singleton and resolve scoped DirectoryComparisonService inside a scope
- Rationale: Preserves singleton job tracking while avoiding captive scoped dependency
- Date: 2026-02-03

## External Resources Referenced
- https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#scope-validation: Scope validation rules
- https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines: Captive dependency guidance

## Blockers & Issues
- **[RESOLVED]** InvalidOperationException: Cannot consume scoped service from singleton

## Failed Approaches
- Google search blocked (HTTP 451) when attempting to access search results via fetch

## Environment Notes
- .NET 8.0

## Next Session Priority
No active tasks

## Session Notes
- Build succeeded for ComparisonTool.Web after DI fix (warnings only)---
applyTo: '**'
lastUpdated: 2026-02-02T22:30:00Z
sessionStatus: complete
---

# Current Session Context

## Active Task
Implement Request Folder Comparison feature (RC-101 through RC-114) - COMPLETED

## Todo List Status
```markdown
- [x]  M1: Create domain models for request comparison
- [x]  M2a: Add request batch upload endpoint
- [x]  M2b: Add job creation, status, and result endpoints  
- [x]  M3: Implement request execution pipeline with bounded concurrency
- [x]  M4: Integrate with existing comparison/analysis pipeline
- [x]  M5: Add UI components for request comparison
- [x]  M6: Add unit tests (12 tests for RequestFileParserService)
- [x]  M7: Add feature flag and configuration
```

## Recent File Changes
- `ComparisonTool.Core\RequestComparison\Models\RequestComparisonJob.cs`: Job model with status enum
- `ComparisonTool.Core\RequestComparison\Models\RequestFileInfo.cs`: Request file metadata model
- `ComparisonTool.Core\RequestComparison\Models\ApiContracts.cs`: API request/response DTOs
- `ComparisonTool.Core\RequestComparison\Services\RequestExecutionService.cs`: HTTP execution with bounded concurrency
- `ComparisonTool.Core\RequestComparison\Services\RequestComparisonJobService.cs`: Job lifecycle management
- `ComparisonTool.Core\RequestComparison\Services\RequestFileParserService.cs`: Request file parsing with sidecar headers
- `ComparisonTool.Web\RequestComparisonApi.cs`: API endpoints for batch upload and job management
- `ComparisonTool.Web\Components\Comparison\RequestComparisonPanel.razor`: UI component with 3-step wizard
- `ComparisonTool.Web\Program.cs`: Service registration and API endpoint mapping
- `ComparisonTool.Web\appsettings.json`: Feature flag and configuration settings
- `ComparisonTool.Core\ComparisonTool.Core.csproj`: Added Microsoft.Extensions.Http package
- `ComparisonTool.Tests\Unit\RequestComparison\RequestFileParserServiceTests.cs`: Unit tests for parser service

## Key Technical Decisions
- Decision: Use IHttpClientFactory for HTTP client management
- Rationale: Proper connection pooling and lifecycle management for high-volume requests

- Decision: Use Parallel.ForEachAsync with bounded concurrency
- Rationale: Efficient parallel execution with configurable max concurrency (default 64)

- Decision: Stream responses directly to disk using ArrayPool<byte> buffers
- Rationale: Avoid OOM for 40k+ requests with potentially large responses

- Decision: Sidecar header files use `.headers.json` extension
- Rationale: Per-request custom headers without modifying payload files

## External Resources Referenced
- docs/RequestComparisonFeatureSpec.md: API contracts and execution flow
- docs/JiraTickets_RequestComparison.md: User stories and acceptance criteria
- docs/RequestComparisonPlan.md: Delivery milestones

## Blockers & Issues
- **[RESOLVED]** String interpolation issues in PowerShell here-strings - fixed by using replace operations
- **[RESOLVED]** Missing IHttpClientFactory package - added Microsoft.Extensions.Http
- **[RESOLVED]** Wrong test framework (xUnit vs MSTest) - rewrote tests using MSTest + FluentAssertions

## Failed Approaches
- Approach: Using Assert.ThrowsExceptionAsync from MSTest directly
- Failure Reason: Project uses FluentAssertions pattern `await action.Should().ThrowAsync<T>()`
- Lesson: Check existing test patterns before writing new tests

## Environment Notes
- .NET 8.0
- MSTest + FluentAssertions + Moq for testing
- New named HttpClient: RequestComparison
- Feature flag: `FeatureFlags:RequestComparisonEnabled = true`
- Config section: `RequestComparison` with MaxConcurrency=64, DefaultTimeoutMs=30000

## Next Session Priority
No active tasks - feature implementation complete. Consider:
- Adding more tests for RequestExecutionService (header merging)
- Adding integration tests for full workflow
- Testing with actual 40k+ file volumes

## Session Notes
- Feature implementation complete with all milestones achieved
- Build passes with 0 errors (146 warnings - most pre-existing)
- All 89 tests pass (77 original + 12 new RequestFileParserService tests)
- Ready for manual testing and potential production deployment
