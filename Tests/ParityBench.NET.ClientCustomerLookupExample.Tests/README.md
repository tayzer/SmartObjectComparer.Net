# ParityBench.NET.ClientCustomerLookupExample.Tests

> **Legacy.** Pins the behaviour of the superseded compile-time contract-profile model. New client coverage belongs in `Tests/ParityBench.ClientCustomerLookupPlugin.Tests`.

## Covers

- **`ClientCustomerLookupExampleTests`** — the profile itself: request mapping, the chained token exchange in the request-preparation step, response normalization onto the canonical type, and the default comparison/ignore rules the profile seeds.
- **`ClientCustomerLookupExampleDefaultsTests`** — endpoint and preset registration for the `client-soap-json-token` preset.

## Boundaries

- Builds the profile directly rather than through host DI, because no host wires the example any more.
- Should not grow. If a case here matters going forward, port it to the plugin tests.

## Run

Run from the physical `Tests` directory so `Tests/global.json` selects Microsoft Testing Platform:

```powershell
dotnet test --project ParityBench.NET.ClientCustomerLookupExample.Tests\ParityBench.NET.ClientCustomerLookupExample.Tests.csproj -v:minimal
```
