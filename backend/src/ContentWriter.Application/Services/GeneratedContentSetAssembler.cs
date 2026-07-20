using ContentWriter.Application.DTOs;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Application.Services;

public static class GeneratedContentSetAssembler
{
    public static GeneratedContentSet Assemble(
        Project project,
        string department,
        string articleBaseUrl,
        string blogBaseUrl,
        string toolBaseUrl)
    {
        var articleRow = Find(project, GeneratedContentType.TechnicalArticle);
        var blogRow = Find(project, GeneratedContentType.BlogPost);
        var facebookRow = Find(project, GeneratedContentType.SocialFacebook);
        var linkedInRow = Find(project, GeneratedContentType.SocialLinkedIn);
        var coldOutreachRow = Find(project, GeneratedContentType.EmailColdOutreach);

        var articleSlug = articleRow?.Slug;
        var articleUrl = articleSlug is null ? null : CombineUrl(articleBaseUrl, department, articleSlug);
        var blogSlug = blogRow?.Slug;
        var blogUrl = blogSlug is null ? null : CombineUrl(blogBaseUrl, department, blogSlug);

        return new GeneratedContentSet(
            Article: articleRow is null ? null : ToArticleDraft(articleRow),
            ArticleSlug: articleSlug,
            ArticleUrl: articleUrl,
            ArticleJsonLd: articleRow?.JsonLdSchema,
            Blog: blogRow is null ? null : ToBlogDraft(blogRow),
            BlogSlug: blogSlug,
            BlogUrl: blogUrl,
            BlogJsonLd: blogRow?.JsonLdSchema,
            FacebookPost: facebookRow is null ? null : new SocialPostDraft("Facebook", facebookRow.BodyHtml),
            LinkedInPost: linkedInRow is null ? null : new SocialPostDraft("LinkedIn", linkedInRow.BodyHtml),
            ColdOutreachEmail: coldOutreachRow is null
                ? null
                : new ColdOutreachEmailContent(
                    coldOutreachRow.Title,
                    coldOutreachRow.BodyHtml,
                    coldOutreachRow.MetaDescription ?? string.Empty,
                    coldOutreachRow.RelatedArticleUrl ?? articleUrl ?? string.Empty),
            ImagePrompts: BuildImagePrompts(project),
            ToolPosts: BuildToolPosts(project, department, toolBaseUrl));
    }

    private static IReadOnlyList<ToolPostContent>? BuildToolPosts(Project project, string department, string toolBaseUrl)
    {
        var toolRows = project.GeneratedContents
            .Where(c => c.ContentType == GeneratedContentType.ToolPost)
            .OrderBy(c => c.SourceAppOrder)
            .Select(c => new ToolPostContent(
                c.Title,
                c.Slug,
                CombineUrl(toolBaseUrl, department, c.Slug),
                c.BodyHtml,
                c.MetaDescription ?? string.Empty,
                c.JsonLdSchema,
                c.SourceAppOrder))
            .ToList();

        return toolRows.Count == 0 ? null : toolRows;
    }

    private static ImagePromptsContent? BuildImagePrompts(Project project)
    {
        var sectionRows = project.GeneratedContents
            .Where(c => c.ContentType == GeneratedContentType.ImagePromptSection)
            .Select(ImagePromptMetadata.ToSectionContent)
            .OrderBy(s => s.SourceType == "pillar" ? 0 : 1)
            .ThenBy(s => s.Order)
            .ToList();

        return sectionRows.Count == 0 ? null : new ImagePromptsContent(sectionRows);
    }

    public static ArticleDraft ToArticleDraft(GeneratedContent row) => new(
        row.Title,
        row.MetaDescription ?? string.Empty,
        row.BodyHtml,
        row.Keywords,
        row.WordCount,
        row.SectionOutline);

    public static BlogDraft ToBlogDraft(GeneratedContent row) => new(
        row.Title,
        row.MetaDescription ?? string.Empty,
        row.BodyHtml,
        row.Keywords,
        row.WordCount,
        row.SectionOutline);

    private static GeneratedContent? Find(Project project, GeneratedContentType type) =>
        project.GeneratedContents.FirstOrDefault(c => c.ContentType == type);

    private static string CombineUrl(string baseUrl, string department, string slug) =>
        $"{baseUrl.TrimEnd('/')}/{department}/{slug}";
}
