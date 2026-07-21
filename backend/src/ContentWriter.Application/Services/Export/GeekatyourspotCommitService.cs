using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContentWriter.Application.Providers;
using Microsoft.Extensions.Logging;

namespace ContentWriter.Application.Services.Export;

public interface IGeekatyourspotCommitService
{
    /// <summary>Exports the project's content and commits it as one atomic Git commit to geekatyourspot's content-writer-output/ directory.</summary>
    Task<GeekatyourspotCommitResult> CommitExportAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public sealed record GeekatyourspotCommitResult(string CommitSha, string CommitUrl, IReadOnlyList<string> FilePaths);

/// <summary>
/// Commits exported .mdx files directly to the geekatyourspot GitHub repo via the Git Data API
/// (blob -> tree -> commit -> ref update) — one atomic commit touching every file from a single
/// export, avoiding the per-file-commit races of the simpler Contents API.
/// </summary>
public sealed class GeekatyourspotCommitService : IGeekatyourspotCommitService
{
    private const string HttpClientName = "GitHub";
    private const string Owner = "jmartinemployment";
    private const string Repo = "geekatyourspot";
    private const string Branch = "main";
    private const string BasePath = "content-writer-output";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMdxExportService _mdxExportService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GeekatyourspotCommitService> _logger;

    public GeekatyourspotCommitService(
        IMdxExportService mdxExportService, IHttpClientFactory httpClientFactory, ILogger<GeekatyourspotCommitService> logger)
    {
        _mdxExportService = mdxExportService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GeekatyourspotCommitResult> CommitExportAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var token = Environment.GetEnvironmentVariable("GEEKATYOURSPOT_GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ContentGenerationException(
                "GEEKATYOURSPOT_GITHUB_TOKEN is not configured — cannot commit to geekatyourspot without a GitHub token.");
        }

        var documents = await _mdxExportService.ExportAsync(projectId, cancellationToken);

        var http = BuildClient(token);

        var refSha = await GetRefShaAsync(http, cancellationToken);
        var baseTreeSha = await GetCommitTreeShaAsync(http, refSha, cancellationToken);

        var treeEntries = new List<TreeEntry>();
        foreach (var document in documents)
        {
            var blobSha = await CreateBlobAsync(http, document.Content, cancellationToken);
            treeEntries.Add(new TreeEntry($"{BasePath}/{document.FileName}", "100644", "blob", blobSha));
        }

        var newTreeSha = await CreateTreeAsync(http, baseTreeSha, treeEntries, cancellationToken);
        var commitMessage = $"Content Writer export: project {projectId} ({documents.Count} file(s))";
        var (commitSha, commitUrl) = await CreateCommitAsync(http, commitMessage, newTreeSha, refSha, cancellationToken);
        await UpdateRefAsync(http, commitSha, cancellationToken);

        _logger.LogInformation(
            "Committed {FileCount} file(s) to {Owner}/{Repo}@{Branch} as {CommitSha}",
            documents.Count, Owner, Repo, Branch, commitSha);

        return new GeekatyourspotCommitResult(commitSha, commitUrl, treeEntries.Select(e => e.Path).ToList());
    }

    private HttpClient BuildClient(string token)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri("https://api.github.com/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("content-writer-v2", "1.0"));
        if (!http.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    private static async Task<string> GetRefShaAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync($"repos/{Owner}/{Repo}/git/refs/heads/{Branch}", cancellationToken);
        await EnsureSuccess(response, "fetching branch ref", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<RefResponse>(JsonOptions, cancellationToken);
        return body?.Object?.Sha ?? throw new ContentGenerationException("GitHub ref response missing object.sha.");
    }

    private static async Task<string> GetCommitTreeShaAsync(HttpClient http, string commitSha, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync($"repos/{Owner}/{Repo}/git/commits/{commitSha}", cancellationToken);
        await EnsureSuccess(response, "fetching base commit", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<CommitResponse>(JsonOptions, cancellationToken);
        return body?.Tree?.Sha ?? throw new ContentGenerationException("GitHub commit response missing tree.sha.");
    }

    private static async Task<string> CreateBlobAsync(HttpClient http, string content, CancellationToken cancellationToken)
    {
        var payload = new BlobRequest(Convert.ToBase64String(Encoding.UTF8.GetBytes(content)), "base64");
        var response = await http.PostAsJsonAsync($"repos/{Owner}/{Repo}/git/blobs", payload, JsonOptions, cancellationToken);
        await EnsureSuccess(response, "creating blob", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ShaResponse>(JsonOptions, cancellationToken);
        return body?.Sha ?? throw new ContentGenerationException("GitHub blob response missing sha.");
    }

    private static async Task<string> CreateTreeAsync(
        HttpClient http, string baseTreeSha, List<TreeEntry> entries, CancellationToken cancellationToken)
    {
        var payload = new TreeRequest(baseTreeSha, entries);
        var response = await http.PostAsJsonAsync($"repos/{Owner}/{Repo}/git/trees", payload, JsonOptions, cancellationToken);
        await EnsureSuccess(response, "creating tree", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ShaResponse>(JsonOptions, cancellationToken);
        return body?.Sha ?? throw new ContentGenerationException("GitHub tree response missing sha.");
    }

    private static async Task<(string Sha, string Url)> CreateCommitAsync(
        HttpClient http, string message, string treeSha, string parentSha, CancellationToken cancellationToken)
    {
        var payload = new CommitRequest(message, treeSha, [parentSha]);
        var response = await http.PostAsJsonAsync($"repos/{Owner}/{Repo}/git/commits", payload, JsonOptions, cancellationToken);
        await EnsureSuccess(response, "creating commit", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<CommitResponse>(JsonOptions, cancellationToken);
        if (body?.Sha is null)
            throw new ContentGenerationException("GitHub commit response missing sha.");
        return (body.Sha, $"https://github.com/{Owner}/{Repo}/commit/{body.Sha}");
    }

    private static async Task UpdateRefAsync(HttpClient http, string commitSha, CancellationToken cancellationToken)
    {
        var payload = new UpdateRefRequest(commitSha, false);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"repos/{Owner}/{Repo}/git/refs/heads/{Branch}")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccess(response, "updating branch ref", cancellationToken);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new ContentGenerationException($"GitHub API error while {action} ({(int)response.StatusCode}): {body}");
    }

    private sealed record BlobRequest(string Content, string Encoding);
    private sealed record TreeEntry(string Path, string Mode, string Type, string Sha);
    private sealed record TreeRequest([property: JsonPropertyName("base_tree")] string BaseTree, List<TreeEntry> Tree);
    private sealed record CommitRequest(string Message, string Tree, List<string> Parents);
    private sealed record UpdateRefRequest(string Sha, bool Force);

    private sealed class ShaResponse
    {
        [JsonPropertyName("sha")] public string? Sha { get; set; }
    }

    private sealed class RefResponse
    {
        [JsonPropertyName("object")] public RefObject? Object { get; set; }
    }

    private sealed class RefObject
    {
        [JsonPropertyName("sha")] public string? Sha { get; set; }
    }

    private sealed class CommitResponse
    {
        [JsonPropertyName("sha")] public string? Sha { get; set; }
        [JsonPropertyName("tree")] public ShaResponse? Tree { get; set; }
    }
}
