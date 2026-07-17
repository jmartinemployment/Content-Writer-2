using ContentWriter.Domain.Enums;

namespace ContentWriter.Domain.Entities;

public class GeneratedContent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public GeneratedContentType ContentType { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Clean H1 for the live page. Falls back to <see cref="Title"/> when unset.</summary>
    public string? DisplayTitle { get; set; }

    public string Slug { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>GeekBackend post_translations.summary — LLM-written, distinct from MetaDescription and every other summary variant (pillar, tool, blog).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Main-page summary (pillar, tool, blog).</summary>
    public string MainSummary { get; set; } = string.Empty;

    /// <summary>Blurb under the page H1 (pillar, tool, blog).</summary>
    public string HeroSummary { get; set; } = string.Empty;

    /// <summary>Home-page feature card copy (pillar, tool, blog).</summary>
    public string HomeSummary { get; set; } = string.Empty;

    /// <summary>Blog-listing teaser copy (pillar, tool, blog).</summary>
    public string BlogSummary { get; set; } = string.Empty;

    /// <summary>Department hub listing copy (/use-cases/{dept}, /tools/{dept}, /blog/{dept}).</summary>
    public string DepartmentListExcerpt { get; set; } = string.Empty;

    /// <summary>Tool page content slot (tool rows only).</summary>
    public string ToolPageExcerpt { get; set; } = string.Empty;

    /// <summary>Sponsored ad copy — not an excerpt (pillar, tool, blog).</summary>
    public string AdvertisingSummary { get; set; } = string.Empty;

    /// <summary>Top Tools app name this tool row was generated from (tool posts only).</summary>
    public string? SourceAppName { get; set; }

    /// <summary>Order within the pillar Top Tools section (tool posts only).</summary>
    public int? SourceAppOrder { get; set; }

    public string? MetaDescription { get; set; }
    public List<string> Keywords { get; set; } = new();
    public int WordCount { get; set; }

    /// <summary>H2 section topics from the plan step; guides the body step.</summary>
    public List<string> SectionOutline { get; set; } = new();

    /// <summary>Serialized JSON+LD object (TechnicalArticle or BlogPosting schema). Null for social posts.</summary>
    public string? JsonLdSchema { get; set; }

    /// <summary>For blog posts: the canonical URL/anchor of the TechnicalArticle it links back to.</summary>
    public string? RelatedArticleUrl { get; set; }

    public LlmProviderType GeneratedByProvider { get; set; }
    public string GeneratedByModel { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<ReviewVerdict> ReviewVerdicts { get; set; } = new();
}
