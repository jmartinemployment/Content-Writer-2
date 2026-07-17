using System.Text.RegularExpressions;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.SchemaBuilders;

namespace ContentWriter.Application.Services;

/// <summary>Extracts platform names from the pillar Tools section for SoftwareApplication JSON+LD.</summary>
public static class ToolsSectionHtmlParser
{
    private static readonly Regex HeadingPattern = new(
        @"<h([2-4])[^>]*>(.*?)</h\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static IReadOnlyList<SoftwareApplicationDescriptor> ExtractApplications(
        string bodyHtml,
        IReadOnlyList<string> sectionOutline) =>
        DiagnoseExtraction(bodyHtml, sectionOutline).Applications;

    public static ToolExtractionResult DiagnoseExtraction(
        string bodyHtml,
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

        if (string.IsNullOrWhiteSpace(bodyHtml))
        {
            return new ToolExtractionResult(ToolGenerationOutcome.ToolsSectionNotFoundInBody, []);
        }

        var matches = HeadingPattern.Matches(bodyHtml).Cast<Match>().ToList();
        if (matches.Count == 0)
        {
            return new ToolExtractionResult(ToolGenerationOutcome.ToolsSectionNotFoundInBody, []);
        }

        var toolsIndex = matches.FindIndex(match =>
            int.Parse(match.Groups[1].Value) == 2
            && StripTags(match.Groups[2].Value).Equals(toolsHeading, StringComparison.OrdinalIgnoreCase));

        if (toolsIndex < 0)
        {
            return new ToolExtractionResult(ToolGenerationOutcome.ToolsSectionNotFoundInBody, []);
        }

        var applications = new List<SoftwareApplicationDescriptor>();
        var seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = toolsIndex + 1; i < matches.Count; i++)
        {
            var level = int.Parse(matches[i].Groups[1].Value);
            if (level == 2)
            {
                break;
            }

            if (level != 3)
            {
                continue;
            }

            var name = StripTags(matches[i].Groups[2].Value);
            if (string.IsNullOrWhiteSpace(name)
                || name.StartsWith("How an AI implementer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!seenSlugs.Add(SlugHelper.Slugify(name)))
            {
                continue;
            }

            var description = ExtractFollowingParagraph(bodyHtml, matches[i].Index + matches[i].Length);
            applications.Add(new SoftwareApplicationDescriptor(name, description));
        }

        if (applications.Count == 0)
        {
            return new ToolExtractionResult(ToolGenerationOutcome.ToolsSectionEmpty, []);
        }

        return new ToolExtractionResult(ToolGenerationOutcome.Success, applications);
    }

    public static string InjectToolLinks(
        string bodyHtml,
        IReadOnlyList<string> sectionOutline,
        string toolBaseUrl,
        IReadOnlyList<(string AppName, string ToolSlug)> tools)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml) || tools.Count == 0)
        {
            return bodyHtml;
        }

        var toolsHeading = sectionOutline.FirstOrDefault(PillarSectionClassifier.IsToolsSection);
        if (string.IsNullOrWhiteSpace(toolsHeading))
        {
            return bodyHtml;
        }

        var result = bodyHtml;
        foreach (var (appName, toolSlug) in tools)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                continue;
            }

            var href = $"{toolBaseUrl.TrimEnd('/')}/{toolSlug}";
            var pattern = $@"(<h3[^>]*>)(\s*{Regex.Escape(appName)}\s*)(</h3>)";
            result = Regex.Replace(
                result,
                pattern,
                $"$1<a href=\"{href}\">$2</a>$3",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromSeconds(2));
        }

        return result;
    }

    private static string? ExtractFollowingParagraph(string html, int startIndex)
    {
        var slice = html[startIndex..];
        var match = Regex.Match(slice, @"<p[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
        {
            return null;
        }

        var text = StripTags(match.Groups[1].Value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string StripTags(string html) =>
        Regex.Replace(html, "<[^>]+>", " ").Replace("&nbsp;", " ").Trim();
}
