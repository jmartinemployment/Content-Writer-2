namespace ContentWriter.Application.Services.Publish;

/// <summary>Connection settings for GeekBackend's blog API (GeekAPI, `api/blog`).</summary>
public class GeekBackendOptions
{
    public const string SectionName = "GeekBackend";

    public string ApiBaseUrl { get; set; } = "http://localhost:5052";

    /// <summary>Sent as the `X-API-Key` header, matching GeekAPI's ApiKeyMiddleware and ImportBlogContent's auth pattern.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// geek_blog.users.id to attribute published posts to. geek_blog.posts.author_id is NOT NULL,
    /// so this must be configured to a seeded user before publishing will succeed.
    /// </summary>
    public int? DefaultAuthorId { get; set; }
}
