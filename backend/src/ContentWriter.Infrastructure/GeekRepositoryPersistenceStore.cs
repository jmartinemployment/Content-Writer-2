using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ContentWriter.Infrastructure;

/// <summary>
/// GeekRepository-backed persistence store — calls the generic content-writer-v2 blobs API
/// (repo/content-writer-v2/blobs/{collection}/{id}) in GeekRepository. Only valid to construct
/// inside a host process that already owns a trusted, pre-configured "GeekRepository" named
/// HttpClient (X-Repo-Key already attached) — i.e. GeekAPI. This class never builds its own
/// client or reads REPO_API_KEY; see AGENTS.md "Persistence and target architecture" for why
/// content-writer-v2 does not hold that credential itself.
/// </summary>
public sealed class GeekRepositoryPersistenceStore : IPersistenceStore
{
    private const string HttpClientName = "GeekRepository";
    private const string BasePath = "repo/content-writer-v2/blobs";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GeekRepositoryPersistenceStore> _logger;

    public GeekRepositoryPersistenceStore(IHttpClientFactory httpClientFactory, ILogger<GeekRepositoryPersistenceStore> logger)
    {
        _httpClientFactory = httpClientFactory;
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
