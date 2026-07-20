using System.Net;
using System.Net.Http.Json;
using ContentWriter.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace ContentWriter.Application.Services.Publish;

/// <summary>
/// content-writer-v2 has no local database tables, no local DbContext, and no local database
/// connection string for WebPost content. This class communicates purely over HTTP by calling
/// GeekAPI, which routes the request to the real GeekRepository service — the only thing that
/// actually talks to Postgres. Auth is X-API-Key, matching GeekAPI's real ApiKeyMiddleware.
/// </summary>
public interface IGeekRepository
{
    Task<WebPost?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<WebPost> UpsertAsync(WebPost post, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default);
}

public sealed class GeekRepository : IGeekRepository
{
    private const string HttpClientName = "GeekBackend";
    private const string ApiKeyHeader = "X-API-Key";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiBaseUrl;
    private readonly string _apiKey;

    public GeekRepository(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;

        _apiBaseUrl = Environment.GetEnvironmentVariable("GEEK_BACKEND_API_BASE_URL")
            ?? configuration["GeekBackend:ApiBaseUrl"]
            ?? "https://api.geekatyourspot.com";

        _apiKey = Environment.GetEnvironmentVariable("GEEK_BACKEND_API_KEY")
            ?? configuration["GeekBackend:ApiKey"]
            ?? throw new InvalidOperationException(
                "GEEK_BACKEND_API_KEY is not configured — cannot call GeekAPI without an API key.");
    }

    public async Task<WebPost?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri($"api/webposts/{Uri.EscapeDataString(slug)}"));
        request.Headers.Add(ApiKeyHeader, _apiKey);

        var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WebPost>(cancellationToken: cancellationToken);
    }

    public async Task<WebPost> UpsertAsync(WebPost post, CancellationToken cancellationToken = default)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("api/webposts"))
        {
            Content = JsonContent.Create(post),
        };
        request.Headers.Add(ApiKeyHeader, _apiKey);

        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WebPost>(cancellationToken: cancellationToken))!;
    }

    public async Task<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUri($"api/webposts/{Uri.EscapeDataString(slug)}"));
        request.Headers.Add(ApiKeyHeader, _apiKey);

        var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();
        return true;
    }

    private Uri BuildUri(string relativePath) =>
        new(_apiBaseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/'));
}
