# ParityBench.NET.Workspaces.Tests

Tests for V2 filesystem workspace behavior.

## Covers

- Request directory staging and request filtering.
- Run snapshot persistence.
- Artifact persistence and hash metadata.
- Detail index persistence, paging, filtering, and preview behavior.
- Workspaces project boundary tests.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.Workspaces.Tests\ParityBench.NET.Workspaces.Tests.csproj -v:minimal
```
