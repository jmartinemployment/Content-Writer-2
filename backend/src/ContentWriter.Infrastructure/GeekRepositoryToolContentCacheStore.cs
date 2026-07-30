using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ContentWriter.Infrastructure;

/// <summary>
/// GeekRepository-backed tool-content cache — calls repo/content-writer-v2/tool-content-cache/{name}
/// in GeekRepository. Only valid to construct inside a host process that already owns a trusted,
/// pre-configured "GeekRepository" named HttpClient (X-Repo-Key already attached) — i.e. GeekAPI.
/// Same credential-boundary rule as <see cref="GeekRepositoryPersistenceStore"/> — see that class's
/// doc comment and AGENTS.md "Persistence and target architecture" for why.
/// </summary>
public sealed class GeekRepositoryToolContentCacheStore : IToolContentCacheStore
{
    private const string HttpClientName = "GeekRepository";
    private const string BasePath = "repo/content-writer-v2/tool-content-cache";

    private static readonly Regex LegalSuffix = new(
        @"\b(inc\.?|llc\.?|ltd\.?|corp\.?|co\.?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NonAlphanumeric = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GeekRepositoryToolContentCacheStore> _logger;

    public GeekRepositoryToolContentCacheStore(IHttpClientFactory httpClientFactory, ILogger<GeekRepositoryToolContentCacheStore> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Canonicalizes a tool name so "Zapier", "zapier", and "Zapier Inc." all resolve to
    /// the same cache key: lowercase, strip common legal suffixes, collapse to alphanumeric.</summary>
    public static string Canonicalize(string toolName)
    {
        var trimmed = toolName.Trim();
        var withoutSuffix = LegalSuffix.Replace(trimmed, string.Empty).Trim();
        var lowered = withoutSuffix.ToLowerInvariant();
        return NonAlphanumeric.Replace(lowered, string.Empty);
    }

    public async Task<CachedToolContent?> GetAsync(string toolName, CancellationToken cancellationToken = default)
    {
        var key = Canonicalize(toolName);
        var http = BuildClient();
        var response = await http.GetAsync($"{BasePath}/{Uri.EscapeDataString(key)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccess(response, $"loading tool content cache for '{key}'", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var dto = JsonSerializer.Deserialize<ToolContentCacheDto>(body, JsonOptions);
        if (dto is null)
        {
            return null;
        }

        _logger.LogDebug("Tool content cache hit for '{Key}' ({Bytes} bytes)", key, dto.OverviewJson.Length);
        return new CachedToolContent(dto.DisplayName, dto.OverviewJson);
    }

    public async Task SaveAsync(string toolName, string displayName, string overviewJson, CancellationToken cancellationToken = default)
    {
        var key = Canonicalize(toolName);
        var http = BuildClient();
        var payload = JsonSerializer.Serialize(new { displayName, overviewJson }, JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await http.PutAsync($"{BasePath}/{Uri.EscapeDataString(key)}", content, cancellationToken);
        await EnsureSuccess(response, $"saving tool content cache for '{key}'", cancellationToken);
        _logger.LogDebug("Saved tool content cache for '{Key}'", key);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ToolContentCacheDto(string NormalizedToolName, string DisplayName, string OverviewJson, DateTime UpdatedAtUtc);

    private HttpClient BuildClient()
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    private async Task EnsureSuccess(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"GeekRepository error while {action} ({(int)response.StatusCode}): {body}");
    }
}
