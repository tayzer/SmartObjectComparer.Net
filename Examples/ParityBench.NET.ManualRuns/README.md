# ParityBench.NET Manual Runs

Run the fixture endpoints:

```powershell
dotnet run --project Source\ParityBench.NET.TestEndpoints\ParityBench.NET.TestEndpoints.csproj --no-launch-profile --urls http://localhost:5056
```

Then start the V2 Desktop app and use the scenarios in `manual-run-catalog.json`.

## XML/XML Consumer Report

- Request directory: `Examples\ParityBench.NET.ManualRuns\xml-xml`
- Endpoint A: `http://localhost:5056/consumer-report/soap/a`
- Endpoint B: `http://localhost:5056/consumer-report/soap/b`
- Model: `ConsumerReportSoapResponseEnvelope`

Suggested options:

- Ignore collection order
- Ignore string case
- Ignore trailing whitespace
- Ignore XML namespaces
- Smart ignores:

```text
PropertyName=ReportId
PropertyName=GeneratedAt
PropertyName=ProviderTraceId
PropertyName=ProcessingMilliseconds
PropertyName=SourceSystem
```

- Mask rules:

```text
Body.ConsumerReportResponse.Subject.NationalIdentifier|preserveLast=4
```

Expected with suggested rules: three equal pairs and two different pairs, including a nested contact preference case.

## JSON/JSON Consumer Report

- Request directory: `Examples\ParityBench.NET.ManualRuns\json-json`
- Endpoint A: `http://localhost:5056/consumer-report/json/a`
- Endpoint B: `http://localhost:5056/consumer-report/json/b`
- Model: `ConsumerReportJsonResponse`

Suggested options:

- Ignore collection order
- Ignore string case
- Ignore trailing whitespace
- Null and empty collections equal
- Smart ignores:

```text
PropertyName=ReportId
PropertyName=GeneratedAt
PropertyName=ProviderTraceId
PropertyName=ProcessingMilliseconds
PropertyName=SourceSystem
```

- Mask rules:

```text
Subject.NationalIdentifier|preserveLast=4
```

Expected with suggested rules: four equal pairs and two different pairs, including a nested contact preference case.

## XML/JSON Sample Contract Profile

- Request directory: `Examples\ParityBench.NET.ManualRuns\xml-json`
- Endpoint A: `http://localhost:5056/sample/customer-lookup/soap/a`
- Endpoint B: `http://localhost:5056/sample/customer-lookup/json/b`
- Model: `SampleSoapCustomerLookupResponseEnvelope`
- Contract profile: `sample-soap-to-json`

Suggested mask rules:

```text
Body.CustomerLookupResponse.SensitiveToken|preserveLast=4
```

Expected with suggested rules: two equal pairs and one different pair.

## Client SOAP/JSON Token Customer Lookup (behaviors)

- Request directory: `Examples\ParityBench.NET.ManualRuns\client-soap-json-token\behaviors`
- Endpoint A: `http://localhost:5056/client/customer-lookup/soap`
- Endpoint B: `http://localhost:5056/client/customer-lookup/json`
- Model: `ClientCustomerLookupResponse`
- Contract profile: `client.customer-lookup.soap-json.tokens.v1`

Nine curated cases, one per comparison behavior exercised by `ClientCustomerLookupProfileFactory`'s baseline ignore rules:

- `01-exact-match` — fully identical responses.
- `02-ignored-fields-only` — `details.traceId`/`details.decisionEngine` differ but are ignored completely.
- `03-address-order-only` — `apps.profile.addresses` returned in a different order, same values; tolerated by `ignoreCollectionOrder` on that path.
- `04-triggered-checks-order-only` — `apps.ruleEvaluations.outcomes.triggeredChecks` reordered; tolerated the same way.
- `05-name-diff` — genuine difference: applicant full name.
- `06-city-diff` — genuine difference: mailing address city.
- `07-fraud-result-diff` — genuine difference: fraud rule outcome result.
- `08-flags-diff` — genuine difference: applicant flags.
- `09-combined-diff` — genuine difference: name, city, fraud result, and flags all differ together.

Expected with suggested rules: four equal pairs (01-04) and five different pairs (05-09).

## Client SOAP/JSON Token Customer Lookup (volume)

- Request directory: `Examples\ParityBench.NET.ManualRuns\client-soap-json-token\volume`
- Endpoint A: `http://localhost:5056/client/customer-lookup/soap`
- Endpoint B: `http://localhost:5056/client/customer-lookup/json`
- Model: `ClientCustomerLookupResponse`
- Contract profile: `client.customer-lookup.soap-json.tokens.v1`

1000 generated requests exercising the same 9 deterministic difference categories as the behaviors set, spread evenly (`CustomerId % 9`) to simulate a realistic client dataset with varied differences instead of one repeated diff. Regenerate via:

```powershell
dotnet run --project Source\ParityBench.NET.ManualRunFixtureGenerator -- --count 1000
```

Expected with suggested rules: 445 equal pairs and 555 different pairs.
