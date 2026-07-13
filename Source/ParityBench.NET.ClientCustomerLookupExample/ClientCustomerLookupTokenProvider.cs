using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

namespace ParityBench.NET.ClientCustomerLookupExample;

public interface IClientCustomerLookupTokenProvider
{
    Task<ClientCustomerLookupTokenResult> GetFinalTokenAsync(
        ClientCustomerLookupRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ClientCustomerLookupTokenProvider : IClientCustomerLookupTokenProvider
{
    public const string SubscriptionKeyHeaderName = "Ocp-Apim-Subscription-Key";

    private readonly HttpClient httpClient;
    private readonly ClientCustomerLookupTokenOptions options;

    public ClientCustomerLookupTokenProvider(
        HttpClient httpClient,
        IOptions<ClientCustomerLookupTokenOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<ClientCustomerLookupTokenResult> GetFinalTokenAsync(
        ClientCustomerLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOptions();

        using HttpRequestMessage primaryTokenRequest = new(HttpMethod.Post, options.PrimaryTokenUrl)
        {
            Content = JsonContent.Create(new PrimaryTokenRequest(
                request.UserName,
                request.Password,
                request.CustomerId)),
        };
        primaryTokenRequest.Headers.Add(SubscriptionKeyHeaderName, options.PrimaryTokenSubscriptionKey);

        PrimaryTokenResponse primaryToken = await SendTokenAsync<PrimaryTokenResponse>(
            primaryTokenRequest,
            cancellationToken).ConfigureAwait(false);

        using HttpRequestMessage finalTokenRequest = new(HttpMethod.Post, options.FinalTokenUrl)
        {
            Content = JsonContent.Create(new FinalTokenRequest(
                primaryToken.AccessToken,
                request.CustomerId,
                request.CorrelationId)),
        };
        finalTokenRequest.Headers.Add(SubscriptionKeyHeaderName, options.FinalTokenSubscriptionKey);

        FinalTokenResponse finalToken = await SendTokenAsync<FinalTokenResponse>(
            finalTokenRequest,
            cancellationToken).ConfigureAwait(false);

        return new ClientCustomerLookupTokenResult(finalToken.AccessToken);
    }

    private async Task<T> SendTokenAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Token response body was empty.");
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(options.PrimaryTokenUrl))
        {
            throw new InvalidOperationException("Client customer lookup primary token URL is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.PrimaryTokenSubscriptionKey))
        {
            throw new InvalidOperationException("Client customer lookup primary token subscription key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.FinalTokenUrl))
        {
            throw new InvalidOperationException("Client customer lookup final token URL is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.FinalTokenSubscriptionKey))
        {
            throw new InvalidOperationException("Client customer lookup final token subscription key is not configured.");
        }
    }

    private sealed record PrimaryTokenRequest(
        [property: JsonPropertyName("username")] string UserName,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("customerId")] string CustomerId);

    private sealed record FinalTokenRequest(
        [property: JsonPropertyName("primaryToken")] string PrimaryToken,
        [property: JsonPropertyName("customerId")] string CustomerId,
        [property: JsonPropertyName("correlationId")] string CorrelationId);
}

public sealed record ClientCustomerLookupTokenResult(string AccessToken);
