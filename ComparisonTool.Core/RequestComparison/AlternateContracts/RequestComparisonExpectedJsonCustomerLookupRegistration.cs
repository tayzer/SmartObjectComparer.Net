using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Serialization;
using ComparisonTool.Core.DI;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ComparisonTool.Core.RequestComparison.AlternateContracts;

public static class RequestComparisonExpectedJsonCustomerLookupRegistration
{
    internal static readonly JsonSerializerOptions DefaultSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public const string ExpectedModelName = "ExpectedJsonCustomerLookupResponse";
    public const string ProfileId = "expected-json-customer-lookup";

    public static void RegisterSharedComparisonModels(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RegisterDomainModel<ExpectedJsonCustomerLookupResponse>(ExpectedModelName);
    }

    public static IServiceCollection AddSupportServices(this IServiceCollection services, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<ExpectedJsonCustomerLookupAlternateContractOptions>();

        if (configuration != null)
        {
            services.Configure<ExpectedJsonCustomerLookupAlternateContractOptions>(
                configuration.GetSection(ExpectedJsonCustomerLookupAlternateContractOptions.ConfigurationSectionName));
        }

        services.TryAddSingleton<IExpectedJsonCustomerLookupAuthorizationTokenService, HttpExpectedJsonCustomerLookupAuthorizationTokenService>();
        return services;
    }

    public static void RegisterProfiles(RequestComparisonAlternateContractOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RegisterProfile<
            ExpectedJsonCustomerLookupSoapRequestEnvelope,
            ExpectedJsonCustomerLookupAlternateRequest,
            ExpectedJsonCustomerLookupResponse,
            ExpectedJsonCustomerLookupAlternateResponse>(
            canonicalModelName: ExpectedModelName,
            profileId: ProfileId,
            requestMapper: request => new ExpectedJsonCustomerLookupAlternateRequest
            {
                LookupId = request.Body.CustomerLookupRequest.CustomerId,
            },
            responseMapper: response => new ExpectedJsonCustomerLookupResponse
            {
                ResultCode = response.ResultCode,
                CustomerName = response.CustomerName,
                TraceId = response.TraceId,
                SourceSystem = response.SourceSystem,
            },
            configure: builder => builder
                .SupportSourceRequestFormats(SerializationFormat.Xml)
                .UseAlternateRequestFormat(SerializationFormat.Json, "application/json")
                .SuggestEndpointA("customer-lookup/soap", "customer-lookup/json")
                .UseAlternateResponseFormat(SerializationFormat.Json)
                .UseCanonicalResponseFormat(SerializationFormat.Json, "application/json")
                .UseAlternateRequestPreparation(async (context, cancellationToken) =>
                {
                    var tokenService = context.Services.GetRequiredService<IExpectedJsonCustomerLookupAuthorizationTokenService>();
                    var tokens = await tokenService.GetAuthorizationTokensAsync(
                        new ExpectedJsonCustomerLookupAuthorizationTokenRequest
                        {
                            CustomerId = context.CanonicalRequest.Body.CustomerLookupRequest.CustomerId,
                            AuthenticationToken = context.CanonicalRequest.Body.CustomerLookupRequest.AuthenticationToken,
                        },
                        cancellationToken).ConfigureAwait(false);

                    var outboundRequest = new ExpectedJsonCustomerLookupAlternateRequest
                    {
                        LookupId = context.CanonicalRequest.Body.CustomerLookupRequest.CustomerId,
                    };

                    return new PreparedAlternateContractRequest(
                        JsonSerializer.SerializeToUtf8Bytes(outboundRequest, DefaultSerializerOptions),
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
                    ArgumentNullException.ThrowIfNull(context.ExecutionResult.ResponsePathA);

                    cancellationToken.ThrowIfCancellationRequested();

                    ExpectedJsonCustomerLookupSoapResponseEnvelope soapResponse;
                    try
                    {
                        await using var stream = File.OpenRead(context.ExecutionResult.ResponsePathA);
                        var serializer = new XmlSerializer(typeof(ExpectedJsonCustomerLookupSoapResponseEnvelope));
                        soapResponse = (ExpectedJsonCustomerLookupSoapResponseEnvelope?)serializer.Deserialize(stream)
                            ?? throw new InvalidOperationException("Endpoint A SOAP response could not be deserialized.");
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException(
                            "Endpoint A returned a response that is not the expected Customer Lookup SOAP contract. " +
                            "Use endpoint A 'Local Mock Customer Lookup SOAP' and endpoint B 'Local Mock Customer Lookup JSON' for the ExpectedJsonCustomerLookupResponse alternate contract profile.",
                            ex);
                    }

                    var normalized = new ExpectedJsonCustomerLookupResponse
                    {
                        ResultCode = soapResponse.Body.CustomerLookupResponse.StatusCode,
                        CustomerName = soapResponse.Body.CustomerLookupResponse.CustomerName,
                        TraceId = soapResponse.Body.CustomerLookupResponse.TraceId,
                        SourceSystem = "endpoint-a",
                    };

                    return new NormalizedAlternateContractResponse(
                        JsonSerializer.SerializeToUtf8Bytes(normalized, DefaultSerializerOptions),
                        SerializationFormat.Json,
                        "application/json",
                        null);
                })
                .AddDefaultIgnoreRule(new IgnoreRuleDto
                {
                    PropertyPath = $"{ExpectedModelName}.SourceSystem",
                    IgnoreCompletely = true,
                }));
    }
}

public sealed class ExpectedJsonCustomerLookupAlternateContractOptions
{
    public const string ConfigurationSectionName = "RequestComparison:AlternateContracts:ExpectedJsonCustomerLookup";

    public string AuthorizationTokenUrl { get; set; } = "http://localhost:5055/api/mock/authorisation-token";

    public string HttpClientName { get; set; } = "RequestComparison";
}

public interface IExpectedJsonCustomerLookupAuthorizationTokenService
{
    Task<ExpectedJsonCustomerLookupAuthorizationTokenResponse> GetAuthorizationTokensAsync(
        ExpectedJsonCustomerLookupAuthorizationTokenRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class HttpExpectedJsonCustomerLookupAuthorizationTokenService
    : IExpectedJsonCustomerLookupAuthorizationTokenService
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptions<ExpectedJsonCustomerLookupAlternateContractOptions> options;

    public HttpExpectedJsonCustomerLookupAuthorizationTokenService(
        IHttpClientFactory httpClientFactory,
        IOptions<ExpectedJsonCustomerLookupAlternateContractOptions> options)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options;
    }

    public async Task<ExpectedJsonCustomerLookupAuthorizationTokenResponse> GetAuthorizationTokensAsync(
        ExpectedJsonCustomerLookupAuthorizationTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuredOptions = options.Value;
        if (string.IsNullOrWhiteSpace(configuredOptions.AuthorizationTokenUrl))
        {
            throw new InvalidOperationException(
                $"Configuration value '{ExpectedJsonCustomerLookupAlternateContractOptions.ConfigurationSectionName}:AuthorizationTokenUrl' is required.");
        }

        var clientName = string.IsNullOrWhiteSpace(configuredOptions.HttpClientName)
            ? "RequestComparison"
            : configuredOptions.HttpClientName;

        using var client = httpClientFactory.CreateClient(clientName);
        using var response = await client.PostAsJsonAsync(
            configuredOptions.AuthorizationTokenUrl,
            request,
            RequestComparisonExpectedJsonCustomerLookupRegistration.DefaultSerializerOptions,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ExpectedJsonCustomerLookupAuthorizationTokenResponse>(
            RequestComparisonExpectedJsonCustomerLookupRegistration.DefaultSerializerOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Authorization token service returned an empty payload.");
    }
}