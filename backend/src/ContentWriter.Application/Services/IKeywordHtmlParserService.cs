using ContentWriter.Domain.Enums;

namespace ContentWriter.Application.Services;

public record ParsedKeywordSource(
    KeywordSourceCategory Category,
    string OriginalFileName,
    string? Title,
    List<string> Headings,
    List<string> Paragraphs,
    List<string> Questions);

public interface IKeywordHtmlParserService
{
    /// <summary>
    /// Parses a single manually-scraped input. HTML categories are parsed with HtmlAgilityPack;
    /// <see cref="KeywordSourceCategory.PeopleAlsoAsk"/> is treated as plain text, one question per line.
    /// </summary>
    ParsedKeywordSource Parse(KeywordSourceCategory category, string originalFileName, string rawContent);
}
