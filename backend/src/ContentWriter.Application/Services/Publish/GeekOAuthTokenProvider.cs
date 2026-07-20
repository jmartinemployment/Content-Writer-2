using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace ContentWriter.Application.Services.Publish;

/// <summary>Client-credentials OAuth2 config for a single GeekBackend target — never persisted, resolved fresh per call.</summary>
public sealed record GeekOAuthCredentials(string TokenEndpoint, string ClientId, string ClientSecret);

public interface IGeekOAuthTokenProvider
{
    /// <summary>Returns a cached Bearer token if still valid, otherwise fetches and caches a new one.</summary>
    Task<string> GetAccessTokenAsync(GeekOAuthCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Drops any cached token for these credentials, forcing a refetch on the next call. Use after a 401.</summary>
    void Invalidate(GeekOAuthCredentials credentials);
}

/// <summary>
/// OAuth2 client-credentials token provider for GeekBackend — replaces the old X-API-Key header.
/// Tokens are cached in memory per (endpoint, clientId) until shortly before expiry.
/// </summary>
public sealed class GeekOAuthTokenProvider : IGeekOAuthTokenProvider
{
    private const string HttpClientName = "GeekBackend";
    private const int ExpiryBufferSeconds = 30;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, CachedToken> _cache = new();

    public GeekOAuthTokenProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> GetAccessTokenAsync(GeekOAuthCredentials credentials, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(credentials);
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(ExpiryBufferSeconds))
            return cached.AccessToken;

        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, credentials.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret,
            }),
        };

        var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new GeekBackendPublishException(
                $"GeekBackend OAuth token request failed ({(int)response.StatusCode}): {errorBody}");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var accessToken = body.TryGetProperty("access_token", out var tokenEl) ? tokenEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new GeekBackendPublishException("GeekBackend OAuth token response missing access_token.");

        var expiresInSeconds = body.TryGetProperty("expires_in", out var expEl) && expEl.TryGetInt32(out var exp) ? exp : 3600;
        _cache[cacheKey] = new CachedToken(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds));

        return accessToken;
    }

    public void Invalidate(GeekOAuthCredentials credentials) => _cache.TryRemove(CacheKey(credentials), out _);

    private static string CacheKey(GeekOAuthCredentials credentials) => $"{credentials.TokenEndpoint}|{credentials.ClientId}";

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
