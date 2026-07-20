using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentWriter.Application.Services.Publish;

public sealed record GeekBlogSectionPayload(
    int SortOrder,
    string? HeadingTag,
    string? HeadingText,
    string BodyContent,
    string? MediaUrl,
    string? MediaAlt);

public sealed record GeekBlogPostPayload(
    string PostType,
    string SchemaType,
    bool IsPublished,
    string LanguageCode,
    string Slug,
    string Title,
    string Summary,
    string? MetaDescription,
    string MainSummary,
    string HeroSummary,
    string HomeSummary,
    string BlogSummary,
    string AdvertisingSummary,
    string? JsonLdOverride,
    IReadOnlyList<GeekBlogSectionPayload> Sections,
    IReadOnlyList<string> TagSlugs,
    int? AuthorId,
    DateTimeOffset? PublishedAt,
    string CategorySlug,
    Dictionary<string, string>? Presentation,
    string? CwJobId);

public sealed record GeekBlogPostResult(int PostId, string Slug, string LanguageCode, int SectionCount, bool WasUpdated);

/// <summary>Resolved per-client connection details for a single publish call — never persisted, built fresh from PublishTarget + env var lookup.</summary>
public sealed record PublishTargetContext(string ApiBaseUrl, GeekOAuthCredentials OAuth, int? DefaultAuthorId);

/// <summary>HTTP client for GeekBackend's blog API (GeekAPI, `api/blog`). Auth mirrors ImportBlogContent's `X-API-Key` header pattern.</summary>
public interface IGeekBackendClient
{
    Task<int?> FindExistingPostIdAsync(
        PublishTargetContext target, string slug, string languageCode, CancellationToken cancellationToken = default);

    Task<GeekBlogPostResult> UpsertPostAsync(
        PublishTargetContext target,
        GeekBlogPostPayload payload,
        int? existingPostId,
        CancellationToken cancellationToken = default);

    Task<JsonElement> GetCategoriesAsync(PublishTargetContext target, string lang, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base address varies per call (each client has its own GeekBackend target), so this uses
/// <see cref="IHttpClientFactory"/> and builds absolute URIs per request rather than a typed
/// client with a fixed BaseAddress — safe under the multi-project concurrency in Phase 3.
/// </summary>
public sealed class GeekBackendClient : IGeekBackendClient
{
    private const string HttpClientName = "GeekBackend";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGeekOAuthTokenProvider _tokenProvider;

    public GeekBackendClient(IHttpClientFactory httpClientFactory, IGeekOAuthTokenProvider tokenProvider)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
    }

    private async Task AttachAuthAsync(HttpRequestMessage request, PublishTargetContext target, CancellationToken cancellationToken)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(target.OAuth, cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<int?> FindExistingPostIdAsync(
        PublishTargetContext target,
        string slug,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(target, $"api/blog/{languageCode}/{slug}"));
        await AttachAuthAsync(request, target, cancellationToken);

        var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return body.TryGetProperty("postId", out var postId) ? postId.GetInt32() : null;
    }

    public async Task<GeekBlogPostResult> UpsertPostAsync(
        PublishTargetContext target,
        GeekBlogPostPayload payload,
        int? existingPostId,
        CancellationToken cancellationToken = default)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            existingPostId is null ? HttpMethod.Post : HttpMethod.Put,
            existingPostId is null ? BuildUri(target, "api/blog") : BuildUri(target, $"api/blog/{existingPostId}"))
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        await AttachAuthAsync(request, target, cancellationToken);

        var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new GeekBackendPublishException(
                $"GeekBackend blog publish failed ({(int)response.StatusCode}): {errorBody}");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var postId = body.GetProperty("postId").GetInt32();
        var slug = body.GetProperty("slug").GetString() ?? payload.Slug;
        var languageCode = body.GetProperty("languageCode").GetString() ?? payload.LanguageCode;
        var sectionCount = body.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array
            ? sections.GetArrayLength()
            : payload.Sections.Count;

        return new GeekBlogPostResult(postId, slug, languageCode, sectionCount, existingPostId is not null);
    }

    public async Task<JsonElement> GetCategoriesAsync(
        PublishTargetContext target, string lang, CancellationToken cancellationToken = default)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(target, $"api/blog/categories?lang={lang}"));
        await AttachAuthAsync(request, target, cancellationToken);

        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
    }

    private static Uri BuildUri(PublishTargetContext target, string relativePath) =>
        new(target.ApiBaseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/'));
}

public sealed class GeekBackendPublishException(string message) : Exception(message);
