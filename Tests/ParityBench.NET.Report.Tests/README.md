# ParityBench.NET.Report.Tests

Tests for the V2 static bundled report host.

## Covers

- Static report data-source loading.
- Lazy detail page and raw sidecar preview behavior.
- Shared result surface rendering in the report host.
- Report host boundary tests.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.Report.Tests\ParityBench.NET.Report.Tests.csproj -v:minimal
```
