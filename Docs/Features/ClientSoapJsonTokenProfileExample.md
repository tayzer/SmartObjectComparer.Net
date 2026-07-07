# Client Setup Example: SOAP Endpoint A To JSON Endpoint B With Chained Tokens

This example shows how a client would set up one contract-profile comparison where:

- Endpoint A receives the uploaded SOAP XML request.
- Endpoint A requires `SOAPAction` and `Content-Type: text/xml`.
- Endpoint A returns SOAP XML.
- Endpoint B receives JSON mapped from the Endpoint A request model with Mapster.
- Endpoint B requires a subscription key header.
- Endpoint B also needs a bearer token created by two token clients.
- Each token client uses credentials extracted from the Endpoint A request and has its own subscription key header.
- Endpoint A and Endpoint B responses are normalized into the same JSON response model before comparison.

The important design choice is that the selected response model is the final canonical JSON response model, not the SOAP response envelope.

## Can This Be Driven From Appsettings?

Yes. Put environment-specific values in appsettings and keep behavior in the profile.

Use appsettings for:

- Endpoint A URL.
- Endpoint A static headers such as `SOAPAction` and `Content-Type: text/xml`.
- Endpoint B URL.
- Endpoint B static headers such as the endpoint subscription key.
- Token client 1 URL and subscription key.
- Token client 2 URL and subscription key.
- Optional timeout, retry, or client-specific HTTP settings.

Keep these in code or a registered client package:

- SOAP request and response models.
- Endpoint B JSON request and response models.
- Canonical JSON response model.
- Mapster mapping configuration.
- The contract profile ID and profile behavior.
- Token orchestration logic.

The profile links to appsettings by using stable endpoint IDs and an options section name. In this example:

| Profile value | Appsettings value |
|---|---|
| `suggestedEndpointAId: "client/customer-lookup/soap"` | configured Endpoint A ID or name |
| `suggestedEndpointBId: "client/customer-lookup/json"` | configured Endpoint B ID or name |
| `ClientCustomerLookup:Tokens` | token provider options section |

That lets the client deploy one profile implementation, edit appsettings per environment, select the profile, and run the tool.

## What The Client Selects In The Tool

Use these values in the run:

| Field | Value |
|---|---|
| Response model | `ClientCustomerLookupResponse` |
| Contract profile | `client.customer-lookup.soap-json.tokens.v1` |
| Endpoint A | SOAP endpoint |
| Endpoint B | JSON endpoint |

Endpoint A should be configured with static headers:

```json
{
  "SOAPAction": "urn:ClientCustomerLookup",
  "Content-Type": "text/xml"
}
```

Endpoint B should be configured with its endpoint subscription key:

```json
{
  "Ocp-Apim-Subscription-Key": "${secret:client.endpointB.subscriptionKey}"
}
```

The profile adds the final bearer token header dynamically:

```json
{
  "Authorization": "Bearer ${auth.finalToken.access_token}"
}
```

## Models

Keep the SOAP, JSON, token, and canonical response models separate. The canonical response model is the one the comparison engine diffs.

```csharp
using System.Text.Json.Serialization;
using System.Xml.Serialization;

[XmlRoot("Envelope")]
public sealed class ClientSoapRequestEnvelope
{
    public ClientSoapRequestBody Body { get; set; } = new();
}

public sealed class ClientSoapRequestBody
{
    public ClientLookupRequest LookupRequest { get; set; } = new();
}

public sealed class ClientLookupRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

[XmlRoot("Envelope")]
public sealed class ClientSoapResponseEnvelope
{
    public ClientSoapResponseBody Body { get; set; } = new();
}

public sealed class ClientSoapResponseBody
{
    public ClientLookupSoapResponse LookupResponse { get; set; } = new();
}

public sealed class ClientLookupSoapResponse
{
    public string StatusCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
}

public sealed class ClientEndpointBRequest
{
    [JsonPropertyName("customerId")]
    public string CustomerId { get; init; } = string.Empty;

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed class ClientEndpointBResponse
{
    [JsonPropertyName("resultCode")]
    public string ResultCode { get; init; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; init; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;
}

public sealed class ClientCustomerLookupResponse
{
    [JsonPropertyName("resultCode")]
    public string ResultCode { get; init; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; init; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;
}
```

## Mapster Mapping

The client can keep Mapster inside their implementation project. Application and Domain contracts should not depend on Mapster directly.

```csharp
using Mapster;

public static class ClientCustomerLookupMapsterConfig
{
    public static void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ClientSoapRequestEnvelope, ClientEndpointBRequest>()
            .Map(dest => dest.CustomerId, src => src.Body.LookupRequest.CustomerId)
            .Map(dest => dest.CorrelationId, src => src.Body.LookupRequest.CorrelationId);

        config.NewConfig<ClientSoapResponseEnvelope, ClientCustomerLookupResponse>()
            .Map(dest => dest.ResultCode, src => src.Body.LookupResponse.StatusCode)
            .Map(dest => dest.CustomerName, src => src.Body.LookupResponse.CustomerName)
            .Map(dest => dest.TraceId, src => src.Body.LookupResponse.TraceId);

        config.NewConfig<ClientEndpointBResponse, ClientCustomerLookupResponse>();
    }
}
```

## Token Clients

The first token call uses credentials extracted from the SOAP request. The second token call uses the first token output. Each token client has its own subscription key header.

```csharp
public sealed class ClientTokenOptions
{
    public string TokenClient1Url { get; init; } = string.Empty;
    public string TokenClient1SubscriptionKey { get; init; } = string.Empty;
    public string TokenClient2Url { get; init; } = string.Empty;
    public string TokenClient2SubscriptionKey { get; init; } = string.Empty;
}

public sealed class ClientTokenResult
{
    public string AccessToken { get; init; } = string.Empty;
}

public interface IClientTokenProvider
{
    Task<ClientTokenResult> GetFinalTokenAsync(
        ClientLookupRequest request,
        CancellationToken cancellationToken);
}

public sealed class ClientTokenProvider : IClientTokenProvider
{
    private readonly HttpClient httpClient;
    private readonly ClientTokenOptions options;

    public ClientTokenProvider(HttpClient httpClient, IOptions<ClientTokenOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<ClientTokenResult> GetFinalTokenAsync(
        ClientLookupRequest request,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage firstTokenRequest = new(HttpMethod.Post, options.TokenClient1Url)
        {
            Content = JsonContent.Create(new
            {
                username = request.UserName,
                password = request.Password
            })
        };
        firstTokenRequest.Headers.Add("Ocp-Apim-Subscription-Key", options.TokenClient1SubscriptionKey);

        TokenClient1Response firstToken = await SendTokenAsync<TokenClient1Response>(
            firstTokenRequest,
            cancellationToken);

        using HttpRequestMessage finalTokenRequest = new(HttpMethod.Post, options.TokenClient2Url)
        {
            Content = JsonContent.Create(new
            {
                subject_token = firstToken.AccessToken,
                customer_id = request.CustomerId
            })
        };
        finalTokenRequest.Headers.Add("Ocp-Apim-Subscription-Key", options.TokenClient2SubscriptionKey);

        TokenClient2Response finalToken = await SendTokenAsync<TokenClient2Response>(
            finalTokenRequest,
            cancellationToken);

        return new ClientTokenResult
        {
            AccessToken = finalToken.AccessToken
        };
    }

    private async Task<T> SendTokenAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Token response body was empty.");
    }

    private sealed class TokenClient1Response
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }

    private sealed class TokenClient2Response
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }
}
```

## Contract Profile

The profile passes Endpoint A through unchanged, maps Endpoint B's JSON request with Mapster, obtains the final token, then normalizes both responses into `ClientCustomerLookupResponse`.

```csharp
using MapsterMapper;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Infrastructure;

public static class ClientCustomerLookupProfile
{
    public const string ResponseModelName = "ClientCustomerLookupResponse";
    public const string ProfileId = "client.customer-lookup.soap-json.tokens.v1";

    public static IContractProfile Create(
        IContractPayloadSerializer serializer,
        IClientTokenProvider tokenProvider,
        IMapper mapper)
    {
        ContractPayloadFactory payloadFactory = new();

        return new ContractProfile<
            ClientSoapRequestEnvelope,
            ClientEndpointBRequest,
            ClientCustomerLookupResponse,
            ClientEndpointBResponse>(
            serializer,
            ProfileId,
            ResponseModelName,
            request => mapper.Map<ClientEndpointBRequest>(request),
            response => mapper.Map<ClientCustomerLookupResponse>(response),
            supportedSourceRequestFormats: new[] { PayloadFormat.Xml },
            endpointBRequestFormat: PayloadFormat.Json,
            endpointBRequestContentType: "application/json",
            endpointBResponseFormat: PayloadFormat.Json,
            canonicalResponseFormat: PayloadFormat.Json,
            canonicalResponseContentType: "application/json",
            suggestedEndpointAId: "client/customer-lookup/soap",
            suggestedEndpointBId: "client/customer-lookup/json",
            defaultIgnoreRules: new[]
            {
                new IgnoreRuleDefinition("traceId")
            },
            requestPreparation: async (context, cancellationToken) =>
            {
                ClientTokenResult finalToken = await tokenProvider.GetFinalTokenAsync(
                    context.SourceRequest.Body.LookupRequest,
                    cancellationToken);

                ClientEndpointBRequest endpointBRequest =
                    mapper.Map<ClientEndpointBRequest>(context.SourceRequest);

                ContractPayload body = await payloadFactory.CreateAsync(
                    PayloadFormat.Json,
                    "application/json",
                    (destination, token) => serializer.SerializeAsync(
                        endpointBRequest,
                        typeof(ClientEndpointBRequest),
                        PayloadFormat.Json,
                        destination,
                        token),
                    cancellationToken);

                return new PreparedContractRequest(
                    body,
                    ProfileId,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Authorization"] = $"Bearer {finalToken.AccessToken}"
                    });
            },
            endpointAResponseNormalizer: async (context, cancellationToken) =>
            {
                await using Stream responseBody = await context.OpenSourceResponseBodyAsync(cancellationToken);

                ClientSoapResponseEnvelope soapResponse =
                    (ClientSoapResponseEnvelope)await serializer.DeserializeAsync(
                        typeof(ClientSoapResponseEnvelope),
                        responseBody,
                        PayloadFormat.Xml,
                        ignoreXmlNamespaces: true,
                        cancellationToken);

                ClientCustomerLookupResponse canonical =
                    mapper.Map<ClientCustomerLookupResponse>(soapResponse);

                ContractPayload body = await payloadFactory.CreateAsync(
                    PayloadFormat.Json,
                    "application/json",
                    (destination, token) => serializer.SerializeAsync(
                        canonical,
                        typeof(ClientCustomerLookupResponse),
                        PayloadFormat.Json,
                        destination,
                        token),
                    cancellationToken);

                return new NormalizedContractResponse(body, ProfileId);
            },
            payloadFactory: payloadFactory);
    }
}
```

## DI Checklist

In addition to appsettings, the host must register the client profile components with DI:

1. Bind token/client options from `ClientCustomerLookup:Tokens`.
2. Register Mapster configuration and `IMapper`.
3. Register the token provider and its `HttpClient`.
4. Register `ClientCustomerLookupResponse` in `IResponseModelRegistry`.
5. Register the contract profile in `IContractProfileRegistry`.
6. Register endpoint options from appsettings if the host does not already do this.
7. Keep built-in models and profiles when adding the client registrations.

## Host Registration

Register the canonical response model, Mapster mappings, token provider, and contract profile in the host composition root.
```csharp
using Mapster;
using MapsterMapper;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Infrastructure;

services.Configure<ClientTokenOptions>(
    configuration.GetSection("ClientCustomerLookup:Tokens"));

TypeAdapterConfig mapsterConfig = TypeAdapterConfig.GlobalSettings;
ClientCustomerLookupMapsterConfig.Register(mapsterConfig);

services.AddSingleton(mapsterConfig);
services.AddSingleton<IMapper, ServiceMapper>();

services.AddHttpClient<IClientTokenProvider, ClientTokenProvider>();

services.AddSingleton<IResponseModelRegistry>(serviceProvider =>
{
    ResponseModelRegistry registry = new();
    registry.Register<ClientCustomerLookupResponse>(
        ClientCustomerLookupProfile.ResponseModelName);
    return registry;
});

services.AddSingleton<IContractProfileRegistry>(serviceProvider =>
{
    ContractProfileRegistry registry = new();
    registry.Register(ClientCustomerLookupProfile.Create(
        serviceProvider.GetRequiredService<IContractPayloadSerializer>(),
        serviceProvider.GetRequiredService<IClientTokenProvider>(),
        serviceProvider.GetRequiredService<IMapper>()));

    return registry;
});
```

If the host already registers built-in response models or profiles, add the client model and profile to the existing registry instead of replacing it.

For an appsettings-first setup, the host should also load endpoint options from configuration into the endpoint registry. Conceptually:

```csharp
services.AddSingleton<IRequestComparisonEndpointRegistry>(serviceProvider =>
{
    InMemoryRequestComparisonEndpointRegistry registry = new();

    foreach (ClientEndpointOption endpoint in configuration
        .GetSection("RequestComparison:EndpointOptions:Endpoints")
        .Get<List<ClientEndpointOption>>() ?? new())
    {
        registry.Register(new EndpointOption(
            endpoint.Id,
            endpoint.Name,
            new Uri(endpoint.Url)));
    }

    return registry;
});
```

The endpoint registry provides selectable endpoint URLs. Header defaults are applied by the run setup path in the current request-comparison host. If you are wiring the newer V2 host directly, make sure the same appsettings endpoint headers are carried into the run request or endpoint definition before execution.

## Configuration

Keep secrets outside the profile and outside committed appsettings files. The example below shows the shape only. Use environment variables, user secrets, Key Vault, or the client's existing secret provider for the actual subscription keys.

```json
{
  "ClientCustomerLookup": {
    "Tokens": {
      "TokenClient1Url": "https://token-client-1.example.com/oauth/token",
      "TokenClient1SubscriptionKey": "<from secret store>",
      "TokenClient2Url": "https://token-client-2.example.com/oauth/token",
      "TokenClient2SubscriptionKey": "<from secret store>"
    }
  }
}
```

Endpoint configuration should carry endpoint-specific URLs and static headers. The profile's suggested endpoint IDs should match these configured endpoint IDs or names.

```json
{
  "RequestComparison": {
    "EndpointOptions": {
      "AllowCustom": true,
      "Endpoints": [
        {
          "Id": "client/customer-lookup/soap",
          "Name": "Client Customer Lookup SOAP",
          "Url": "https://endpoint-a.example.com/customerLookup",
          "ContentType": "text/xml",
          "DefaultHeaders": {
            "SOAPAction": "urn:ClientCustomerLookup"
          }
        },
        {
          "Id": "client/customer-lookup/json",
          "Name": "Client Customer Lookup JSON",
          "Url": "https://endpoint-b.example.com/customer-lookup",
          "ContentType": "application/json",
          "DefaultHeaders": {
            "Ocp-Apim-Subscription-Key": "<from secret store>"
          }
        }
      ]
    },
    "Profiles": {
      "ClientCustomerLookup": {
        "ResponseModel": "ClientCustomerLookupResponse",
        "ProfileId": "client.customer-lookup.soap-json.tokens.v1",
        "EndpointAId": "client/customer-lookup/soap",
        "EndpointBId": "client/customer-lookup/json"
      }
    }
  }
}
```

The `Profiles` section is optional unless your host wants to preselect or validate a named client setup. The contract profile itself still needs to be registered in code so the tool has the Mapster mapping and token orchestration behavior.

## Expected Runtime Flow

1. The uploaded SOAP request is sent to Endpoint A with `SOAPAction` and `Content-Type: text/xml`.
2. The same SOAP request is deserialized into `ClientSoapRequestEnvelope`.
3. Credentials from `ClientSoapRequestEnvelope.Body.LookupRequest` are sent to token client 1 with token-client-1's subscription key.
4. Token client 1's token is sent to token client 2 with token-client-2's subscription key.
5. The final token is added to Endpoint B as `Authorization: Bearer ...`.
6. The Endpoint B JSON request body is produced from the SOAP request model using Mapster.
7. Endpoint B is called with `Content-Type: application/json`, its endpoint subscription key, and the final bearer token.
8. Endpoint A's SOAP response is mapped to `ClientCustomerLookupResponse`.
9. Endpoint B's JSON response is mapped to `ClientCustomerLookupResponse`.
10. The tool compares the two canonical JSON artifacts.

## Validation Checklist

- `ClientCustomerLookupResponse` appears in the response-model selector.
- `client.customer-lookup.soap-json.tokens.v1` appears for that response model.
- Endpoint A requests include `SOAPAction` and `Content-Type: text/xml`.
- Endpoint B requests do not include `SOAPAction`.
- Endpoint B requests include the endpoint subscription key.
- Token client 1 and token client 2 each receive their own subscription key header.
- Endpoint B receives `Authorization: Bearer <final token>`.
- The stored canonical artifacts for both endpoints are JSON in the `ClientCustomerLookupResponse` shape.
