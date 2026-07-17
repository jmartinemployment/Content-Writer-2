using System.Text.RegularExpressions;
using Markdig;

namespace ContentWriter.Application.Services;

/// <summary>Ensures article/blog bodies are semantic HTML even when the model returns Markdown.</summary>
public static class HtmlBodyNormalizer
{
    private static readonly Regex MarkdownFence = new(@"^```(?:json|html|markdown|md)?\s*|\s*```$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownHeading = new(@"^#{1,6}\s", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownList = new(@"^\s*[-*]\s", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    public static string Normalize(string rawContent)
    {
        var cleaned = MarkdownFence.Replace(rawContent, string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return cleaned;
        }

        if (LooksLikeHtml(cleaned))
        {
            return cleaned;
        }

        if (LooksLikeMarkdown(cleaned))
        {
            return Markdown.ToHtml(cleaned, Pipeline).Trim();
        }

        return cleaned;
    }

    private static bool LooksLikeHtml(string content) =>
        content.Contains("<h2", StringComparison.OrdinalIgnoreCase)
        || content.Contains("<h3", StringComparison.OrdinalIgnoreCase)
        || content.Contains("<p>", StringComparison.OrdinalIgnoreCase)
        || content.Contains("<p ", StringComparison.OrdinalIgnoreCase)
        || content.Contains("<ul", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeMarkdown(string content) =>
        MarkdownHeading.IsMatch(content)
        || content.Contains("**", StringComparison.Ordinal)
        || MarkdownList.IsMatch(content);
}
