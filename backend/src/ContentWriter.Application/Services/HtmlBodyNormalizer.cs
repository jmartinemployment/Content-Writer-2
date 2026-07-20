using System.Text.RegularExpressions;

namespace ContentWriter.Application.Services;

/// <summary>Strips stray code fences the model sometimes wraps its Markdown/MDX body in.</summary>
public static class HtmlBodyNormalizer
{
    private static readonly Regex MarkdownFence = new(@"^```(?:json|html|markdown|md)?\s*|\s*```$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static string Normalize(string rawContent) =>
        MarkdownFence.Replace(rawContent, string.Empty).Trim();
}
