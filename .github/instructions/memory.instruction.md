---
applyTo: '**'
---

# Long-Term Memory

## Project Overview
- ComparisonTool: XML/JSON file comparison tool with CLI, Web (Blazor Server), and Desktop (WPF + BlazorWebView) hosts
- Core business logic in ComparisonTool.Core (host-agnostic)
- Domain models in ComparisonTool.Domain
- .NET 10.0, Central Package Management via Directory.Packages.props/Directory.Build.props
- MudBlazor for UI components across both Web and Desktop

## Architecture Decisions
- Desktop target: WPF + BlazorWebView (not MAUI) — Windows-only, simpler
- Shared UI via ComparisonTool.UI Razor Class Library
- Platform abstractions in ComparisonTool.Core/Abstractions (IFileExportService, IFolderPickerService, INotificationService, IScrollService, IRequestComparisonGateway, IProgressSubscriber)
- Desktop uses in-process services; Web uses HTTP APIs + SignalR
- ConsoleProgressPublisher pattern (CLI) proved the IComparisonProgressPublisher abstraction works; Desktop follows same pattern

## Coding Conventions
- Central package management (all versions in Directory.Packages.props)
- StyleCop analyzers enabled globally
- Nullable reference types enabled
- Services registered via extension methods in DI namespace
- XML doc comments on public APIs

## User Preferences
- Prefers concrete, incremental implementation over abstract planning
- Values keeping existing Web UI functional during migration
- Wants to preserve all current UX/functionality in desktop version
