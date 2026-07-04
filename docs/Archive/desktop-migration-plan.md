# Desktop Migration Plan: Blazor Hybrid (WPF + BlazorWebView)

## Architecture Overview

```
┌─────────────────────────────────────────────────┐
│              ComparisonTool.Desktop              │
│  (WPF Host + BlazorWebView + Desktop Services)  │
└───────────────┬─────────────────────────────────┘
                │ references
┌───────────────▼─────────────────────────────────┐
│               ComparisonTool.UI                  │
│  (Razor Class Library - shared components)       │
│  Components, Pages, Shared, CSS, JS              │
└───────────────┬─────────────────────────────────┘
                │ references
┌───────────────▼─────────────────────────────────┐
│              ComparisonTool.Core                 │
│  (Business logic, DI, Comparison engine,         │
│   Platform abstractions)                         │
└─────────────────────────────────────────────────┘
```

ComparisonTool.Web also references ComparisonTool.UI and ComparisonTool.Core,
keeping the web host operational during migration.

## Phase 1: Foundation (Current)
- [x] Create platform service abstractions in Core
- [x] Create ComparisonTool.UI Razor Class Library
- [x] Create ComparisonTool.Desktop WPF project with BlazorWebView
- [x] Desktop service implementations (file export, folder picker, notifications)
- [x] In-process progress publisher for desktop
- [x] Update solution and package references
- [ ] Verify desktop shell compiles and renders

## Phase 2: Component Migration
- [ ] Move Razor components from Web to UI library
- [ ] Update Web project to reference UI library
- [ ] Create web-side platform service implementations (wrapping IJSRuntime)
- [ ] Verify Web project still works unchanged
- [ ] Verify Desktop renders Home page with File/Folder comparison

## Phase 3: Request Comparison Desktop Path
- [ ] Replace HTTP-based request comparison panel with in-process gateway
- [ ] Replace SignalR progress subscription with event-based progress
- [ ] Wire up request comparison job service directly in Desktop
- [ ] Test full request comparison flow in Desktop

## Phase 4: Polish & Hardening
- [ ] Native file save dialogs for export
- [ ] Window state persistence (size, position)
- [ ] Error handling and crash recovery
- [ ] Temp file lifecycle management
- [ ] Performance testing with large datasets
- [ ] Remove web-only code paths from shared components

## Key Decisions
- **WPF + BlazorWebView** over MAUI: simpler, Windows-only target, no MAUI overhead
- **Shared UI library**: enables both Web and Desktop to coexist during migration
- **Platform abstractions in Core**: clean dependency direction, testable
- **In-process services**: no HTTP/SignalR overhead in desktop; direct service calls
