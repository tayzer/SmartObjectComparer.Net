# ParityBench.NET.Engine.Tests

Tests for V2 execution and comparison behavior.

## Covers

- Basic A/B request execution flow.
- Response classification and summary generation.
- CompareNETObjects option behavior.
- Masking, contract profile request transformation, and response normalization.
- Engine project boundary tests.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.Engine.Tests\ParityBench.NET.Engine.Tests.csproj -v:minimal
```
