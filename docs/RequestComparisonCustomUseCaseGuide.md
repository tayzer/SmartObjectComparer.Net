# Build Your Own Request Comparison Use Case

This guide is for ComparisonTool users who want to add a new request-comparison use case to the repository.

Use this guide when:

- Endpoint A already accepts the source request format you upload into the tool.
- Endpoint B uses a different contract, such as JSON instead of SOAP/XML.
- You want ComparisonTool to translate between those contracts and still produce one comparable result.

If you want the API reference for each registration method, read [RequestComparisonSetup.md](RequestComparisonSetup.md) as the companion reference. This guide is the task-oriented version.

---

## What you are building

Every custom use case has the same shape:

1. The tool uploads a source request file.
2. The source request is deserialized into a canonical request model.
3. Endpoint A receives that source contract directly.
4. Endpoint B receives an alternate request model.
5. Both responses are normalized into one comparison model.
6. ComparisonTool diffs the normalized outputs using ignore rules and report generation.

There are two common variants:

- Simple variant: endpoint B only needs a straight request mapper and response mapper.
- Advanced variant: endpoint B needs extra preparation, such as a token lookup, and endpoint A or endpoint B must be normalized into JSON before comparison.

---

## Quick checklist

Before writing code, decide these five things:

| Decision | Example |
|---|---|
| Source request format | SOAP XML uploaded by the user |
| Endpoint A contract | Same as uploaded SOAP XML |
| Endpoint B contract | JSON REST payload |
| Final comparison model | SOAP response model or normalized JSON model |
| Extra dependencies | Token service, custom serializer, default ignore rules |

If you cannot answer the final comparison model clearly, stop there first. Most integration mistakes in this feature come from registering the wrong model for the final diff.

---

## Pick the right pattern

### Pattern A: Straight mapper

Use this when:

- The uploaded request is already the canonical request for endpoint A.
- Endpoint B only needs a transformed request body.
- Endpoint B's response can be mapped back to the same XML comparison model used by endpoint A.

Copy this example first:

- [ComparisonTool.Domain/Models/RequestComparisonSampleAlternateContractModels.cs](../ComparisonTool.Domain/Models/RequestComparisonSampleAlternateContractModels.cs)
- [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractSampleRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractSampleRegistration.cs)

### Pattern B: Prepared request plus normalized comparison model

Use this when:

- Endpoint B needs data that is not present directly in the mapped request, such as a token lookup.
- Endpoint A and endpoint B should be compared as normalized JSON instead of as the raw SOAP response type.
- The profile owns default ignore rules for normalization-only fields.

Copy this example first:

- [ComparisonTool.Domain/Models/RequestComparisonExpectedJsonCustomerLookupModels.cs](../ComparisonTool.Domain/Models/RequestComparisonExpectedJsonCustomerLookupModels.cs)
- [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs)

---

## Files you usually add

For a new use case, you will usually touch these areas:

| Area | What goes there |
|---|---|
| `ComparisonTool.Domain/Models/` | Request, response, alternate-contract, and normalized comparison models |
| `ComparisonTool.Core/RequestComparison/AlternateContracts/` | Profile registration, mapper, and optional support service |
| Host startup files | Register the models and profile in Web, CLI, Desktop, and optionally Report |
| Host `appsettings.json` | Alternate-contract options and friendly local endpoints |
| `ComparisonTool.MockApi/` or tests | Mock endpoints or focused regression coverage |

---

## Step-by-step recipe

### Step 1: Define the source request model

This is the model used to deserialize the request files uploaded into Request Comparison.

Most teams start with a SOAP envelope in `ComparisonTool.Domain/Models/`.

Keep this question in mind:

- Is this model only for parsing the uploaded request?
- Or is this also the final comparison model?

Those are not always the same type.

### Step 2: Define the endpoint B models

Create the alternate request and alternate response models for endpoint B.

Use `JsonPropertyName` for JSON payloads. Keep these models limited to the external wire contract. Do not mix them with the final comparison shape unless they are truly identical.

### Step 3: Define the final comparison model

This is the model that the diff engine compares after normalization.

There are two valid choices:

- XML comparison model: use this when both sides should end up as the same SOAP/XML response type.
- JSON comparison model: use this when both sides should be normalized into a shared JSON shape before diffing.

Rule of thumb:

- If endpoint A's raw SOAP response is the comparison artifact, your comparison model is usually XML.
- If endpoint A and endpoint B both need to be projected into a business-friendly shape first, your comparison model is usually JSON.

### Step 4: Register the comparison model correctly

This is the most important setup rule.

If the final comparison model is XML, register it inside `AddUnifiedComparisonServices(...)`:

```csharp
services.AddUnifiedComparisonServices(configuration, options =>
{
    options.RegisterDomainModelWithRootElement<MySoapResponseEnvelope>(
        modelName: "MySoapResponseEnvelope",
        rootElementName: "Envelope");
});
```

If the final comparison model is JSON, register it on `IServiceCollection` instead:

```csharp
services.RegisterDomainModel<MyExpectedJsonResponse>("MyExpectedJsonResponse");
```

Why this matters:

- The model dropdown in Request Comparison comes from the registered comparison model names.
- JSON comparison models must be visible to the shared XML and JSON deserializer graph.
- Registering a JSON comparison model only inside `XmlComparisonOptions` is not enough.

### Step 5: Implement the alternate-contract profile

For the simple pattern, implement `IAlternateContractMapper<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>` and register the profile.

For the advanced pattern, use the extra builder hooks as needed:

- `UseCanonicalResponseFormat(...)`
- `UseAlternateRequestPreparation(...)`
- `UseEndpointAResponseNormalizer(...)`
- `AddDefaultIgnoreRule(...)` or `AddDefaultIgnoreRules(...)`

Use these hooks for cases like:

- Fetching a token before calling endpoint B.
- Serializing a custom JSON body instead of using the simple mapper output.
- Converting endpoint A's SOAP response into a normalized JSON comparison artifact.
- Ignoring fields such as `SourceSystem` that only exist because of normalization.

### Step 6: Wire the profile into each host

If the use case should be available everywhere, wire it into the shared built-in registration helper and let each host call the same helper.

The current repo pattern is:

```csharp
RequestComparisonAlternateContractBuiltInRegistration.RegisterSharedComparisonModels(services);

services.AddUnifiedComparisonServices(configuration, options =>
{
    options.RegisterDomainModelWithRootElement<SoapEnvelope>("SoapEnvelope", "Envelope");
    RequestComparisonAlternateContractBuiltInRegistration.RegisterXmlComparisonModels(options);
});

services.AddBuiltInRequestComparisonAlternateContracts(configuration);
```

Use these files as the wiring reference:

- [ComparisonTool.Web/Program.cs](../ComparisonTool.Web/Program.cs)
- [ComparisonTool.Cli/Infrastructure/ServiceProviderFactory.cs](../ComparisonTool.Cli/Infrastructure/ServiceProviderFactory.cs)
- [ComparisonTool.Desktop/App.xaml.cs](../ComparisonTool.Desktop/App.xaml.cs)
- [ComparisonTool.Report/Program.cs](../ComparisonTool.Report/Program.cs)

Use the Report host only when the report viewer must deserialize the new comparison model for bundled results.

### Step 7: Add configuration

If your profile needs support services, add an options section under `RequestComparison:AlternateContracts`.

Example:

```json
{
  "RequestComparison": {
    "AlternateContracts": {
      "MyUseCase": {
        "AuthorizationTokenUrl": "http://localhost:5055/api/mock/authorisation-token",
        "HttpClientName": "RequestComparison"
      }
    }
  }
}
```

If the use case should be easy to try locally, also add friendly mock endpoints to the host `EndpointOptions` list.

### Step 8: Add a local test path

Do not stop at registration. Add one way to prove the setup works.

Usually that means one or more of these:

- Mock API endpoints for the source flow and alternate flow.
- Focused integration tests covering the real profile registration.
- Report/raw-content regression tests if the comparison artifact is JSON.

The current repo examples are:

- [ComparisonTool.MockApi/Program.cs](../ComparisonTool.MockApi/Program.cs)
- [ComparisonTool.Tests/Integration/RequestComparison/RequestComparisonAlternateContractIntegrationTests.cs](../ComparisonTool.Tests/Integration/RequestComparison/RequestComparisonAlternateContractIntegrationTests.cs)
- [ComparisonTool.Tests/Unit/RequestComparison/RawContentServiceTests.cs](../ComparisonTool.Tests/Unit/RequestComparison/RawContentServiceTests.cs)
- [ComparisonTool.Tests/Unit/Cli/BlazorReportBundleBuilderTests.cs](../ComparisonTool.Tests/Unit/Cli/BlazorReportBundleBuilderTests.cs)

### Step 9: Verify it in the tool

After the host starts:

1. Open Request Comparison.
2. Confirm your comparison model appears in the model dropdown.
3. Enable alternate contract for endpoint B.
4. Confirm your profile appears in the profile dropdown.
5. Run a known request through both endpoints.
6. Open the raw or normalized artifact view and verify the stored comparison shape is what you intended.

---

## Minimal implementation order

If you want the fastest safe path, do the work in this order:

1. Add models.
2. Register the final comparison model.
3. Add a simple profile with direct request and response mapping.
4. Wire it into one host and one focused test.
5. Only then add advanced hooks such as token lookup or endpoint A normalization.

That order keeps the failure surface small. If the model never appears in the UI, fix registration before debugging any HTTP behavior.

---

## Common mistakes

### The profile does not appear in the UI

Check these first:

- The selected comparison model name exactly matches the profile's `canonicalModelName`.
- The profile was registered with `AddRequestComparisonAlternateContractProfiles(...)`.
- The host startup path actually calls the shared registration helper.

### The comparison model does not appear in the UI

Usually one of these is wrong:

- The JSON comparison model was never registered with `services.RegisterDomainModel<T>(...)`.
- The host was restarted without rebuilding.
- The model was registered in only one host, but you are testing another.

### Endpoint B needs a token or extra request data

Do not try to squeeze that into a simple synchronous mapper. Use `UseAlternateRequestPreparation(...)` and resolve the dependency from DI.

### The comparison result is using the wrong artifact format

If you expect JSON in reports and full-file views, the profile must call `UseCanonicalResponseFormat(SerializationFormat.Json, "application/json")` and provide the required normalization path.

### The report cannot deserialize the new result type

Make sure the Report host registers the same shared comparison models as the executable hosts.

---

## Copy-from examples in this repo

Start from the example closest to your use case instead of building from scratch.

| Need | Copy from |
|---|---|
| Straight SOAP-to-JSON mapping | [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractSampleRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractSampleRegistration.cs) |
| Normalized JSON comparison model | [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs) |
| Shared model registration helper | [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractBuiltInRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractBuiltInRegistration.cs) |
| Web host wiring | [ComparisonTool.Web/Program.cs](../ComparisonTool.Web/Program.cs) |
| CLI host wiring | [ComparisonTool.Cli/Infrastructure/ServiceProviderFactory.cs](../ComparisonTool.Cli/Infrastructure/ServiceProviderFactory.cs) |
| Desktop host wiring | [ComparisonTool.Desktop/App.xaml.cs](../ComparisonTool.Desktop/App.xaml.cs) |
| Report host wiring | [ComparisonTool.Report/Program.cs](../ComparisonTool.Report/Program.cs) |
| Local mock endpoints | [ComparisonTool.MockApi/Program.cs](../ComparisonTool.MockApi/Program.cs) |

---

## Final validation checklist

Before calling the use case complete, confirm all of these:

- Uploaded source requests deserialize successfully.
- Endpoint A and endpoint B both execute through the intended profile.
- The correct comparison model appears in the UI.
- The correct profile appears in the UI for that model.
- Ignore rules apply to the normalized output you actually compare.
- Reports and raw-content views can open the stored artifacts.
- At least one focused integration test or mock path proves the flow end to end.

If those checks pass, the use case is wired correctly.