# ParityBench.NET.Domain.Tests

Tests for V2 Domain contracts.

## Covers

- Run identities, lifecycle transitions, progress, and result summaries.
- Request and artifact value objects.
- Comparison option, ignore rule, smart ignore, mask rule, and contract profile selection validation.
- Architecture boundary smoke tests for the Domain project.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.Domain.Tests\ParityBench.NET.Domain.Tests.csproj -v:minimal
```
