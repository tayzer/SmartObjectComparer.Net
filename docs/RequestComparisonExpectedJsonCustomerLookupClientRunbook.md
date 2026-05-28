# Client Runbook: SOAP To JSON Customer Lookup Comparison

This guide is for the exact request-comparison flow that is already built into this repository.

Use this runbook when your client scenario matches all of the following:

- The uploaded request file is SOAP/XML.
- Endpoint A accepts that SOAP/XML request as-is.
- Before calling endpoint B, the tool must call a token service with `customerId` and `authenticationToken`.
- The token service returns two tokens, but only `authorizationToken` is used for endpoint B.
- Endpoint B accepts a JSON request.
- Endpoint A's SOAP response and endpoint B's JSON response should be normalized into the same JSON comparison model before diffing.
- `sourceSystem` should be ignored during comparison.

If the client's contract shape differs from the shapes in this guide, do not use this runbook as-is. That means the built-in profile is close, but code changes are still required before deployment.

---

## What is already built in

This repository already contains a built-in request-comparison profile for this use case.

Use these exact identifiers in the UI:

- Comparison model: `ExpectedJsonCustomerLookupResponse`
- Alternate-contract profile: `expected-json-customer-lookup`

The built-in profile does all of the following automatically:

1. Deserializes the uploaded SOAP request.
2. Calls the token service with `customerId` and `authenticationToken`.
3. Uses only `authorizationToken` from the token-service response.
4. Builds the JSON request for endpoint B.
5. Deserializes endpoint B's JSON response.
6. Normalizes endpoint A's SOAP response into the same JSON comparison shape.
7. Ignores `ExpectedJsonCustomerLookupResponse.SourceSystem` by default.

---

## Step 1: Confirm the client contract matches the built-in profile

Before deploying anything, verify the client endpoints match these payload shapes.

### Uploaded SOAP request shape

```xml
<Envelope>
  <Body>
    <CustomerLookupRequest>
      <CustomerId>1001</CustomerId>
      <AuthenticationToken>AUTH-ABC-123</AuthenticationToken>
    </CustomerLookupRequest>
  </Body>
</Envelope>
```

### Token-service request sent by the tool

```json
{
  "customerId": "1001",
  "authenticationToken": "AUTH-ABC-123"
}
```

### Token-service response expected by the tool

```json
{
  "authorizationToken": "AUTHZ-1001",
  "backupAuthorizationToken": "BACKUP-1001"
}
```

Only `authorizationToken` is used when the tool calls endpoint B.

### Endpoint B JSON request sent by the tool

```json
{
  "lookupId": "1001",
  "authorizationToken": "AUTHZ-1001"
}
```

### Endpoint A SOAP response expected by the tool

```xml
<Envelope>
  <Body>
    <CustomerLookupResponse>
      <StatusCode>00</StatusCode>
      <CustomerName>Alpha</CustomerName>
      <TraceId>trace-1001</TraceId>
    </CustomerLookupResponse>
  </Body>
</Envelope>
```

### Endpoint B JSON response expected by the tool

```json
{
  "resultCode": "00",
  "customerName": "Alpha",
  "traceId": "trace-1001",
  "sourceSystem": "endpoint-b"
}
```

### Final normalized comparison shape

Both endpoints are compared as this JSON model:

```json
{
  "resultCode": "00",
  "customerName": "Alpha",
  "traceId": "trace-1001",
  "sourceSystem": "endpoint-a or endpoint-b"
}
```

The built-in profile ignores `sourceSystem`, so a difference between `endpoint-a` and `endpoint-b` does not fail the comparison.

If any property names, element names, or nesting differ from the shapes above, stop here and update the code before you install this on the client machine.

---

## Step 2: Gather the client values you need

Have these values ready before you edit configuration:

- Endpoint A SOAP URL
- Endpoint B JSON URL
- Token-service URL
- Any required headers for endpoint A
- Any required headers for endpoint B
- Any required headers for the token-service call
- A sample SOAP request file that contains both `CustomerId` and `AuthenticationToken`
- Expected timeout if the default `30000` ms is too low

Important limitation:

- The built-in token-service call is configured by URL and named `HttpClient`, but it does not have a separate token-service header configuration section today.
- If the token service needs special HTTP headers beyond normal network access, that is a code change, not just a config change.

---

## Step 3: Install the correct build on the client machine

Use a build that already contains this implemented profile.

Before you publish or copy the application to the client machine, verify your source checkout contains these files:

- [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs)
- [ComparisonTool.Domain/Models/RequestComparisonExpectedJsonCustomerLookupModels.cs](../ComparisonTool.Domain/Models/RequestComparisonExpectedJsonCustomerLookupModels.cs)
- [ComparisonTool.Web/Program.cs](../ComparisonTool.Web/Program.cs)

If your source checkout does not contain those files, or the host was built before this feature landed, the model and profile will not appear in the UI.

---

## Step 4: Edit `appsettings.json` on the client machine

For the web host, update the deployed [ComparisonTool.Web/appsettings.json](../ComparisonTool.Web/appsettings.json) so the token-service URL and endpoint shortcuts point to the client environment.

Use this exact shape:

```json
{
  "RequestComparison": {
    "DefaultTimeoutMs": 30000,
    "MaxConcurrency": 64,
    "AlternateContracts": {
      "ExpectedJsonCustomerLookup": {
        "AuthorizationTokenUrl": "https://client-auth.example.com/api/authorisation-token",
        "HttpClientName": "RequestComparison"
      }
    },
    "EndpointOptions": {
      "AllowCustom": true,
      "Endpoints": [
        {
          "Name": "Client Customer Lookup SOAP",
          "Url": "https://client-soap.example.com/customer-lookup"
        },
        {
          "Name": "Client Customer Lookup JSON",
          "Url": "https://client-json.example.com/customer-lookup"
        }
      ]
    }
  }
}
```

Notes:

- Replace the sample URLs with the real client URLs.
- Leave `HttpClientName` as `RequestComparison` unless the host code was changed to use a different named client.
- If you are using the Desktop or CLI host instead of Web, apply the same `RequestComparison` section to that host's deployed `appsettings.json` file.
- You do not need to register the model or profile manually in config. They are already wired in code.

---

## Step 5: Start the host on the client machine

The built-in profile is wired into the Web, Desktop, and CLI hosts.

For a UI-driven client setup, the Web host is the most direct path.

1. Start the deployed web application.
2. Open the application in the browser.
3. Confirm the Request Comparison feature is enabled.

If the feature is hidden, check that `FeatureFlags:RequestComparisonEnabled` is set to `true` in the deployed configuration.

---

## Step 6: Prepare the request files you will upload

Each request file must be SOAP/XML and must contain:

- `CustomerId`
- `AuthenticationToken`

Use one file per request.

Minimal example:

```xml
<Envelope>
  <Body>
    <CustomerLookupRequest>
      <CustomerId>1001</CustomerId>
      <AuthenticationToken>AUTH-ABC-123</AuthenticationToken>
    </CustomerLookupRequest>
  </Body>
</Envelope>
```

The built-in request model is namespace-tolerant under the normal request-comparison setup, so standard SOAP namespaces are usually fine unless the host was changed to enforce strict XML namespaces.

---

## Step 7: Run the comparison in the UI

In the Request Comparison screen:

1. Select the comparison model `ExpectedJsonCustomerLookupResponse`.
2. Set endpoint A to the client's SOAP endpoint.
3. Set endpoint B to the client's JSON endpoint.
4. Enable alternate contract for endpoint B.
5. Select the profile `expected-json-customer-lookup`.
6. Add any required endpoint A and endpoint B headers in the UI.
7. Set timeout and concurrency if the defaults are not appropriate.
8. Upload the SOAP request files.
9. Run the comparison.

If the comparison model is missing, the deployed host does not include the correct build.

If the profile is missing but the model is present, make sure alternate contract is enabled and the selected model is `ExpectedJsonCustomerLookupResponse`.

---

## Step 8: Understand what the tool does for each request

For every uploaded SOAP request, the built-in profile performs this sequence:

1. Read `CustomerId` and `AuthenticationToken` from the SOAP request.
2. POST to the configured token-service URL with:

```json
{
  "customerId": "...",
  "authenticationToken": "..."
}
```

3. Read the token-service response.
4. Take only `authorizationToken`.
5. POST to endpoint B with:

```json
{
  "lookupId": "...",
  "authorizationToken": "..."
}
```

6. Call endpoint A with the original SOAP request.
7. Normalize endpoint A's SOAP response into this JSON shape:

```json
{
  "resultCode": "...",
  "customerName": "...",
  "traceId": "...",
  "sourceSystem": "endpoint-a"
}
```

8. Deserialize endpoint B's JSON response into this same JSON shape, typically with `sourceSystem` equal to `endpoint-b`.
9. Compare the two normalized JSON results.
10. Ignore `sourceSystem` automatically.

---

## Step 9: Verify the result on the client machine

A successful setup should behave like this:

- The request executes against both endpoints.
- The token service is called once per request.
- Endpoint B receives `lookupId` and `authorizationToken`.
- Successful comparisons are based on normalized JSON, not on raw SOAP text.
- `sourceSystem` does not produce a reported difference.

Use this checklist after the first run:

1. Pick a request with known good data.
2. Confirm endpoint A returned SOAP and endpoint B returned JSON.
3. Open the result details.
4. Confirm the comparison fields are `resultCode`, `customerName`, and `traceId`.
5. Confirm a difference in `sourceSystem` is not counted.

For non-success responses:

- The request-comparison pipeline still captures HTTP status codes and raw bodies.
- That means failed requests can still be investigated through the raw-result path instead of only the normalized JSON path.

---

## Step 10: Troubleshoot the common failures

### The model does not appear in the dropdown

Cause:

- The deployed host does not include the build with this profile.

Fix:

- Redeploy the host built from the current code.

### The profile does not appear in the dropdown

Cause:

- Alternate contract is not enabled.
- The selected model is not `ExpectedJsonCustomerLookupResponse`.

Fix:

- Select the expected model first, then enable alternate contract.

### The token call fails

Cause:

- `AuthorizationTokenUrl` is wrong.
- The client machine cannot reach the token endpoint.
- The token-service contract does not match the expected JSON shape.

Fix:

- Correct the URL.
- Verify network access from the client machine.
- Verify the response returns `authorizationToken` and `backupAuthorizationToken`.

### Endpoint B returns an error because the request body is wrong

Cause:

- Endpoint B expects something other than:

```json
{
  "lookupId": "...",
  "authorizationToken": "..."
}
```

Fix:

- If the client expects a different JSON shape, update the code before deployment.

### Endpoint A response cannot be normalized

Cause:

- The SOAP response does not match:
  - `Envelope`
  - `Body`
  - `CustomerLookupResponse`
  - `StatusCode`
  - `CustomerName`
  - `TraceId`

Fix:

- If the client response shape differs, update the SOAP response model and normalizer before deployment.

### The only visible difference is `sourceSystem`

Cause:

- The wrong model/profile was used, or the request did not go through the built-in normalized profile.

Fix:

- Re-run using `ExpectedJsonCustomerLookupResponse` and `expected-json-customer-lookup`.

---

## Exact implementation references

If you need to verify the built-in behavior before going on site, these are the source files that define it:

- [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs)
- [ComparisonTool.Domain/Models/RequestComparisonExpectedJsonCustomerLookupModels.cs](../ComparisonTool.Domain/Models/RequestComparisonExpectedJsonCustomerLookupModels.cs)
- [ComparisonTool.Web/appsettings.json](../ComparisonTool.Web/appsettings.json)
- [ComparisonTool.MockApi/Program.cs](../ComparisonTool.MockApi/Program.cs)

Those files are the source of truth for this runbook.