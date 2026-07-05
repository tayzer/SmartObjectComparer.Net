# ParityBench.NET.TestEndpoints.Tests

Tests for the V2 manual and E2E fixture server.

## Covers

- Consumer-report fixture scenario behavior.
- Sample customer lookup fixture behavior for `sample-soap-to-json`.
- Manual run catalog validity.
- Test endpoint project boundary tests.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.TestEndpoints.Tests\ParityBench.NET.TestEndpoints.Tests.csproj -v:minimal
```
