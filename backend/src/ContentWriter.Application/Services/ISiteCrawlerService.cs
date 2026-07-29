namespace ContentWriter.Application.Services;

public record SiteCrawlResult(
    string SiteName,
    List<string> JsonLdBlocks,
    List<string> Headings,
    List<string> Paragraphs,
    string DetectedTone,
    string DetectedFocus,
    int PagesCrawled,
    List<string> HomePageHeadings,
    List<string> HomePageParagraphs);

public interface ISiteCrawlerService
{
    /// <summary>
    /// Crawls the given project URL and every same-domain page discovered from it (sitemap + linked
    /// pages, capped only by <paramref name="maxPages"/> as a safety valve), extracting JSON+LD,
    /// headings, and body text (paragraphs, list items, blockquotes, table cells) to determine tone and focus.
    /// </summary>
    Task<SiteCrawlResult> CrawlAsync(string startUrl, int maxPages = int.MaxValue, CancellationToken cancellationToken = default);
}
