using System.Text.RegularExpressions;
using ContentWriter.Domain.Enums;
using HtmlAgilityPack;

namespace ContentWriter.Application.Services;

public class KeywordHtmlParserService : IKeywordHtmlParserService
{
    public ParsedKeywordSource Parse(KeywordSourceCategory category, string originalFileName, string rawContent)
    {
        if (category == KeywordSourceCategory.PeopleAlsoAsk)
        {
            return ParsePlainTextQuestions(originalFileName, rawContent);
        }

        return ParseHtml(category, originalFileName, rawContent);
    }

    private static ParsedKeywordSource ParsePlainTextQuestions(string originalFileName, string rawContent)
    {
        var questions = rawContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('-', '*', '•', ' ').Trim())
            .Where(line => line.Length > 0)
            .ToList();

        return new ParsedKeywordSource(
            KeywordSourceCategory.PeopleAlsoAsk,
            originalFileName,
            Title: null,
            Headings: new List<string>(),
            Paragraphs: new List<string>(),
            Questions: questions);
    }

    private static ParsedKeywordSource ParseHtml(KeywordSourceCategory category, string originalFileName, string rawContent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(rawContent);

        var title = HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//title")?.InnerText)?.Trim();

        var headings = new List<string>();
        var headingNodes = doc.DocumentNode.SelectNodes("//h1 | //h2 | //h3");
        if (headingNodes is not null)
        {
            foreach (var node in headingNodes)
            {
                var text = CleanText(node.InnerText);
                if (text.Length > 2)
                {
                    headings.Add(text);
                }
            }
        }

        var paragraphs = new List<string>();
        var paragraphNodes = doc.DocumentNode.SelectNodes("//p");
        if (paragraphNodes is not null)
        {
            foreach (var node in paragraphNodes)
            {
                var text = CleanText(node.InnerText);
                if (text.Length > 20)
                {
                    paragraphs.Add(text);
                }
            }
        }

        // Google SERP People-Also-Ask blocks are sometimes embedded in the same saved HTML
        // as a keyword result page rather than a separate text file - pull them out too.
        var questions = new List<string>();
        var questionNodes = doc.DocumentNode.SelectNodes(
            "//*[contains(@class,'related-question') or @data-q or contains(@jsname,'Vy1nD')]");
        if (questionNodes is not null)
        {
            foreach (var node in questionNodes)
            {
                var text = CleanText(node.InnerText);
                if (text.EndsWith('?') && text.Length > 5)
                {
                    questions.Add(text);
                }
            }
        }
        questions.AddRange(headings.Where(h => h.EndsWith('?')));

        return new ParsedKeywordSource(category, originalFileName, title, headings, paragraphs, questions.Distinct().ToList());
    }

    private static string CleanText(string? raw)
    {
        var decoded = HtmlEntity.DeEntitize(raw) ?? string.Empty;
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
