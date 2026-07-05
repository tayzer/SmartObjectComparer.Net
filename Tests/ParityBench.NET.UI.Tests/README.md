# ParityBench.NET.UI.Tests

Tests for shared V2 UI components and UI helpers.

## Covers

- Run history and result-view rendering.
- Paged detail display and lazy raw preview loading.
- Run workflow validation behavior.
- Manual rule parser behavior for ignore, smart ignore, and mask inputs.
- UI, Web, Desktop, Report, CLI, and V2 boundary smoke tests.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.UI.Tests\ParityBench.NET.UI.Tests.csproj -v:minimal
```
