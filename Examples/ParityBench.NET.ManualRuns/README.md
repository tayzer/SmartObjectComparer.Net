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

Expected with suggested rules: three equal pairs and one different pair.

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

Expected with suggested rules: four equal pairs and one different pair.

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
