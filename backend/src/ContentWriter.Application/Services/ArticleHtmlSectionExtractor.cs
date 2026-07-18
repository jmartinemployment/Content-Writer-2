using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace ContentWriter.Application.Services;

/// <summary>One flat HTML section, matching a geek_blog.post_sections row.</summary>
public sealed record HtmlSection(
    int SortOrder,
    string? HeadingTag,
    string? HeadingText,
    string BodyContent,
    string? MediaUrl = null,
    string? MediaAlt = null);

public static class ArticleHtmlSectionExtractor
{
    private static readonly Regex H2Regex = new(
        @"<h2[^>]*>(.*?)</h2>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Splits a single HTML blob into ordered <see cref="HtmlSection"/> rows on &lt;h2&gt; boundaries.
    /// Any content before the first &lt;h2&gt; becomes sort_order 0 with a null heading.
    /// Mirrors GeekBackend's GeekApplication/Blog/HtmlSectionSplitter.cs splitting rule exactly.
    /// </summary>
    public static IReadOnlyList<HtmlSection> Split(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var sections = new List<HtmlSection>();
        var sortOrder = 0;
        string? currentHeadingTag = null;
        string? currentHeadingText = null;
        var buffer = new StringBuilder();

        void Flush()
        {
            var bodyContent = buffer.ToString().Trim();
            if (bodyContent.Length == 0 && currentHeadingTag is null)
            {
                buffer.Clear();
                return;
            }

            sections.Add(new HtmlSection(sortOrder, currentHeadingTag, currentHeadingText, bodyContent));
            sortOrder++;
            buffer.Clear();
        }

        foreach (var node in doc.DocumentNode.ChildNodes.ToList())
        {
            if (node.NodeType == HtmlNodeType.Element
                && string.Equals(node.Name, "h2", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                currentHeadingTag = "h2";
                currentHeadingText = HtmlEntity.DeEntitize(node.InnerText)?.Trim() ?? string.Empty;
                continue;
            }

            buffer.Append(node.OuterHtml);
        }

        Flush();

        return sections;
    }

    public static IReadOnlyList<string> ExtractH2Headings(string? bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
            return [];

        return H2Regex.Matches(bodyHtml)
            .Select(m => StripTags(m.Groups[1].Value).Trim())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .ToList();
    }

    public static IReadOnlyList<ImagePromptSectionTarget> BuildSectionTargets(
        string? pillarBodyHtml,
        string? blogBodyHtml,
        IReadOnlyList<string>? toolTitles = null)
    {
        var targets = new List<ImagePromptSectionTarget>();
        var order = 1;

        foreach (var heading in ExtractH2Headings(pillarBodyHtml))
        {
            targets.Add(new ImagePromptSectionTarget("pillar", heading, order++));
        }

        order = 1;
        foreach (var heading in ExtractH2Headings(blogBodyHtml))
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

    private static string StripTags(string html) =>
        Regex.Replace(html, "<[^>]+>", " ").Trim();
}

public sealed record ImagePromptSectionTarget(string SourceType, string Heading, int Order);
