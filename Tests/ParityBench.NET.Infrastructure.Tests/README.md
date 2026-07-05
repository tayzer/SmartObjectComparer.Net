# ParityBench.NET.Infrastructure.Tests

Tests for V2 infrastructure adapters.

## Covers

- HTTP endpoint sender behavior.
- Response model registry and JSON/XML deserialization.
- Contract profile infrastructure.
- Static report bundle writing and report asset location.
- Consumer-report fixture response model registration.
- Infrastructure project boundary tests.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.Infrastructure.Tests\ParityBench.NET.Infrastructure.Tests.csproj -v:minimal
```
