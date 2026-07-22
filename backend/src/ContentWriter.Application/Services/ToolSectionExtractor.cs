using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.SchemaBuilders;
using ContentWriter.Domain.Entities;

namespace ContentWriter.Application.Services;

/// <summary>
/// Extracts tool/platform descriptors from the pillar's already-structured Tools section, and
/// injects cross-reference links into it — both are now plain tree walks/field assignments over
/// the ContentDocument, since the section tree is already structured data (no text to re-parse).
/// </summary>
public static class ToolSectionExtractor
{
    public static IReadOnlyList<SoftwareApplicationDescriptor> ExtractApplications(
        ContentDocument? document,
        IReadOnlyList<string> sectionOutline) =>
        DiagnoseExtraction(document, sectionOutline).Applications;

    public static ToolExtractionResult DiagnoseExtraction(ContentDocument? document, IReadOnlyList<string> sectionOutline)
    {
        if (sectionOutline.Count == 0)
        {
            return new ToolExtractionResult(ToolGenerationOutcome.NoToolsSection, []);
        }

        var toolsHeading = sectionOutline.FirstOrDefault(PillarSectionClassifier.IsToolsSection);
        if (string.IsNullOrWhiteSpace(toolsHeading))
        {
            return new ToolExtractionResult(ToolGenerationOutcome.NoToolsSection, []);
        }

        var toolsSection = document is null ? null : ContentDocumentText.FindTopLevelSection(document, toolsHeading);
        if (toolsSection is null)
        {
            return new ToolExtractionResult(ToolGenerationOutcome.ToolsSectionNotFoundInBody, []);
        }

        var seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applications = new List<SoftwareApplicationDescriptor>();
        foreach (var platform in toolsSection.Children.Where(c => c.Tag == "h3"))
        {
            var name = platform.Heading;
            if (string.IsNullOrWhiteSpace(name)
                || name.StartsWith("How an AI implementer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!seenSlugs.Add(SlugHelper.Slugify(name)))
            {
                continue;
            }

            var description = platform.Paragraphs.OfType<TextParagraph>().FirstOrDefault() is { } firstParagraph
                ? string.Join(" ", firstParagraph.Runs.Select(r => r.Text))
                : null;

            // .Href is null until InjectToolLinks has run (tool pages don't exist yet the first time
            // this is called, during pillar-body generation) — populated on later calls once the
            // Tools section's child links have actually been assigned.
            applications.Add(new SoftwareApplicationDescriptor(name, description, platform.Href));
        }

        if (applications.Count == 0)
        {
            return new ToolExtractionResult(ToolGenerationOutcome.ToolsSectionEmpty, []);
        }

        return new ToolExtractionResult(ToolGenerationOutcome.Success, applications);
    }

    /// <summary>Returns a new document with each matching platform's child Section given an Href — a
    /// field assignment on already-parsed nodes, never regex/string surgery on markup.</summary>
    public static ContentDocument InjectToolLinks(
        ContentDocument document,
        IReadOnlyList<string> sectionOutline,
        string toolBaseUrl,
        IReadOnlyList<(string AppName, string ToolSlug)> tools)
    {
        if (tools.Count == 0)
        {
            return document;
        }

        var toolsHeading = sectionOutline.FirstOrDefault(PillarSectionClassifier.IsToolsSection);
        if (string.IsNullOrWhiteSpace(toolsHeading))
        {
            return document;
        }

        var hrefByName = tools.ToDictionary(
            t => t.AppName,
            t => $"{toolBaseUrl.TrimEnd('/')}/{t.ToolSlug}",
            StringComparer.OrdinalIgnoreCase);

        var newSections = document.Sections
            .Select(section => string.Equals(section.Heading, toolsHeading, StringComparison.OrdinalIgnoreCase)
                ? section with { Children = section.Children.Select(child => hrefByName.TryGetValue(child.Heading, out var href)
                    ? child with { Href = href }
                    : child).ToList() }
                : section)
            .ToList();

        return document with { Sections = newSections };
    }
}
