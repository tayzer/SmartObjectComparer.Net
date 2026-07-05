# ParityBench.NET.Cli.Tests

Tests for the V2 CLI host.

## Covers

- Request command parsing.
- Required option validation.
- Request comparison command orchestration through fake workflow services.
- Report-output option behavior.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.Cli.Tests\ParityBench.NET.Cli.Tests.csproj -v:minimal
```
