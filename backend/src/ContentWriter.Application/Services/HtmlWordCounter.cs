using System.Text.RegularExpressions;

namespace ContentWriter.Application.Services;

public static class HtmlWordCounter
{
    private static readonly Regex HtmlTagPattern = new("<[^>]+>", RegexOptions.Compiled);

    public static int Count(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return 0;
        }

        var text = HtmlTagPattern.Replace(html, " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
