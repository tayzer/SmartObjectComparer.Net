# ParityBench.NET.Desktop

Opt-in WPF BlazorWebView host for V2.

## Owns

- Desktop composition root for V2 services.
- WPF application startup and BlazorWebView hosting.
- Workspace root configuration.
- Rendering shared V2 UI components.

## Boundaries

- Host project only: configure DI and platform services, then delegate behavior to V2 layers.
- May reference concrete V2 layers because it is a composition root.
- Must not reference V1 projects or duplicate UI/business logic from shared layers.

## Run

```powershell
dotnet run --project Source\ParityBench.NET.Desktop\ParityBench.NET.Desktop.csproj
```
