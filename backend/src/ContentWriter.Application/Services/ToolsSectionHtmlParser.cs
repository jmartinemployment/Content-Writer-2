using System.Text.RegularExpressions;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.SchemaBuilders;
using Markdig;
using Markdig.Syntax;

namespace ContentWriter.Application.Services;

/// <summary>Extracts platform names from the pillar Tools section (Markdown) for SoftwareApplication JSON+LD.</summary>
public static class ToolsSectionHtmlParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private sealed record HeadingInfo(int Level, string Text, SourceSpan Span);

    public static IReadOnlyList<SoftwareApplicationDescriptor> ExtractApplications(
        string bodyMarkdown,
        IReadOnlyList<string> sectionOutline) =>
        DiagnoseExtraction(bodyMarkdown, sectionOutline).Applications;

    public static ToolExtractionResult DiagnoseExtraction(
        string bodyMarkdown,
        IReadOnlyList<string> sectionOutline)
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

        if (string.IsNullOrWhiteSpace(bodyMarkdown))
        {
            return new ToolExtractionResult(ToolGenerationOutcome.ToolsSectionNotFoundInBody, []);
        }

        var document = Markdig.Markdown.Parse(bodyMarkdown, Pipeline);
        var headings = document.OfType<HeadingBlock>()
            .Select(h => new HeadingInfo(h.Level, HeadingText(bodyMarkdown, h), h.Span))
            .ToList();

        if (headings.Count == 0)
        {
            return new ToolExtractionResult(ToolGenerationOutcome.ToolsSectionNotFoundInBody, []);
        }

        var toolsIndex = headings.FindIndex(h =>
            h.Level == 2 && h.Text.Equals(toolsHeading, StringComparison.OrdinalIgnoreCase));

        if (toolsIndex < 0)
        {
            return new ToolExtractionResult(ToolGenerationOutcome.ToolsSectionNotFoundInBody, []);
        }

        var applications = new List<SoftwareApplicationDescriptor>();
        var seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = toolsIndex + 1; i < headings.Count; i++)
        {
            if (headings[i].Level == 2)
            {
                break;
            }

            if (headings[i].Level != 3)
            {
                continue;
            }

            var name = headings[i].Text;
            if (string.IsNullOrWhiteSpace(name)
                || name.StartsWith("How an AI implementer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!seenSlugs.Add(SlugHelper.Slugify(name)))
            {
                continue;
            }

            var sectionEnd = i + 1 < headings.Count ? headings[i + 1].Span.Start : bodyMarkdown.Length;
            var description = ExtractFollowingParagraph(bodyMarkdown, headings[i].Span.End + 1, sectionEnd);
            applications.Add(new SoftwareApplicationDescriptor(name, description));
        }

        if (applications.Count == 0)
        {
            return new ToolExtractionResult(ToolGenerationOutcome.ToolsSectionEmpty, []);
        }

        return new ToolExtractionResult(ToolGenerationOutcome.Success, applications);
    }

    public static string InjectToolLinks(
        string bodyMarkdown,
        IReadOnlyList<string> sectionOutline,
        string toolBaseUrl,
        IReadOnlyList<(string AppName, string ToolSlug)> tools)
    {
        if (string.IsNullOrWhiteSpace(bodyMarkdown) || tools.Count == 0)
        {
            return bodyMarkdown;
        }

        var toolsHeading = sectionOutline.FirstOrDefault(PillarSectionClassifier.IsToolsSection);
        if (string.IsNullOrWhiteSpace(toolsHeading))
        {
            return bodyMarkdown;
        }

        var result = bodyMarkdown;
        foreach (var (appName, toolSlug) in tools)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                continue;
            }

            var href = $"{toolBaseUrl.TrimEnd('/')}/{toolSlug}";
            var pattern = $@"^(###\s+)({Regex.Escape(appName)})\s*$";
            result = Regex.Replace(
                result,
                pattern,
                $"$1[$2]({href})",
                RegexOptions.IgnoreCase | RegexOptions.Multiline,
                TimeSpan.FromSeconds(2));
        }

        return result;
    }

    private static string? ExtractFollowingParagraph(string markdown, int start, int end)
    {
        if (start >= end || start >= markdown.Length)
        {
            return null;
        }

        var slice = markdown[start..Math.Min(end, markdown.Length)];
        var document = Markdig.Markdown.Parse(slice, Pipeline);
        var paragraph = document.OfType<ParagraphBlock>().FirstOrDefault();
        if (paragraph is null)
        {
            return null;
        }

        var text = slice[paragraph.Span.Start..(paragraph.Span.End + 1)].Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string HeadingText(string markdown, HeadingBlock heading)
    {
        var raw = markdown[heading.Span.Start..(heading.Span.End + 1)];
        return raw.TrimStart('#').Trim();
    }
}
