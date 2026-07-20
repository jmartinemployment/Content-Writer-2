using Markdig;
using Markdig.Syntax;

namespace ContentWriter.Application.Services;

/// <summary>One flat section split from a Markdown body, matching a geek_blog.post_sections row.</summary>
public sealed record HtmlSection(
    int SortOrder,
    string? HeadingTag,
    string? HeadingText,
    string BodyContent,
    string? MediaUrl = null,
    string? MediaAlt = null);

public static class ArticleHtmlSectionExtractor
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>
    /// Splits a Markdown body into ordered <see cref="HtmlSection"/> rows on H2 ("## ") boundaries,
    /// using Markdig's AST rather than regex so code fences/blockquotes containing "##" aren't
    /// mistaken for headings. Any content before the first H2 becomes sort_order 0 with a null heading.
    /// </summary>
    public static IReadOnlyList<HtmlSection> Split(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return [];

        var h2s = TopLevelHeadings(markdown, level: 2);
        if (h2s.Count == 0)
        {
            var whole = markdown.Trim();
            return whole.Length == 0 ? [] : [new HtmlSection(0, null, null, whole)];
        }

        var sections = new List<HtmlSection>();
        var sortOrder = 0;

        if (h2s[0].Span.Start > 0)
        {
            var lead = markdown[..h2s[0].Span.Start].Trim();
            if (lead.Length > 0)
            {
                sections.Add(new HtmlSection(sortOrder++, null, null, lead));
            }
        }

        for (var i = 0; i < h2s.Count; i++)
        {
            var bodyStart = h2s[i].Span.End + 1;
            var bodyEnd = i + 1 < h2s.Count ? h2s[i + 1].Span.Start : markdown.Length;
            var body = bodyStart < bodyEnd ? markdown[bodyStart..bodyEnd].Trim() : string.Empty;

            sections.Add(new HtmlSection(sortOrder++, "h2", h2s[i].Text, body));
        }

        return sections;
    }

    public static IReadOnlyList<string> ExtractH2Headings(string? bodyMarkdown)
    {
        if (string.IsNullOrWhiteSpace(bodyMarkdown))
            return [];

        return TopLevelHeadings(bodyMarkdown, level: 2).Select(h => h.Text).ToList();
    }

    public static IReadOnlyList<ImagePromptSectionTarget> BuildSectionTargets(
        string? pillarBodyMarkdown,
        string? blogBodyMarkdown,
        IReadOnlyList<string>? toolTitles = null)
    {
        var targets = new List<ImagePromptSectionTarget>();
        var order = 1;

        foreach (var heading in ExtractH2Headings(pillarBodyMarkdown))
        {
            targets.Add(new ImagePromptSectionTarget("pillar", heading, order++));
        }

        order = 1;
        foreach (var heading in ExtractH2Headings(blogBodyMarkdown))
        {
            targets.Add(new ImagePromptSectionTarget("blog", heading, order++));
        }

        // One prompt per tool page (not per-H2 within it) — a single hero/overview image per tool.
        order = 1;
        foreach (var title in toolTitles ?? [])
        {
            targets.Add(new ImagePromptSectionTarget("tool", title, order++));
        }

        return targets;
    }

    /// <summary>All ATX headings at the given level, in document order, with their source span and text.</summary>
    internal static IReadOnlyList<(string Text, Markdig.Syntax.SourceSpan Span)> TopLevelHeadings(string markdown, int level)
    {
        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var result = new List<(string, Markdig.Syntax.SourceSpan)>();

        foreach (var block in document)
        {
            if (block is HeadingBlock heading && heading.Level == level)
            {
                result.Add((HeadingText(markdown, heading), heading.Span));
            }
        }

        return result;
    }

    internal static string HeadingText(string markdown, HeadingBlock heading)
    {
        var raw = markdown[heading.Span.Start..(heading.Span.End + 1)];
        return raw.TrimStart('#').Trim();
    }
}

public sealed record ImagePromptSectionTarget(string SourceType, string Heading, int Order);
