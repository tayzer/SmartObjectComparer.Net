using System.Collections.Concurrent;
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

    // Volume runs fan hundreds of comparison requests out concurrently, each of which
    // otherwise re-fetches a primary+final token pair. The token authenticates the caller
    // (UserName/Password), not the customer being looked up, so it's cached by credential
    // identity only -- CustomerId must NOT be part of the key, or every request in a volume
    // batch misses (each fixture uses a distinct CustomerId) and the cache does nothing.
    // Caching collapses hundreds of token round trips down to one per distinct credential
    // set, which is what keeps the in-process test server from being overwhelmed (and
    // requests from being aborted mid-flight) under load. A faulted fetch is evicted so
    // later callers retry.
    private readonly ConcurrentDictionary<string, Task<ClientCustomerLookupTokenResult>> tokenCache = new();

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

        string cacheKey = string.Join('␟', request.UserName, request.Password);
        Task<ClientCustomerLookupTokenResult> tokenTask = tokenCache.GetOrAdd(
            cacheKey,
            _ => FetchFinalTokenAsync(request, CancellationToken.None));

        try
        {
            return await tokenTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (tokenTask.IsFaulted)
        {
            tokenCache.TryRemove(cacheKey, out _);
            throw;
        }
    }

    private async Task<ClientCustomerLookupTokenResult> FetchFinalTokenAsync(
        ClientCustomerLookupRequest request,
        CancellationToken cancellationToken)
    {
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
