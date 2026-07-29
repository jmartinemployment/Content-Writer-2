using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ContentWriter.Infrastructure;

/// <summary>
/// GeekRepository-backed persistence store — calls the generic content-writer-v2 blobs API
/// (repo/content-writer-v2/blobs/{collection}/{id}) added to GeekRepository specifically for
/// this store. Auth is a static shared secret (X-Repo-Key header == GeekRepository's own
/// REPO_API_KEY env var) — not OAuth, no token to fetch or refresh; see
/// RepoApiKeyAuthenticationHandler in GeekRepository for the server-side contract this mirrors.
/// </summary>
public sealed class GeekRepositoryPersistenceStore : IPersistenceStore
{
    private const string HttpClientName = "GeekRepository";
    private const string BasePath = "repo/content-writer-v2/blobs";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly ILogger<GeekRepositoryPersistenceStore> _logger;

    public GeekRepositoryPersistenceStore(
        IHttpClientFactory httpClientFactory, string baseUrl, string apiKey, ILogger<GeekRepositoryPersistenceStore> logger)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("GeekRepository base URL is required.", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("REPO_API_KEY is required.", nameof(apiKey));

        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _logger = logger;
    }

    public async Task SaveDocumentAsync(string collection, Guid id, string json, CancellationToken cancellationToken = default)
    {
        var http = BuildClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await http.PutAsync($"{BasePath}/{collection}/{id:D}", content, cancellationToken);
        await EnsureSuccess(response, $"saving {collection}/{id}", cancellationToken);
        _logger.LogDebug("Persisted {Collection}/{Id} to GeekRepository ({Bytes} bytes)", collection, id, json.Length);
    }

    public async Task<string?> LoadDocumentAsync(string collection, Guid id, CancellationToken cancellationToken = default)
    {
        var http = BuildClient();
        var response = await http.GetAsync($"{BasePath}/{collection}/{id:D}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccess(response, $"loading {collection}/{id}", cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogDebug("Loaded {Collection}/{Id} from GeekRepository ({Bytes} bytes)", collection, id, json.Length);
        return json;
    }

    public async Task<IReadOnlyList<Guid>> ListDocumentsAsync(string collection, CancellationToken cancellationToken = default)
    {
        var http = BuildClient();
        var response = await http.GetAsync($"{BasePath}/{collection}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await EnsureSuccess(response, $"listing {collection}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var ids = JsonSerializer.Deserialize<List<Guid>>(body) ?? [];
        _logger.LogDebug("Listed {Collection}: {Count} document(s) from GeekRepository", collection, ids.Count);
        return ids;
    }

    public async Task DeleteDocumentAsync(string collection, Guid id, CancellationToken cancellationToken = default)
    {
        var http = BuildClient();
        var response = await http.DeleteAsync($"{BasePath}/{collection}/{id:D}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccess(response, $"deleting {collection}/{id}", cancellationToken);
        _logger.LogDebug("Deleted {Collection}/{Id} from GeekRepository", collection, id);
    }

    private HttpClient BuildClient()
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(_baseUrl + "/");
        if (!http.DefaultRequestHeaders.Contains("X-Repo-Key"))
            http.DefaultRequestHeaders.Add("X-Repo-Key", _apiKey);
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
