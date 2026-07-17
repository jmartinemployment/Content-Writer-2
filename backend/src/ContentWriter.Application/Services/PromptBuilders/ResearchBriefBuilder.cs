using System.Text;
using ContentWriter.Application.DTOs;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Application.Services.PromptBuilders;

internal enum ResearchBriefPhase
{
    ArticleMetadata,
    ArticleBody,
    ArticleSection,
    ArticleFaq
}

/// <summary>
/// Builds phase-specific research briefs so each LLM call only receives the context it needs.
/// Keyword SERP files stay tight (intent/ranking patterns); Wikipedia/.edu/.gov are body-only sources to quote.
/// </summary>
internal static class ResearchBriefBuilder
{
    private static readonly KeywordSourceCategory[] AuthoritativeCategories =
    [
        KeywordSourceCategory.Wikipedia,
        KeywordSourceCategory.EduDomain,
        KeywordSourceCategory.GovDomain
    ];

    public static string Build(ProjectGenerationContext context, ResearchBriefPhase phase, string instructions)
    {
        var sb = new StringBuilder();

        switch (phase)
        {
            case ResearchBriefPhase.ArticleMetadata:
                AppendCompactSiteContext(sb, context, includeJsonLd: false);
                AppendKeywordSerpBrief(sb, context, maxHeadingsPerFile: 6, maxParagraphsPerFile: 0);
                AppendCompetitorGapsBrief(sb, context);
                AppendPaaBrief(sb, context, forFaqSectionOnly: true);
                break;

            case ResearchBriefPhase.ArticleBody:
                AppendCompactSiteContext(sb, context, includeJsonLd: true);
                AppendKeywordSerpBrief(sb, context, maxHeadingsPerFile: 4, maxParagraphsPerFile: 2);
                AppendAuthoritativeSourcesBrief(sb, context);
                AppendCompetitorGapsBrief(sb, context);
                // PAA listed only in the body prompt FAQ block — omit here to avoid FAQ-shaped articles.
                break;

            case ResearchBriefPhase.ArticleSection:
                AppendKeywordSerpBrief(sb, context, maxHeadingsPerFile: 3, maxParagraphsPerFile: 1);
                AppendAuthoritativeSourcesBrief(sb, context);
                break;

            case ResearchBriefPhase.ArticleFaq:
                AppendAuthoritativeSourcesBrief(sb, context);
                break;
        }

        sb.AppendLine();
        sb.AppendLine("=== INSTRUCTIONS ===");
        sb.AppendLine(instructions);

        return sb.ToString();
    }

    private static void AppendCompactSiteContext(StringBuilder sb, ProjectGenerationContext context, bool includeJsonLd)
    {
        sb.AppendLine($"=== PROJECT SITE: {context.SiteName} ({context.ProjectUrl}) ===");
        sb.AppendLine($"Brand tone: {context.DetectedTone}");
        sb.AppendLine($"Site focus: {context.DetectedFocus}");

        if (context.CrawledHeadings.Count > 0)
        {
            sb.AppendLine("Key site headings:");
            foreach (var h in context.CrawledHeadings.Take(12)) sb.AppendLine($"- {h}");
        }

        if (context.CrawledParagraphs.Count > 0)
        {
            sb.AppendLine("Representative site copy:");
            foreach (var p in context.CrawledParagraphs.Take(5)) sb.AppendLine($"- {p}");
        }

        if (includeJsonLd && !string.IsNullOrWhiteSpace(context.JsonLdStructuredSummary))
        {
            sb.AppendLine();
            sb.AppendLine(context.JsonLdStructuredSummary);
        }
    }

    private static void AppendKeywordSerpBrief(
        StringBuilder sb,
        ProjectGenerationContext context,
        int maxHeadingsPerFile,
        int maxParagraphsPerFile)
    {
        var keywordSources = context.KeywordSources
            .Where(s => s.Category == KeywordSourceCategory.KeywordResult)
            .ToList();

        if (keywordSources.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("=== KEYWORD SERP (search intent — prefer declarative headings for pillar outline, not questions) ===");
        foreach (var source in keywordSources)
        {
            var label = FormatSourceLabel(source);
            sb.AppendLine($"[{label}]");
            foreach (var h in source.Headings.Take(maxHeadingsPerFile)) sb.AppendLine($"- {h}");
            foreach (var p in source.Paragraphs.Take(maxParagraphsPerFile)) sb.AppendLine($"- {p}");
        }
    }

    private static void AppendAuthoritativeSourcesBrief(StringBuilder sb, ProjectGenerationContext context)
    {
        var sources = context.KeywordSources
            .Where(s => AuthoritativeCategories.Contains(s.Category))
            .ToList();

        if (sources.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("=== AUTHORITATIVE SOURCES (quote, paraphrase, and attribute facts to these) ===");
        foreach (var source in sources)
        {
            var label = FormatSourceLabel(source);
            sb.AppendLine($"[{label}]");
            foreach (var h in source.Headings.Take(6)) sb.AppendLine($"- {h}");
            foreach (var p in source.Paragraphs.Take(8)) sb.AppendLine($"- {p}");
        }
    }

    private static void AppendCompetitorGapsBrief(StringBuilder sb, ProjectGenerationContext context)
    {
        var sources = context.KeywordSources
            .Where(s => s.Category is KeywordSourceCategory.CompetitorCrawl or KeywordSourceCategory.Local)
            .ToList();

        if (sources.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("=== COMPETITOR / LOCAL SERP (headings only — identify gaps to cover better) ===");
        foreach (var source in sources)
        {
            var label = FormatSourceLabel(source);
            sb.AppendLine($"[{label}]");
            foreach (var h in source.Headings.Take(8)) sb.AppendLine($"- {h}");
        }
    }

    private static void AppendPaaBrief(StringBuilder sb, ProjectGenerationContext context, bool forFaqSectionOnly)
    {
        if (context.PeopleAlsoAskQuestions.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        if (forFaqSectionOnly)
        {
            sb.AppendLine("=== PEOPLE ALSO ASK (dedicated FAQ section at end — H2 \"People Also Ask\", each question as H3) ===");
        }
        else
        {
            sb.AppendLine("=== PEOPLE ALSO ASK (answer naturally in the body) ===");
        }

        foreach (var q in context.PeopleAlsoAskQuestions.Take(15)) sb.AppendLine($"- {q}");
    }

    private static string FormatSourceLabel(KeywordSourceSummary source) =>
        !string.IsNullOrWhiteSpace(source.Title) ? source.Title! : source.SourceLabel;
}
