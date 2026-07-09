using System.Text;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

using ParityBench.NET.ClientCustomerLookupExample;

namespace ParityBench.NET.TestEndpoints.ClientCustomerLookup;

public static class ClientCustomerLookupEndpoints
{
    public const string SoapAction = "urn:ClientCustomerLookup";
    public const string EndpointBSubscriptionKey = "mock-endpoint-b-subscription-key";
    public const string PrimaryTokenSubscriptionKey = "mock-primary-token-subscription-key";
    public const string FinalTokenSubscriptionKey = "mock-final-token-subscription-key";
    public const string PrimaryToken = "mock-primary-token";
    public const string FinalToken = "mock-final-token";

    public static void MapClientCustomerLookupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/client");
        group.MapPost("/customer-lookup/soap", (Delegate)HandleSoapAsync);
        group.MapPost("/customer-lookup/json", (Delegate)HandleJsonAsync);
        group.MapPost("/token/primary", (Delegate)HandlePrimaryTokenAsync);
        group.MapPost("/token/final", (Delegate)HandleFinalTokenAsync);
    }

    private static async Task<IResult> HandleSoapAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("SOAPAction", out var soapAction)
            || !string.Equals(soapAction.ToString().Trim('"'), SoapAction, StringComparison.Ordinal))
        {
            return Results.Problem("Missing or invalid SOAPAction header.", statusCode: StatusCodes.Status400BadRequest);
        }

        ClientCustomerLookupSoapRequestEnvelope request = await DeserializeXmlAsync<ClientCustomerLookupSoapRequestEnvelope>(
            context.Request.Body,
            context.RequestAborted).ConfigureAwait(false);

        ClientCustomerLookupSoapResponseEnvelope response = new ClientCustomerLookupSoapResponseEnvelope
        {
            Body = new ClientCustomerLookupSoapResponseBody
            {
                LookupResponse = CreateSoapResponse(request.Body.LookupRequest),
            },
        };

        return Results.Text(SerializeXml(response), "text/xml", Encoding.UTF8);
    }

    private static async Task<IResult> HandleJsonAsync(HttpContext context)
    {
        if (!HasHeader(context, ClientCustomerLookupTokenProvider.SubscriptionKeyHeaderName, EndpointBSubscriptionKey))
        {
            return Results.Problem("Missing or invalid endpoint B subscription key.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!HasHeader(context, "Authorization", $"Bearer {FinalToken}"))
        {
            return Results.Problem("Missing or invalid bearer token.", statusCode: StatusCodes.Status401Unauthorized);
        }

        ClientCustomerLookupJsonRequest? request = await context.Request
            .ReadFromJsonAsync<ClientCustomerLookupJsonRequest>(cancellationToken: context.RequestAborted)
            .ConfigureAwait(false);
        if (request is null)
        {
            return Results.Problem("JSON request body was empty.", statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Json(new ClientCustomerLookupJsonResponse
        {
            Details = new ClientCustomerLookupDetails
            {
                ResultCode = "OK",
                TraceId = request.CorrelationId,
                DecisionEngine = "EndpointB-JSON-Service",
            },
            Applicants = new[]
            {
                CreateEndpointBApplicant(request.CustomerId, request.CorrelationId),
            },
            IsAThing = true,
        });
    }

    private static async Task<IResult> HandlePrimaryTokenAsync(HttpContext context)
    {
        if (!HasHeader(context, ClientCustomerLookupTokenProvider.SubscriptionKeyHeaderName, PrimaryTokenSubscriptionKey))
        {
            return Results.Problem("Missing or invalid primary token subscription key.", statusCode: StatusCodes.Status401Unauthorized);
        }

        PrimaryTokenRequest? request = await context.Request
            .ReadFromJsonAsync<PrimaryTokenRequest>(cancellationToken: context.RequestAborted)
            .ConfigureAwait(false);
        if (request is null || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Problem("Primary token request was invalid.", statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Json(new TokenResponse(PrimaryToken));
    }

    private static async Task<IResult> HandleFinalTokenAsync(HttpContext context)
    {
        if (!HasHeader(context, ClientCustomerLookupTokenProvider.SubscriptionKeyHeaderName, FinalTokenSubscriptionKey))
        {
            return Results.Problem("Missing or invalid final token subscription key.", statusCode: StatusCodes.Status401Unauthorized);
        }

        FinalTokenRequest? request = await context.Request
            .ReadFromJsonAsync<FinalTokenRequest>(cancellationToken: context.RequestAborted)
            .ConfigureAwait(false);
        if (request is null || !string.Equals(request.PrimaryToken, PrimaryToken, StringComparison.Ordinal))
        {
            return Results.Problem("Final token request was invalid.", statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Json(new TokenResponse(FinalToken));
    }

    private static ClientCustomerLookupSoapResponse CreateSoapResponse(ClientCustomerLookupRequest request) =>
        new ClientCustomerLookupSoapResponse
        {
            StatusCode = "OK",
            CustomerName = ResolveEndpointACustomerName(request.CustomerId),
            TraceId = request.CorrelationId,
        };

    private static string ResolveEndpointACustomerName(string customerId) => "Riley Morgan";

    private static string ResolveEndpointBCustomerName(string customerId) =>
        string.Equals(customerId, "2002", StringComparison.Ordinal) ? "Riley Morgan Updated" : "Riley Morgan";

    private static ClientCustomerLookupApplicant CreateEndpointBApplicant(string customerId, string correlationId) =>
        new ClientCustomerLookupApplicant
        {
            ApplicantId = correlationId,
            Profile = new ClientCustomerLookupApplicantProfile
            {
                FullName = ResolveEndpointBCustomerName(customerId),
                Addresses = new[]
                {
                    new ClientCustomerLookupAddress
                    {
                        Type = "HOME",
                        City = "Seattle",
                        Country = "US",
                    },
                    new ClientCustomerLookupAddress
                    {
                        Type = "MAILING",
                        City = string.Equals(customerId, "2002", StringComparison.Ordinal) ? "Bellevue" : "Tacoma",
                        Country = "US",
                    },
                },
            },
            RuleEvaluations = new[]
            {
                new ClientCustomerLookupRuleEvaluation
                {
                    RuleSet = "identity",
                    Outcomes = new[]
                    {
                        new ClientCustomerLookupRuleOutcome
                        {
                            Code = "ID_DOC_MATCH",
                            Result = "PASS",
                            TriggeredChecks = new[] { "name_match", "dob_match" },
                        },
                    },
                },
                new ClientCustomerLookupRuleEvaluation
                {
                    RuleSet = "fraud",
                    Outcomes = new[]
                    {
                        new ClientCustomerLookupRuleOutcome
                        {
                            Code = "DEVICE_RISK",
                            Result = string.Equals(customerId, "2002", StringComparison.Ordinal) ? "FAIL" : "REVIEW",
                            TriggeredChecks = new[] { "ip_velocity", "device_reuse" },
                        },
                    },
                },
            },
            Flags = string.Equals(customerId, "2002", StringComparison.Ordinal)
                ? new[] { "KYC_COMPLETE", "FRAUD_ALERT" }
                : new[] { "KYC_COMPLETE", "MANUAL_REVIEW_REQUIRED" },
        };

    private static bool HasHeader(HttpContext context, string name, string expectedValue) =>
        context.Request.Headers.TryGetValue(name, out var value)
        && string.Equals(value.ToString(), expectedValue, StringComparison.Ordinal);

    private static async Task<T> DeserializeXmlAsync<T>(Stream body, CancellationToken cancellationToken)
    {
        using StreamReader reader = new StreamReader(body, Encoding.UTF8);
        string xml = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        using StringReader stringReader = new StringReader(xml);
        return (T)new XmlSerializer(typeof(T)).Deserialize(stringReader)!;
    }

    private static string SerializeXml<T>(T value)
    {
        XmlSerializerNamespaces namespaces = new XmlSerializerNamespaces();
        namespaces.Add(string.Empty, string.Empty);
        using StringWriter writer = new StringWriter();
        new XmlSerializer(typeof(T)).Serialize(writer, value, namespaces);
        return writer.ToString();
    }

    private sealed record PrimaryTokenRequest(
        [property: JsonPropertyName("username")] string UserName,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("customerId")] string CustomerId);

    private sealed record FinalTokenRequest(
        [property: JsonPropertyName("primaryToken")] string PrimaryToken,
        [property: JsonPropertyName("customerId")] string CustomerId,
        [property: JsonPropertyName("correlationId")] string CorrelationId);

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
