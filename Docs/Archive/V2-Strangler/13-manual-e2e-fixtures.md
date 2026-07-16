# Slice 13: Manual E2E Fixtures

## Summary

This slice adds a V2-only fixture endpoint project and reusable request examples for manual end-to-end testing. The goal is to run the fixture server alongside the V2 Desktop app and exercise realistic XML/XML, JSON/JSON, and XML/JSON request comparison flows without referencing V1 projects.

## Fixture Project

`ParityBench.NET.TestEndpoints` is a standalone ASP.NET Core host under `Source`. It exposes two contract shapes with A/B route variants:

- SOAP/XML consumer report:
  - `POST /consumer-report/soap/a`
  - `POST /consumer-report/soap/b`
- JSON consumer report:
  - `POST /consumer-report/json/a`
  - `POST /consumer-report/json/b`
- Previous-slice sample customer lookup:
  - `POST /sample/customer-lookup/soap/a`
  - `POST /sample/customer-lookup/soap/b`
  - `POST /sample/customer-lookup/json/a`
  - `POST /sample/customer-lookup/json/b`

The project has no dependency on `ComparisonTool.*` V1 projects. It is intended to behave like an external bureau or consumer-report provider, while remaining deterministic enough for Playwright/E2E reuse.

## Manual Scenarios

Example request folders live under `Examples/ParityBench.NET.ManualRuns`.

- `xml-xml`: SOAP consumer report requests for same-contract XML comparison.
- `json-json`: JSON consumer report requests for same-contract JSON comparison.
- `xml-json`: SOAP customer lookup requests for the existing `sample-soap-to-json` contract profile.

`manual-run-catalog.json` records the request directory, endpoints, model name, optional profile id, suggested comparison toggles, rule text, and expected summary counts.

## Rule Coverage

The fixture covers these representative differences:

- Volatile metadata: report id, generated time, provider trace id, and processing time.
- Collection order differences.
- String case and trailing-whitespace differences.
- Null vs empty collection differences.
- Maskable sensitive identifiers with the same last four characters.
- Real score/risk differences that should remain different.
- XML-to-JSON contract-profile transformation and response normalization.

The V2 workflow UI includes manual rule inputs for ignore paths, smart ignores, and mask rules so these examples can be tested from Desktop/Web without dropping to code.

## Running Manually

Start the fixture endpoints:

```powershell
dotnet run --project Source\ParityBench.NET.TestEndpoints\ParityBench.NET.TestEndpoints.csproj --no-launch-profile --urls http://localhost:5056
```

Then start the V2 Desktop app and copy the run settings from `Examples\ParityBench.NET.ManualRuns\manual-run-catalog.json`.

## Non-Goals

- This slice does not replace `ComparisonTool.MockApi`.
- This slice does not switch V1 hosts or V1 E2E tests to V2.
- This slice does not add a new consumer-report cross-contract profile; XML/JSON uses the existing `sample-soap-to-json` profile.
