using ContentWriter.Application.Services.PromptBuilders;
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
    /// same shape as sectioned content, lede-only, no further sections. Heading is left empty so
    /// <see cref="Flatten"/> / HTML export do not duplicate a truncated prefix of the body
    /// (plain-text rows store the whole payload in the lede paragraph only).</summary>
    public static ContentDocument FromPlainText(string text) => new(
        new Section("h2", string.Empty, [new TextParagraph([new Run(text)])], null, []),
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

    /// <summary>Appends a "Related reading" list of real, code-authored links (to sibling pillar
    /// articles the LLM only ever named/echoed via title, never invented a URL for) to the last
    /// top-level section, or the lede if there are no sections. No-op when <paramref name="links"/> is empty.</summary>
    public static ContentDocument AppendRelatedPillarLinks(ContentDocument document, IReadOnlyList<RelatedPillarLink> links)
    {
        if (links.Count == 0)
        {
            return document;
        }

        var leadIn = new TextParagraph([new Run("Related reading:")]);
        var listParagraph = new ListParagraph(
            Ordered: false,
            Items: links.Select(l => (IReadOnlyList<Run>)[new Run(l.Title, Href: l.Url)]).ToList());

        if (document.Sections.Count == 0)
        {
            var lede = document.Lede with { Paragraphs = [.. document.Lede.Paragraphs, leadIn, listParagraph] };
            return document with { Lede = lede };
        }

        var sections = document.Sections.ToList();
        var lastIndex = sections.Count - 1;
        sections[lastIndex] = sections[lastIndex] with { Paragraphs = [.. sections[lastIndex].Paragraphs, leadIn, listParagraph] };
        return document with { Sections = sections };
    }

    /// <summary>Assigns stable, unique in-page <see cref="Section.Id"/> values (slugified headings)
    /// across the whole tree so TOC / jump links can target them. Empty headings stay null.</summary>
    public static ContentDocument AssignSectionIds(ContentDocument document)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        return document with
        {
            Lede = AssignSectionId(document.Lede, used),
            Sections = document.Sections.Select(s => AssignSectionId(s, used)).ToList(),
        };
    }

    /// <summary>Appends a code-authored "In this article" TOC to the lede, linking to each top-level
    /// section's <see cref="Section.Id"/>. Skips FAQ/PAA. No-op when there are no eligible sections.
    /// Call <see cref="AssignSectionIds"/> first so hrefs match heading ids on export.</summary>
    public static ContentDocument AppendSectionToc(ContentDocument document)
    {
        var tocEntries = document.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.Heading)
                        && !string.IsNullOrWhiteSpace(s.Id)
                        && !PillarOutlineNormalizer.IsFaqSectionTitle(s.Heading))
            .ToList();

        if (tocEntries.Count == 0)
        {
            return document;
        }

        var leadIn = new TextParagraph([new Run("In this article:")]);
        var listParagraph = new ListParagraph(
            Ordered: false,
            Items: tocEntries
                .Select(s => (IReadOnlyList<Run>)[new Run(s.Heading, Href: $"#{s.Id}")])
                .ToList());

        var lede = document.Lede with { Paragraphs = [.. document.Lede.Paragraphs, leadIn, listParagraph] };
        return document with { Lede = lede };
    }

    /// <summary>For every section that has nested children (e.g. keyword h3 → h4 subtopics), appends a
    /// code-authored "In this section" list of <c>#id</c> jump links to those children. Walks the
    /// whole tree depth-first. Call <see cref="AssignSectionIds"/> first. Skips FAQ parents.</summary>
    public static ContentDocument AppendChildSectionReferences(ContentDocument document) =>
        document with
        {
            Lede = AppendChildRefsToSection(document.Lede),
            Sections = document.Sections.Select(AppendChildRefsToSection).ToList(),
        };

    private static Section AppendChildRefsToSection(Section section)
    {
        var children = section.Children.Select(AppendChildRefsToSection).ToList();
        var withChildren = section with { Children = children };

        if (PillarOutlineNormalizer.IsFaqSectionTitle(withChildren.Heading))
        {
            return withChildren;
        }

        var linkable = children
            .Where(c => !string.IsNullOrWhiteSpace(c.Heading) && !string.IsNullOrWhiteSpace(c.Id))
            .ToList();

        if (linkable.Count == 0)
        {
            return withChildren;
        }

        var leadIn = new TextParagraph([new Run("In this section:")]);
        var listParagraph = new ListParagraph(
            Ordered: false,
            Items: linkable
                .Select(c => (IReadOnlyList<Run>)[new Run(c.Heading, Href: $"#{c.Id}")])
                .ToList());

        return withChildren with { Paragraphs = [.. withChildren.Paragraphs, leadIn, listParagraph] };
    }

    private static Section AssignSectionId(Section section, ISet<string> used)
    {
        string? id = null;
        if (!string.IsNullOrWhiteSpace(section.Heading))
        {
            id = SlugHelper.EnsureUniqueSlug(SlugHelper.Slugify(section.Heading), used);
        }

        return section with
        {
            Id = id,
            Children = section.Children.Select(c => AssignSectionId(c, used)).ToList(),
        };
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
