# ParityBench.NET.TestEndpoints

Manual and E2E fixture server for V2.

## Owns

- Deterministic SOAP/XML and JSON endpoint variants for realistic consumer-report testing.
- Sample customer lookup endpoints for the `sample-soap-to-json` contract profile.
- A health endpoint for manual and automated startup checks.

## Boundaries

- Standalone external-service fixture, not a production layer.
- Has no V1 project references.
- Must keep scenarios deterministic so manual runs and future Playwright tests can share expectations.

## Run

```powershell
dotnet run --project Source\ParityBench.NET.TestEndpoints\ParityBench.NET.TestEndpoints.csproj --no-launch-profile --urls http://localhost:5056
```
