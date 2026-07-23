using ContentWriter.Application.Services.Export;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Application.DTOs;

/// <summary>Everything the orchestrator needs to know about a project to generate its content set.</summary>
public record ProjectGenerationContext(
    string ProjectName,
    string ProjectUrl,
    string TargetKeyword,
    string Department,
    string SiteName,
    string DetectedTone,
    string DetectedFocus,
    List<string> CrawledHeadings,
    List<string> CrawledParagraphs,
    string? JsonLdStructuredSummary,
    List<KeywordSourceSummary> KeywordSources,
    List<string> PeopleAlsoAskQuestions,
    string PublisherName,
    string PublisherLogoUrl,
    string AuthorName,
    string ArticleBaseUrl,
    string BlogBaseUrl,
    string ToolBaseUrl,
    string ImplementerPositioning,
    LlmProviderType Provider,
    bool UseExactKeywordAsTitle = false);

public record TopicFocusResponse(string[]? Focus);

public record KeywordSourceSummary(
    KeywordSourceCategory Category,
    string? Title,
    string SourceLabel,
    List<string> Headings,
    List<string> Paragraphs);

public record ArticleMetadataDraft(
    string Title,
    string MetaDescription,
    List<string> Keywords,
    List<string> SectionOutline);

public record BlogMetadataDraft(
    string Title,
    string MetaDescription,
    List<string> Keywords,
    List<string> SectionOutline);

/// <summary>
/// <see cref="Body"/> is the structural source of truth (per-element addressing, e.g.
/// Body.Sections[1].Heading). <see cref="BodyHtml"/> is a computed wire/display convenience —
/// the same rendered fragment the export step would produce — so the frontend can render it
/// directly without needing its own copy of the Section-tree-to-HTML renderer.
/// </summary>
public record ArticleDraft(
    string Title,
    string MetaDescription,
    ContentDocument Body,
    List<string> Keywords,
    int WordCount,
    List<string> SectionOutline)
{
    public string BodyHtml => SectionHtmlRenderer.RenderFragment(Body);
}

public record BlogDraft(
    string Title,
    string MetaDescription,
    ContentDocument Body,
    List<string> Keywords,
    int WordCount,
    List<string> SectionOutline)
{
    public string BodyHtml => SectionHtmlRenderer.RenderFragment(Body);
}

public record ToolMetadataDraft(
    string DepartmentListExcerpt,
    string Summary,
    string MainSummary,
    string HeroSummary,
    string HomeSummary,
    string BlogSummary,
    string ToolPageExcerpt,
    string AdvertisingSummary,
    string MetaDescription);

public record SummaryVariantsDraft(
    string Summary,
    string MainSummary,
    string HeroSummary,
    string HomeSummary,
    string BlogSummary,
    string AdvertisingSummary);

public record SocialPostDraft(string Platform, string Text);

public record ColdOutreachEmailDraft(string Subject, string BodyText, string CtaLabel);

public record ColdOutreachEmailContent(string Subject, string BodyText, string CtaLabel, string CtaUrl);

public record ImagePromptItemDraft(
    string Prompt,
    int Width,
    int Height,
    string ImageModel,
    string StylePreset,
    bool Alchemy,
    bool PhotoReal,
    string? Notes);

public record ImagePromptSectionDraft(
    string SourceType,
    string Heading,
    int Order,
    string Prompt,
    int Width,
    int Height,
    string ImageModel,
    string StylePreset,
    bool Alchemy,
    bool PhotoReal,
    string? Notes);

public record ImagePromptSectionPromptsDraft(IReadOnlyList<ImagePromptSectionDraft> Sections);

public record ImagePromptSectionContent(
    string SourceType,
    string Heading,
    int Order,
    string Prompt,
    int Width,
    int Height,
    string ImageModel,
    string ImageModelId,
    string StylePreset,
    bool Alchemy,
    bool PhotoReal,
    string? Notes);

public record ImagePromptsContent(IReadOnlyList<ImagePromptSectionContent> Sections);

public record ToolPostContent(
    string Title,
    string Slug,
    string ToolUrl,
    ContentDocument Body,
    string MetaDescription,
    string? JsonLdSchema,
    int? SourceAppOrder)
{
    public string BodyHtml => SectionHtmlRenderer.RenderFragment(Body);
}

public record GeneratedContentSet(
    ArticleDraft? Article,
    string? ArticleSlug,
    string? ArticleUrl,
    string? ArticleJsonLd,
    BlogDraft? Blog,
    string? BlogSlug,
    string? BlogUrl,
    string? BlogJsonLd,
    SocialPostDraft? FacebookPost,
    SocialPostDraft? LinkedInPost,
    ColdOutreachEmailContent? ColdOutreachEmail,
    ImagePromptsContent? ImagePrompts,
    IReadOnlyList<ToolPostContent>? ToolPosts);
