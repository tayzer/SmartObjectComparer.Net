# Client Runbook: SOAP To JSON Customer Lookup Comparison

This guide is for the exact request-comparison flow that is already built into this repository.

Use this runbook when your client scenario matches all of the following:

- The uploaded request file is SOAP/XML.
- Endpoint A accepts that SOAP/XML request as-is.
- Before calling endpoint B, the tool must call a token service with `customerId` and `authenticationToken`.
- The token service returns two tokens, but only `authorizationToken` is used for an endpoint B header.
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

This is the exact implemented flow, matching your original requirement:

1. You upload SOAP XML requests.
2. Endpoint A receives the SOAP XML request as-is.
3. Before endpoint B is called, the tool maps the SOAP request into a token-service request.
4. The token service returns two tokens.
5. The tool uses only `authorizationToken` and sends it as an endpoint B header.
6. The tool builds the JSON request body for endpoint B.
7. Endpoint A returns SOAP XML.
8. The tool maps endpoint A's SOAP XML response into endpoint B's JSON response model.
9. Endpoint B returns JSON and is deserialized into that same JSON response model.
10. The tool compares the two normalized JSON results using predefined ignore rules plus any job-level rules you add.
11. Raw comparison support is still preserved for request-comparison results.

The built-in profile does all of the following automatically:

1. Deserializes the uploaded SOAP request.
2. Calls the token service with `customerId` and `authenticationToken`.
3. Uses only `authorizationToken` from the token-service response.
4. Sends `authorizationToken` as the `AuthorizationToken` header on the endpoint B request.
5. Builds the JSON request for endpoint B.
6. Deserializes endpoint B's JSON response.
7. Normalizes endpoint A's SOAP response into the same JSON comparison shape.
8. Ignores `ExpectedJsonCustomerLookupResponse.SourceSystem` by default.

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

### Endpoint B headers sent by the tool

The built-in profile adds this header to endpoint B:

```http
AuthorizationToken: AUTHZ-1001
```

Any other endpoint B headers you configure in the UI or request metadata are still sent as well.

### Endpoint B JSON request sent by the tool

```json
{
  "lookupId": "1001"
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

This ignore rule is built into the profile. You do not have to add it manually in the UI.

If any property names, element names, or nesting differ from the shapes above, stop here and update the code before you install this on the client machine.

---

## Step 1A: If the client contract differs, create a client-specific profile

If Step 1 does not match the client's real contract, the fix is not in `appsettings.json`. You need a client-specific alternate-contract implementation.

For this exact pattern, the customization work is always in four places:

1. Add the client's domain models.
2. Add a client-specific registration/profile class.
3. Wire that class into the built-in host registration path.
4. Add one focused integration or mock validation path.

The easiest approach is to copy the built-in implementation and rename it for the client.

Use these files as the starting point:

- [ComparisonTool.Domain/Models/RequestComparisonExpectedJsonCustomerLookupModels.cs](../ComparisonTool.Domain/Models/RequestComparisonExpectedJsonCustomerLookupModels.cs)
- [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs)
- [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractBuiltInRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractBuiltInRegistration.cs)

### 1A.1 Create the client's domain model file

Add a new file under `ComparisonTool.Domain/Models/`.

Example file name:

- `ComparisonTool.Domain/Models/ClientCustomerLookupModels.cs`

For this pattern, define these model groups:

1. SOAP request model used for uploaded requests and endpoint A.
2. SOAP response model used to read endpoint A's response.
3. Token-service request and response models.
4. Endpoint B JSON request model.
5. Endpoint B JSON response model.
6. Final normalized comparison model.

Minimal skeleton:

```csharp
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace ComparisonTool.Domain.Models;

[XmlRoot("Envelope")]
public class ClientSoapRequestEnvelope
{
  public ClientSoapRequestBody Body { get; set; } = new();
}

public class ClientSoapRequestBody
{
  public ClientSoapRequest CustomerLookupRequest { get; set; } = new();
}

public class ClientSoapRequest
{
  public string CustomerId { get; set; } = string.Empty;

  public string AuthenticationToken { get; set; } = string.Empty;
}

[XmlRoot("Envelope")]
public class ClientSoapResponseEnvelope
{
  public ClientSoapResponseBody Body { get; set; } = new();
}

public class ClientSoapResponseBody
{
  public ClientSoapResponse CustomerLookupResponse { get; set; } = new();
}

public class ClientSoapResponse
{
  public string StatusCode { get; set; } = string.Empty;

  public string CustomerName { get; set; } = string.Empty;

  public string TraceId { get; set; } = string.Empty;
}

public class ClientAuthorizationTokenRequest
{
  [JsonPropertyName("customerId")]
  public string CustomerId { get; set; } = string.Empty;

  [JsonPropertyName("authenticationToken")]
  public string AuthenticationToken { get; set; } = string.Empty;
}

public class ClientAuthorizationTokenResponse
{
  [JsonPropertyName("authorizationToken")]
  public string AuthorizationToken { get; set; } = string.Empty;

  [JsonPropertyName("backupAuthorizationToken")]
  public string BackupAuthorizationToken { get; set; } = string.Empty;
}

public class ClientAlternateRequest
{
  [JsonPropertyName("lookupId")]
  public string LookupId { get; set; } = string.Empty;
}

public class ClientAlternateResponse
{
  [JsonPropertyName("resultCode")]
  public string ResultCode { get; set; } = string.Empty;

  [JsonPropertyName("customerName")]
  public string CustomerName { get; set; } = string.Empty;

  [JsonPropertyName("traceId")]
  public string TraceId { get; set; } = string.Empty;

  [JsonPropertyName("sourceSystem")]
  public string SourceSystem { get; set; } = string.Empty;
}

public class ClientExpectedResponse
{
  [JsonPropertyName("resultCode")]
  public string ResultCode { get; set; } = string.Empty;

  [JsonPropertyName("customerName")]
  public string CustomerName { get; set; } = string.Empty;

  [JsonPropertyName("traceId")]
  public string TraceId { get; set; } = string.Empty;

  [JsonPropertyName("sourceSystem")]
  public string SourceSystem { get; set; } = string.Empty;
}
```

Replace the property names and nesting with the real client contract. The built-in sample only works unchanged if those wire names are exact.

### 1A.2 Create the client-specific registration/profile class

Add a new file under `ComparisonTool.Core/RequestComparison/AlternateContracts/`.

Example file name:

- `ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonClientCustomerLookupRegistration.cs`

This class is the real implementation point. It does three jobs:

1. Registers the final JSON comparison model for the UI.
2. Configures the token-service dependency.
3. Defines how SOAP is transformed into endpoint B JSON and how endpoint A SOAP is normalized back into the JSON comparison model.

Minimal skeleton:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Serialization;
using ComparisonTool.Core.DI;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ComparisonTool.Core.RequestComparison.AlternateContracts;

public static class RequestComparisonClientCustomerLookupRegistration
{
  internal static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    WriteIndented = false,
  };

  public const string ExpectedModelName = "ClientExpectedResponse";
  public const string ProfileId = "client-customer-lookup";

  public static void RegisterSharedComparisonModels(IServiceCollection services)
  {
    services.RegisterDomainModel<ClientExpectedResponse>(ExpectedModelName);
  }

  public static IServiceCollection AddClientCustomerLookupSupportServices(
    this IServiceCollection services,
    IConfiguration? configuration = null)
  {
    services.AddOptions<ClientCustomerLookupOptions>();

    if (configuration != null)
    {
      services.Configure<ClientCustomerLookupOptions>(
        configuration.GetSection(ClientCustomerLookupOptions.ConfigurationSectionName));
    }

    services.TryAddSingleton<IClientAuthorizationTokenService, HttpClientAuthorizationTokenService>();
    return services;
  }

  public static void RegisterProfiles(RequestComparisonAlternateContractOptions options)
  {
    options.RegisterProfile<
      ClientSoapRequestEnvelope,
      ClientAlternateRequest,
      ClientExpectedResponse,
      ClientAlternateResponse>(
      canonicalModelName: ExpectedModelName,
      profileId: ProfileId,
      requestMapper: request => new ClientAlternateRequest
      {
        LookupId = request.Body.CustomerLookupRequest.CustomerId,
      },
      responseMapper: response => new ClientExpectedResponse
      {
        ResultCode = response.ResultCode,
        CustomerName = response.CustomerName,
        TraceId = response.TraceId,
        SourceSystem = response.SourceSystem,
      },
      configure: builder => builder
        .SupportSourceRequestFormats(SerializationFormat.Xml)
        .UseAlternateRequestFormat(SerializationFormat.Json, "application/json")
        .UseAlternateResponseFormat(SerializationFormat.Json)
        .UseCanonicalResponseFormat(SerializationFormat.Json, "application/json")
        .UseAlternateRequestPreparation(async (context, cancellationToken) =>
        {
          var tokenService = context.Services.GetRequiredService<IClientAuthorizationTokenService>();
          var tokens = await tokenService.GetAuthorizationTokensAsync(
            new ClientAuthorizationTokenRequest
            {
              CustomerId = context.CanonicalRequest.Body.CustomerLookupRequest.CustomerId,
              AuthenticationToken = context.CanonicalRequest.Body.CustomerLookupRequest.AuthenticationToken,
            },
            cancellationToken).ConfigureAwait(false);

          var outbound = new ClientAlternateRequest
          {
            LookupId = context.CanonicalRequest.Body.CustomerLookupRequest.CustomerId,
          };

          return new PreparedAlternateContractRequest(
            JsonSerializer.SerializeToUtf8Bytes(outbound, SerializerOptions),
            "application/json",
            SerializationFormat.Json,
            ProfileId,
            new Dictionary<string, string>
            {
              ["AuthorizationToken"] = tokens.AuthorizationToken,
            });
        })
        .UseEndpointAResponseNormalizer(async (context, cancellationToken) =>
        {
          await using var stream = File.OpenRead(context.ExecutionResult.ResponsePathA!);
          var serializer = new XmlSerializer(typeof(ClientSoapResponseEnvelope));
          var soapResponse = (ClientSoapResponseEnvelope?)serializer.Deserialize(stream)
            ?? throw new InvalidOperationException("Endpoint A SOAP response could not be deserialized.");

          var normalized = new ClientExpectedResponse
          {
            ResultCode = soapResponse.Body.CustomerLookupResponse.StatusCode,
            CustomerName = soapResponse.Body.CustomerLookupResponse.CustomerName,
            TraceId = soapResponse.Body.CustomerLookupResponse.TraceId,
            SourceSystem = "endpoint-a",
          };

          return new NormalizedAlternateContractResponse(
            JsonSerializer.SerializeToUtf8Bytes(normalized, SerializerOptions),
            SerializationFormat.Json,
            "application/json",
            null);
        })
        .AddDefaultIgnoreRule(new IgnoreRuleDto
        {
          PropertyPath = $"{ExpectedModelName}.SourceSystem",
          IgnoreCompletely = true,
        });
  }
}

public sealed class ClientCustomerLookupOptions
{
  public const string ConfigurationSectionName = "RequestComparison:AlternateContracts:ClientCustomerLookup";

  public string AuthorizationTokenUrl { get; set; } = string.Empty;

  public string HttpClientName { get; set; } = "RequestComparison";
}

public interface IClientAuthorizationTokenService
{
  Task<ClientAuthorizationTokenResponse> GetAuthorizationTokensAsync(
    ClientAuthorizationTokenRequest request,
    CancellationToken cancellationToken = default);
}

internal sealed class HttpClientAuthorizationTokenService : IClientAuthorizationTokenService
{
  private readonly IHttpClientFactory httpClientFactory;
  private readonly IOptions<ClientCustomerLookupOptions> options;

  public HttpClientAuthorizationTokenService(
    IHttpClientFactory httpClientFactory,
    IOptions<ClientCustomerLookupOptions> options)
  {
    this.httpClientFactory = httpClientFactory;
    this.options = options;
  }

  public async Task<ClientAuthorizationTokenResponse> GetAuthorizationTokensAsync(
    ClientAuthorizationTokenRequest request,
    CancellationToken cancellationToken = default)
  {
    using var client = httpClientFactory.CreateClient(options.Value.HttpClientName);
    using var response = await client.PostAsJsonAsync(
      options.Value.AuthorizationTokenUrl,
      request,
      RequestComparisonClientCustomerLookupRegistration.SerializerOptions,
      cancellationToken).ConfigureAwait(false);

    response.EnsureSuccessStatusCode();

    return await response.Content.ReadFromJsonAsync<ClientAuthorizationTokenResponse>(
      RequestComparisonClientCustomerLookupRegistration.SerializerOptions,
      cancellationToken).ConfigureAwait(false)
      ?? throw new InvalidOperationException("Authorization token service returned an empty payload.");
  }
}
```

What you change in this file:

- Replace every `Client...` model with the real client types.
- Map the real SOAP request fields into the real token-service request.
- Use the correct JSON request shape for endpoint B.
- Set the correct dynamic header name and value for endpoint B in `PreparedAlternateContractRequest`.
- Normalize the real SOAP response fields from endpoint A into the real expected JSON response model.
- Add or change default ignore rules to match the client's known safe differences.

### 1A.3 Wire the client registration into the built-in host registration path

Update [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractBuiltInRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractBuiltInRegistration.cs).

If the client flow compares as JSON, add the shared model registration:

```csharp
public static void RegisterSharedComparisonModels(IServiceCollection services)
{
  ArgumentNullException.ThrowIfNull(services);
  RequestComparisonExpectedJsonCustomerLookupRegistration.RegisterSharedComparisonModels(services);
  RequestComparisonClientCustomerLookupRegistration.RegisterSharedComparisonModels(services);
}
```

Then wire the support services and profile registration:

```csharp
public static IServiceCollection AddBuiltInRequestComparisonAlternateContracts(
  this IServiceCollection services,
  IConfiguration? configuration = null)
{
  ArgumentNullException.ThrowIfNull(services);

  services.AddSupportServices(configuration);
  services.AddClientCustomerLookupSupportServices(configuration);

  return services.AddRequestComparisonAlternateContractProfiles(options =>
  {
    RequestComparisonAlternateContractSampleRegistration.RegisterProfiles(options);
    RequestComparisonExpectedJsonCustomerLookupRegistration.RegisterProfiles(options);
    RequestComparisonClientCustomerLookupRegistration.RegisterProfiles(options);
  });
}
```

Important naming note:

- Do not name every new extension method `AddSupportServices`.
- Give the client-specific version a unique name such as `AddClientCustomerLookupSupportServices(...)` so you do not create ambiguous extension-method calls.

Header precedence note:

- Existing endpoint B headers from the UI or request metadata are still sent.
- Profile-generated headers returned from `PreparedAlternateContractRequest` override them only when the header names collide.

### 1A.4 Confirm the host startup already uses the shared helper

If the Web, CLI, or Desktop host already calls `RequestComparisonAlternateContractBuiltInRegistration`, you usually do not need additional startup changes after Step 1A.3.

Use these files to verify that:

- [ComparisonTool.Web/Program.cs](../ComparisonTool.Web/Program.cs)
- [ComparisonTool.Cli/Infrastructure/ServiceProviderFactory.cs](../ComparisonTool.Cli/Infrastructure/ServiceProviderFactory.cs)
- [ComparisonTool.Desktop/App.xaml.cs](../ComparisonTool.Desktop/App.xaml.cs)
- [ComparisonTool.Report/Program.cs](../ComparisonTool.Report/Program.cs)

If a host does not call the built-in helper, the client profile will not appear there.

### 1A.5 Add the client config section

Add a new configuration section that matches the options class you created.

Example:

```json
{
  "RequestComparison": {
    "AlternateContracts": {
      "ClientCustomerLookup": {
        "AuthorizationTokenUrl": "https://client-auth.example.com/api/token",
        "HttpClientName": "RequestComparison"
      }
    }
  }
}
```

The section name must match the `ConfigurationSectionName` constant in the options class.

### 1A.6 Add one focused test or mock path

Before installing on the client machine, prove the custom profile works with the real request/response shapes.

The fastest path is to copy and adapt the advanced integration test in:

- [ComparisonTool.Tests/Integration/RequestComparison/RequestComparisonAlternateContractIntegrationTests.cs](../ComparisonTool.Tests/Integration/RequestComparison/RequestComparisonAlternateContractIntegrationTests.cs)

For this exact scenario, make sure your test proves all of these:

1. Endpoint A receives SOAP as-is.
2. The token service is called with the correct request payload.
3. Only `authorizationToken` is forwarded to endpoint B as a header.
4. Endpoint A SOAP is normalized into the expected JSON response model.
5. Endpoint B JSON is deserialized into that same model.
6. The default ignore rules are applied.
7. Successful pairs preserve normalized artifacts for full-file/report viewing.
8. Non-success pairs still go through raw-text comparison.

### 1A.7 What is a mapper in this pattern?

For the client's exact use case, the implementation is usually split across three mapping points:

1. SOAP request to token-service request.
2. Token-service response plus SOAP request data to endpoint B JSON request and headers.
3. Endpoint A SOAP response to the final expected JSON response model.

That is why this pattern usually uses `UseAlternateRequestPreparation(...)` and `UseEndpointAResponseNormalizer(...)` instead of only a simple `IAlternateContractMapper<...>` implementation.

Use `IAlternateContractMapper<...>` alone only when endpoint B can be built directly from the SOAP request and no extra dependency or async preparation step is needed.

### 1A.8 Minimum file checklist for a custom client implementation

If you are adapting this flow for a real client, expect to touch at least these files:

- New domain models file under `ComparisonTool.Domain/Models/`
- New registration/profile file under `ComparisonTool.Core/RequestComparison/AlternateContracts/`
- [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractBuiltInRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonAlternateContractBuiltInRegistration.cs)
- One host `appsettings.json`
- One focused integration test or mock flow

Once those are in place, return to Step 2 and continue the deployment setup on the client machine.

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
- The built-in endpoint B profile can now generate dynamic per-request headers, such as `AuthorizationToken`.
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

You do not need to create the predefined ignore rule for `sourceSystem` in the UI. The profile applies it automatically.

You also do not need to manually type the `AuthorizationToken` header in the UI for this built-in flow. The profile generates it per request from the token-service response.

---

## Step 7A: Run the comparison from the CLI

The CLI uses the same model/profile identifiers as the UI. First confirm the deployed CLI can see the built-in pieces:

```powershell
comparisontool request-models
comparisontool request-profiles --model ExpectedJsonCustomerLookupResponse
comparisontool request-endpoints
```

If the endpoint options include the expected customer lookup endpoints, this is the shortest command:

```powershell
comparisontool request C:\client\requests `
  --model ExpectedJsonCustomerLookupResponse `
  --alternate-contract-profile expected-json-customer-lookup `
  --use-profile-endpoints `
  --header-b "X-Client-Correlation: customer-lookup-smoke" `
  --format Json Markdown `
  --output C:\client\reports
```

You can also pass endpoint names directly. Name matching is exact and case-insensitive:

```powershell
comparisontool request C:\client\requests `
  --model ExpectedJsonCustomerLookupResponse `
  --endpoint-a "Client Customer Lookup SOAP" `
  --endpoint-b "Client Customer Lookup JSON" `
  --alternate-contract-profile expected-json-customer-lookup `
  --format Json Markdown `
  --output C:\client\reports
```

Endpoint defaults from `RequestComparison:EndpointOptions` are applied automatically unless `--no-endpoint-defaults` is supplied. The profile still owns endpoint B's alternate JSON request content type and still generates the per-request `AuthorizationToken` header from the token-service response.

Per-request sidecars can add common and endpoint-specific headers without changing the request body:

```json
{
  "headers": {
    "X-Request-Id": "1001"
  },
  "headersA": {
    "X-Endpoint": "soap"
  },
  "headersB": {
    "X-Endpoint": "json"
  }
}
```

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
5. POST to endpoint B with the header:

```http
AuthorizationToken: ...
```

6. Send this JSON body to endpoint B:

```json
{
  "lookupId": "..."
}
```

7. Merge any other configured endpoint B custom headers with that request.
8. Call endpoint A with the original SOAP request.
9. Normalize endpoint A's SOAP response into this JSON shape:

```json
{
  "resultCode": "...",
  "customerName": "...",
  "traceId": "...",
  "sourceSystem": "endpoint-a"
}
```

10. Deserialize endpoint B's JSON response into this same JSON shape, typically with `sourceSystem` equal to `endpoint-b`.
11. Compare the two normalized JSON results.
12. Ignore `sourceSystem` automatically.

This is the point where the flow becomes "SOAP on A, JSON on B, but compare them as one shared JSON model."

---

## Step 9: Understand how ignore rules and raw comparison work

There are two comparison paths in this use case.

### Successful responses

When both endpoints return success responses:

1. Endpoint A SOAP is normalized into the JSON comparison model.
2. Endpoint B JSON is deserialized into the same JSON comparison model.
3. Structured comparison runs against those normalized JSON artifacts.
4. The predefined ignore rule for `sourceSystem` is applied automatically.
5. Any additional ignore rules you configure for the job are applied on top of that.

Important detail:

- The normalized JSON artifacts are kept on disk for the full-file view and report bundling.
- So you still have a raw side-by-side file experience, but for successful alternate-contract pairs it is the normalized comparison artifacts that are shown.

### Non-success responses

When the pair is a non-success HTTP result, such as status mismatches or both sides returning non-success:

1. The pipeline skips structured domain-model comparison for that pair.
2. The actual persisted response bodies are compared as raw text.
3. That means you still get raw comparison behavior like the XML flow for error cases.

In practice, that means:

- Success pairs compare normalized JSON artifacts.
- Non-success pairs compare raw SOAP/XML and raw JSON bodies.

---

## Step 10: Verify the result on the client machine

A successful setup should behave like this:

- The request executes against both endpoints.
- The token service is called once per request.
- Endpoint B receives `lookupId` in the body and `AuthorizationToken` in the headers.
- Successful comparisons are based on normalized JSON, not on raw SOAP text.
- `sourceSystem` does not produce a reported difference.
- Full-file and report views can still open the persisted comparison artifacts.

Use this checklist after the first run:

1. Pick a request with known good data.
2. Confirm endpoint A returned SOAP and endpoint B returned JSON.
3. Open the result details.
4. Confirm the comparison fields are `resultCode`, `customerName`, and `traceId`.
5. Confirm a difference in `sourceSystem` is not counted.
6. Open the full-file view and confirm the stored comparison artifacts are available.

For non-success responses:

- The request-comparison pipeline still captures HTTP status codes and raw bodies.
- That means failed requests can still be investigated through the raw-result path instead of only the normalized JSON path.

---

## Step 11: Troubleshoot the common failures

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
  "lookupId": "..."
}
```

Fix:

- If the client expects a different JSON shape, update the code before deployment.

### Endpoint B returns an error because the token header is wrong

Cause:

- The client expects the token in a header and the header name or value does not match.

Fix:

- Update the profile so `PreparedAlternateContractRequest` returns the correct header name and token value for the client contract.

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
- An expected success pair fell back to some other comparison path.

Fix:

- Re-run using `ExpectedJsonCustomerLookupResponse` and `expected-json-customer-lookup`.
- Confirm the profile-owned ignore rules are active by checking that the job used the built-in alternate-contract profile.

### The client expects raw SOAP-vs-JSON comparison for success responses

Cause:

- That is not how this built-in flow works.

What actually happens:

- Success responses are compared as normalized JSON artifacts.
- Raw comparison is preserved for non-success pairs and raw/full-file inspection.

If the client requires literal raw-body SOAP-vs-JSON comparison even for successful responses, that is a different requirement and would need a code change.

---

## Exact implementation references

If you need to verify the built-in behavior before going on site, these are the source files that define it:

- [ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs](../ComparisonTool.Core/RequestComparison/AlternateContracts/RequestComparisonExpectedJsonCustomerLookupRegistration.cs)
- [ComparisonTool.Domain/Models/RequestComparisonExpectedJsonCustomerLookupModels.cs](../ComparisonTool.Domain/Models/RequestComparisonExpectedJsonCustomerLookupModels.cs)
- [ComparisonTool.Web/appsettings.json](../ComparisonTool.Web/appsettings.json)
- [ComparisonTool.MockApi/Program.cs](../ComparisonTool.MockApi/Program.cs)

Those files are the source of truth for this runbook.
