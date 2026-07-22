using ContentWriter.Domain.Entities;

namespace ContentWriter.Application.Services;

/// <summary>
/// Plain-text projections of a <see cref="ContentDocument"/> tree — for word counts, flat-text
/// contexts (social/email/image-prompt content, review prompts), and section-heading lookups.
/// Everything here is a tree walk over already-structured data; nothing re-parses a text blob.
/// </summary>
public static class ContentDocumentText
{
    /// <summary>Wraps flat text (social posts, emails, image-gen prompts) as a degenerate document —
    /// same shape as sectioned content, lede-only, no further sections. See design plan §1.</summary>
    public static ContentDocument FromPlainText(string text) => new(
        new Section("h2", text.Length > 80 ? text[..80] : text, [new TextParagraph([new Run(text)])], null, []),
        []);

    public static string Flatten(ContentDocument? document)
    {
        if (document is null)
        {
            return string.Empty;
        }

        var parts = new List<string> { FlattenSection(document.Lede) };
        parts.AddRange(document.Sections.Select(FlattenSection));
        return string.Join("\n\n", parts.Where(p => p.Length > 0));
    }

    public static int CountWords(ContentDocument? document) =>
        Flatten(document).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public static int CountWords(Section section) =>
        FlattenSection(section).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public static int CountWords(IReadOnlyList<Section> sections) =>
        string.Join("\n", sections.Select(FlattenSection)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Top-level (h2) section headings, in document order — used for image-prompt section targeting.</summary>
    public static IReadOnlyList<string> TopLevelHeadings(ContentDocument? document) =>
        document?.Sections.Select(s => s.Heading).ToList() ?? [];

    public static Section? FindTopLevelSection(ContentDocument document, string heading) =>
        document.Sections.FirstOrDefault(s => string.Equals(s.Heading, heading, StringComparison.OrdinalIgnoreCase));

    /// <summary>Appends a real, code-authored closing CTA link to the last top-level section (or the
    /// lede, if there are no sections). The model is explicitly told not to write this link itself —
    /// only the orchestrator knows the real URL at generation time, so assigning it here is a field
    /// write on already-parsed data, never a placeholder href for the model to guess at.</summary>
    public static ContentDocument AppendClosingLink(ContentDocument document, string linkText, string href)
    {
        var ctaParagraph = new TextParagraph([new Run(linkText, Href: href)]);

        if (document.Sections.Count == 0)
        {
            var lede = document.Lede with { Paragraphs = [.. document.Lede.Paragraphs, ctaParagraph] };
            return document with { Lede = lede };
        }

        var sections = document.Sections.ToList();
        var lastIndex = sections.Count - 1;
        sections[lastIndex] = sections[lastIndex] with { Paragraphs = [.. sections[lastIndex].Paragraphs, ctaParagraph] };
        return document with { Sections = sections };
    }

    /// <summary>Builds the ordered list of section image-prompt targets from already-structured
    /// top-level headings — no markdown heading parsing needed, since the tree is already structured.</summary>
    public static IReadOnlyList<ImagePromptSectionTarget> BuildSectionTargets(
        string? pillarTitle,
        ContentDocument? pillarDocument,
        string? blogTitle,
        ContentDocument? blogDocument,
        IReadOnlyList<string>? toolTitles = null)
    {
        var targets = new List<ImagePromptSectionTarget>();

        if (!string.IsNullOrWhiteSpace(pillarTitle))
        {
            targets.Add(new ImagePromptSectionTarget("pillar-hero", pillarTitle, 0));
        }

        if (!string.IsNullOrWhiteSpace(blogTitle))
        {
            targets.Add(new ImagePromptSectionTarget("blog-hero", blogTitle, 0));
        }

        var order = 1;
        foreach (var heading in TopLevelHeadings(pillarDocument))
        {
            targets.Add(new ImagePromptSectionTarget("pillar", heading, order++));
        }

        order = 1;
        foreach (var heading in TopLevelHeadings(blogDocument))
        {
            targets.Add(new ImagePromptSectionTarget("blog", heading, order++));
        }

        order = 1;
        foreach (var title in toolTitles ?? [])
        {
            targets.Add(new ImagePromptSectionTarget("tool", title, order++));
        }

        return targets;
    }

    private static string FlattenSection(Section section)
    {
        var parts = new List<string> { section.Heading };
        parts.AddRange(section.Paragraphs.SelectMany(FlattenParagraph));
        parts.AddRange(section.Children.Select(FlattenSection));
        return string.Join("\n", parts.Where(p => p.Length > 0));
    }

    private static IEnumerable<string> FlattenParagraph(Paragraph paragraph) => paragraph switch
    {
        TextParagraph text => [string.Join(" ", text.Runs.Select(r => r.Text))],
        ListParagraph list => list.Items.Select(item => string.Join(" ", item.Select(r => r.Text))),
        _ => [],
    };
}

public sealed record ImagePromptSectionTarget(string SourceType, string Heading, int Order);
