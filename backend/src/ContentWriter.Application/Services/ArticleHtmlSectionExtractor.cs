using Markdig;
using Markdig.Syntax;

namespace ContentWriter.Application.Services;

/// <summary>
/// One node in the nested H2-H6 heading tree for a Markdown body — each heading individually
/// addressable (e.g. sections[1].children[0].heading) rather than bundled into one blob per H2.
/// Level 0 with a null heading holds any content before the first H2.
/// </summary>
public sealed record MarkdownSection(
    int Level,
    string? Heading,
    string Body,
    IReadOnlyList<MarkdownSection> Children);

public static class ArticleHtmlSectionExtractor
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>
    /// Splits a Markdown body into a nested heading tree (H2 at the root, H3-H6 nested under their
    /// parent heading) using Markdig's AST. A heading's Body is only the text directly under it,
    /// before its first child heading — subheadings live in Children, not concatenated into Body.
    /// </summary>
    public static IReadOnlyList<MarkdownSection> SplitTree(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return [];

        var headings = AllHeadings(markdown, minLevel: 2, maxLevel: 6);
        if (headings.Count == 0)
        {
            var whole = markdown.Trim();
            return whole.Length == 0 ? [] : [new MarkdownSection(0, null, whole, [])];
        }

        var result = new List<MarkdownSection>();
        if (headings[0].Span.Start > 0)
        {
            var lead = markdown[..headings[0].Span.Start].Trim();
            if (lead.Length > 0)
            {
                result.Add(new MarkdownSection(0, null, lead, []));
            }
        }

        var index = 0;
        result.AddRange(BuildLevel(markdown, headings, ref index, minLevel: 2));
        return result;
    }

    private static List<MarkdownSection> BuildLevel(
        string markdown,
        IReadOnlyList<(string Text, int Level, SourceSpan Span)> headings,
        ref int index,
        int minLevel)
    {
        var nodes = new List<MarkdownSection>();

        while (index < headings.Count && headings[index].Level >= minLevel)
        {
            var current = headings[index];
            index++;

            var bodyStart = current.Span.End + 1;
            var bodyEnd = index < headings.Count ? headings[index].Span.Start : markdown.Length;
            var body = bodyStart < bodyEnd ? markdown[bodyStart..bodyEnd].Trim() : string.Empty;

            var children = index < headings.Count && headings[index].Level > current.Level
                ? BuildLevel(markdown, headings, ref index, current.Level + 1)
                : [];

            nodes.Add(new MarkdownSection(current.Level, current.Text, body, children));
        }

        return nodes;
    }

    private static IReadOnlyList<(string Text, int Level, SourceSpan Span)> AllHeadings(
        string markdown, int minLevel, int maxLevel)
    {
        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var result = new List<(string, int, SourceSpan)>();

        foreach (var block in document)
        {
            if (block is HeadingBlock heading && heading.Level >= minLevel && heading.Level <= maxLevel)
            {
                result.Add((HeadingText(markdown, heading), heading.Level, heading.Span));
            }
        }

        return result;
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
