using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ParityBench.ClientCustomerLookupPlugin;

/// <summary>
/// Configuration a run supplies for the token exchange. URLs are plain settings;
/// the subscription keys arrive already resolved from the secret store (the schema
/// marks them as <see cref="PluginSdk.Configuration.PluginFieldKind.Secret"/>), so
/// this class never sees a <c>secret://</c> reference.
/// </summary>
public sealed record ClientCustomerLookupTokenOptions(
    string PrimaryTokenUrl,
    string PrimaryTokenSubscriptionKey,
    string FinalTokenUrl,
    string FinalTokenSubscriptionKey,
    string EndpointBSubscriptionKey);

/// <summary>
/// Performs the two-hop bearer-token exchange endpoint B requires and caches the
/// result by credential identity.
/// </summary>
/// <remarks>
/// The token authenticates the caller (username/password), not the customer being
/// looked up, so the cache key must exclude the customer id — otherwise every
/// request in a volume batch (each with a distinct customer id) misses and the
/// cache does nothing. A faulted fetch is evicted so later callers retry.
/// </remarks>
public sealed class ClientCustomerLookupTokenExchange
{
    public const string SubscriptionKeyHeaderName = "Ocp-Apim-Subscription-Key";

    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<string, Task<ClientCustomerLookupTokenResult>> tokenCache =
        new ConcurrentDictionary<string, Task<ClientCustomerLookupTokenResult>>();

    public ClientCustomerLookupTokenExchange(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
    }

    public async Task<ClientCustomerLookupTokenResult> GetFinalTokenAsync(
        ClientCustomerLookupRequest request,
        ClientCustomerLookupTokenOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        string cacheKey = string.Join('␟', request.UserName, request.Password);
        Task<ClientCustomerLookupTokenResult> tokenTask = tokenCache.GetOrAdd(
            cacheKey,
            _ => FetchFinalTokenAsync(request, options, CancellationToken.None));

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
        ClientCustomerLookupTokenOptions options,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage primaryRequest = new HttpRequestMessage(HttpMethod.Post, options.PrimaryTokenUrl)
        {
            Content = JsonContent.Create(new PrimaryTokenRequest(request.UserName, request.Password, request.CustomerId)),
        };
        primaryRequest.Headers.Add(SubscriptionKeyHeaderName, options.PrimaryTokenSubscriptionKey);
        TokenResponse primaryToken = await SendAsync<TokenResponse>(primaryRequest, cancellationToken).ConfigureAwait(false);

        using HttpRequestMessage finalRequest = new HttpRequestMessage(HttpMethod.Post, options.FinalTokenUrl)
        {
            Content = JsonContent.Create(new FinalTokenRequest(primaryToken.AccessToken, request.CustomerId, request.CorrelationId)),
        };
        finalRequest.Headers.Add(SubscriptionKeyHeaderName, options.FinalTokenSubscriptionKey);
        TokenResponse finalToken = await SendAsync<TokenResponse>(finalRequest, cancellationToken).ConfigureAwait(false);

        return new ClientCustomerLookupTokenResult(finalToken.AccessToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Token response body was empty.");
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
