# ParityBench.NET.Web

Opt-in Blazor Server host for V2.

## Owns

- Web composition root for V2 services.
- Workspace root configuration.
- MudBlazor, logging, data protection, and Blazor host setup.
- Rendering shared V2 UI components.

## Boundaries

- Host project only: configure DI and platform services, then delegate behavior to V2 layers.
- May reference concrete V2 layers because it is a composition root.
- Must not reference V1 projects or duplicate UI/business logic from shared layers.

## Run

```powershell
dotnet run --project Source\ParityBench.NET.Web\ParityBench.NET.Web.csproj
```
