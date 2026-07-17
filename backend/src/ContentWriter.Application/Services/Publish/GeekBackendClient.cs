using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

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

/// <summary>HTTP client for GeekBackend's blog API (GeekAPI, `api/blog`). Auth mirrors ImportBlogContent's `X-API-Key` header pattern.</summary>
public interface IGeekBackendClient
{
    Task<int?> FindExistingPostIdAsync(string slug, string languageCode, CancellationToken cancellationToken = default);

    Task<GeekBlogPostResult> UpsertPostAsync(
        GeekBlogPostPayload payload,
        int? existingPostId,
        CancellationToken cancellationToken = default);

    Task<JsonElement> GetCategoriesAsync(string lang, CancellationToken cancellationToken = default);
}

public sealed class GeekBackendClient : IGeekBackendClient
{
    private const string ApiKeyHeader = "X-API-Key";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly GeekBackendOptions _options;

    public GeekBackendClient(HttpClient http, IOptions<GeekBackendOptions> options)
    {
        _options = options.Value;
        http.BaseAddress = new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/");
        _http = http;
    }

    public async Task<int?> FindExistingPostIdAsync(
        string slug,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/blog/{languageCode}/{slug}");
        request.Headers.Add(ApiKeyHeader, _options.ApiKey);

        var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return body.TryGetProperty("postId", out var postId) ? postId.GetInt32() : null;
    }

    public async Task<GeekBlogPostResult> UpsertPostAsync(
        GeekBlogPostPayload payload,
        int? existingPostId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            existingPostId is null ? HttpMethod.Post : HttpMethod.Put,
            existingPostId is null ? "api/blog" : $"api/blog/{existingPostId}")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Add(ApiKeyHeader, _options.ApiKey);

        var response = await _http.SendAsync(request, cancellationToken);
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

    public async Task<JsonElement> GetCategoriesAsync(string lang, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/blog/categories?lang={lang}");
        request.Headers.Add(ApiKeyHeader, _options.ApiKey);

        var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
    }
}

public sealed class GeekBackendPublishException(string message) : Exception(message);
