using System.Text.RegularExpressions;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.Export;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentWriter.Application.Services.Publish;

public interface IGeekBlogPublishService
{
    Task<GeekBlogPublishResult> PublishAsync(
        Guid projectId,
        string? departmentOverride = null,
        CancellationToken cancellationToken = default);
}

public sealed record GeekBlogPublishedPost(
    string ContentType,
    int PostId,
    string Slug,
    string LanguageCode,
    int SectionCount,
    bool WasUpdated);

public sealed record GeekBlogPublishResult(string CategorySlug, IReadOnlyList<GeekBlogPublishedPost> Posts);

/// <summary>
/// Publishes assembled project content straight into GeekBackend's geek_blog schema via `api/blog`,
/// replacing the old markdown export step. Splits BodyHtml into flat post_sections on &lt;h2&gt;
/// boundaries using the same rule as GeekBackend's GeekApplication/Blog/HtmlSectionSplitter.cs.
/// </summary>
public class GeekBlogPublishService : IGeekBlogPublishService
{
    private const int PillarBodyMinWords = 200;
    private const string DefaultLanguageCode = "en";

    private readonly IProjectRepository _projectRepository;
    private readonly IGeekBackendClient _geekBackendClient;
    private readonly CompanyProfileOptions _companyProfile;
    private readonly GeekBackendOptions _geekBackendOptions;
    private readonly ILogger<GeekBlogPublishService> _logger;

    public GeekBlogPublishService(
        IProjectRepository projectRepository,
        IGeekBackendClient geekBackendClient,
        IOptions<CompanyProfileOptions> companyProfile,
        IOptions<GeekBackendOptions> geekBackendOptions,
        ILogger<GeekBlogPublishService> logger)
    {
        _projectRepository = projectRepository;
        _geekBackendClient = geekBackendClient;
        _companyProfile = companyProfile.Value;
        _geekBackendOptions = geekBackendOptions.Value;
        _logger = logger;
    }

    public async Task<GeekBlogPublishResult> PublishAsync(
        Guid projectId,
        string? departmentOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (_geekBackendOptions.DefaultAuthorId is null)
        {
            throw new ContentGenerationException(
                "GeekBackend:DefaultAuthorId is not configured. geek_blog.posts.author_id is required " +
                "— set it to a seeded geek_blog.users.id before publishing.");
        }

        var project = await _projectRepository.GetWithDetailsAsync(projectId, cancellationToken)
            ?? throw new ContentGenerationException($"Project {projectId} was not found.");

        var contentSet = GeneratedContentSetAssembler.Assemble(
            project,
            _companyProfile.ArticleBaseUrl,
            _companyProfile.BlogBaseUrl,
            _companyProfile.ToolBaseUrl);

        var categorySlug = DepartmentNameResolver.Resolve(departmentOverride);

        var publishedAt = DateTimeOffset.UtcNow;
        var published = new List<GeekBlogPublishedPost>();

        var articleRow = project.GeneratedContents.FirstOrDefault(c => c.ContentType == GeneratedContentType.TechnicalArticle);
        if (contentSet.Article is not null
            && contentSet.Article.WordCount >= PillarBodyMinWords
            && !string.IsNullOrWhiteSpace(contentSet.ArticleSlug))
        {
            published.Add(await PublishOneAsync(
                contentType: "pillar",
                postType: "Pillar",
                schemaType: "TechnicalArticle",
                slug: contentSet.ArticleSlug!,
                title: contentSet.Article.Title,
                metaDescription: contentSet.Article.MetaDescription,
                bodyHtml: contentSet.Article.BodyHtml,
                jsonLdOverride: contentSet.ArticleJsonLd,
                categorySlug: categorySlug,
                cwJobId: project.Id.ToString(),
                publishedAt: publishedAt,
                summary: articleRow?.Summary ?? string.Empty,
                mainSummary: articleRow?.MainSummary ?? string.Empty,
                heroSummary: articleRow?.HeroSummary ?? string.Empty,
                homeSummary: articleRow?.HomeSummary ?? string.Empty,
                blogSummary: articleRow?.BlogSummary ?? string.Empty,
                advertisingSummary: articleRow?.AdvertisingSummary ?? string.Empty,
                cancellationToken: cancellationToken));
        }

        var blogRow = project.GeneratedContents.FirstOrDefault(c => c.ContentType == GeneratedContentType.BlogPost);
        if (contentSet.Blog is not null
            && contentSet.Blog.WordCount > 0
            && !string.IsNullOrWhiteSpace(contentSet.BlogSlug))
        {
            published.Add(await PublishOneAsync(
                contentType: "blog",
                postType: "Blog",
                schemaType: "BlogPosting",
                slug: contentSet.BlogSlug!,
                title: contentSet.Blog.Title,
                metaDescription: contentSet.Blog.MetaDescription,
                bodyHtml: contentSet.Blog.BodyHtml,
                jsonLdOverride: contentSet.BlogJsonLd,
                categorySlug: categorySlug,
                cwJobId: project.Id.ToString(),
                publishedAt: publishedAt,
                summary: blogRow?.Summary ?? string.Empty,
                mainSummary: blogRow?.MainSummary ?? string.Empty,
                heroSummary: blogRow?.HeroSummary ?? string.Empty,
                homeSummary: blogRow?.HomeSummary ?? string.Empty,
                blogSummary: blogRow?.BlogSummary ?? string.Empty,
                advertisingSummary: blogRow?.AdvertisingSummary ?? string.Empty,
                cancellationToken: cancellationToken));
        }

        var toolRows = project.GeneratedContents
            .Where(c => c.ContentType == GeneratedContentType.ToolPost
                && c.WordCount > 0
                && !string.IsNullOrWhiteSpace(c.Slug))
            .OrderBy(c => c.SourceAppOrder ?? int.MaxValue)
            .ToList();

        foreach (var toolRow in toolRows)
        {
            published.Add(await PublishOneAsync(
                contentType: "tool",
                postType: "Tool",
                schemaType: "SoftwareApplication",
                slug: toolRow.Slug,
                title: string.IsNullOrWhiteSpace(toolRow.DisplayTitle) ? toolRow.Title : toolRow.DisplayTitle!,
                metaDescription: toolRow.MetaDescription ?? string.Empty,
                bodyHtml: toolRow.BodyHtml,
                jsonLdOverride: toolRow.JsonLdSchema,
                categorySlug: categorySlug,
                cwJobId: project.Id.ToString(),
                publishedAt: publishedAt,
                summary: toolRow.Summary,
                mainSummary: toolRow.MainSummary,
                heroSummary: toolRow.HeroSummary,
                homeSummary: toolRow.HomeSummary,
                blogSummary: toolRow.BlogSummary,
                advertisingSummary: toolRow.AdvertisingSummary,
                cancellationToken: cancellationToken));
        }

        if (published.Count == 0)
        {
            throw new ContentGenerationException(
                "Nothing to publish. Generate the pillar body and/or blog content first.");
        }

        return new GeekBlogPublishResult(categorySlug, published);
    }

    private async Task<GeekBlogPublishedPost> PublishOneAsync(
        string contentType,
        string postType,
        string schemaType,
        string slug,
        string title,
        string metaDescription,
        string bodyHtml,
        string? jsonLdOverride,
        string categorySlug,
        string cwJobId,
        DateTimeOffset publishedAt,
        string summary,
        string mainSummary,
        string heroSummary,
        string homeSummary,
        string blogSummary,
        string advertisingSummary,
        CancellationToken cancellationToken)
    {
        var sections = ArticleHtmlSectionExtractor.Split(bodyHtml)
            .Select(s => new GeekBlogSectionPayload(s.SortOrder, s.HeadingTag, s.HeadingText, s.BodyContent, s.MediaUrl, s.MediaAlt))
            .ToList();

        // LLM-written alongside the other summary variants (GenerateSummaryVariantsAsync), so it's
        // enforced distinct from MetaDescription and them. Body-derived fallback only covers content
        // generated before this field existed.
        if (string.IsNullOrWhiteSpace(summary))
            summary = DeriveSummaryFromBody(bodyHtml);

        var payload = new GeekBlogPostPayload(
            PostType: postType,
            SchemaType: schemaType,
            IsPublished: true,
            LanguageCode: DefaultLanguageCode,
            Slug: slug,
            Title: title,
            Summary: summary,
            MetaDescription: string.IsNullOrWhiteSpace(metaDescription) ? null : metaDescription,
            MainSummary: mainSummary,
            HeroSummary: heroSummary,
            HomeSummary: homeSummary,
            BlogSummary: blogSummary,
            AdvertisingSummary: advertisingSummary,
            JsonLdOverride: jsonLdOverride,
            Sections: sections,
            TagSlugs: [categorySlug],
            AuthorId: _geekBackendOptions.DefaultAuthorId,
            PublishedAt: publishedAt,
            CategorySlug: categorySlug,
            Presentation: null,
            CwJobId: cwJobId);

        var existingPostId = await _geekBackendClient.FindExistingPostIdAsync(slug, DefaultLanguageCode, cancellationToken);
        var result = await _geekBackendClient.UpsertPostAsync(payload, existingPostId, cancellationToken);

        _logger.LogInformation(
            "Published {ContentType} {Slug} to GeekBackend as post {PostId} ({Action}, {SectionCount} sections)",
            contentType, slug, result.PostId, result.WasUpdated ? "updated" : "created", result.SectionCount);

        return new GeekBlogPublishedPost(contentType, result.PostId, result.Slug, result.LanguageCode, result.SectionCount, result.WasUpdated);
    }

    private static string DeriveSummaryFromBody(string bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
            return string.Empty;

        var firstParagraphMatch = Regex.Match(
            bodyHtml, @"<p[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var source = firstParagraphMatch.Success ? firstParagraphMatch.Groups[1].Value : bodyHtml;
        var stripped = Regex.Replace(source, "<[^>]+>", " ").Trim();
        stripped = Regex.Replace(stripped, @"\s+", " ");

        return stripped.Length > 500 ? stripped[..500].TrimEnd() : stripped;
    }
}
