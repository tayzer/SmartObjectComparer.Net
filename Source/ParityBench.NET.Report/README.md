# ParityBench.NET.Report

Static Blazor WebAssembly host for bundled V2 reports.

## Owns

- Report-side application startup.
- Static report data-source registration.
- Rendering shared V2 result UI from packaged report data.

## Boundaries

- References `Domain` and `UI` only.
- Must not reference Infrastructure, Workspaces, Engine, Web, Desktop, CLI, or V1 projects.
- Static report data should be loaded lazily from report JSON pages and raw sidecars.

## Build

```powershell
dotnet build Source\ParityBench.NET.Report\ParityBench.NET.Report.csproj
```
