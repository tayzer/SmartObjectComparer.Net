# ParityBench.NET.Application.Tests

Tests for V2 Application use cases and ports.

## Covers

- Run creation, start, cancellation, list, and summary-loading behavior.
- Host workflow use cases for request directory staging and report generation.
- Background run job coordination.
- Contract payload abstractions and Application boundary tests.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.Application.Tests\ParityBench.NET.Application.Tests.csproj -v:minimal
```
