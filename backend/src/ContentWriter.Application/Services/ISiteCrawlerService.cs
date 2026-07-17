namespace ContentWriter.Application.Services;

public record SiteCrawlResult(
    string SiteName,
    List<string> JsonLdBlocks,
    List<string> Headings,
    List<string> Paragraphs,
    string DetectedTone,
    string DetectedFocus,
    int PagesCrawled);

public interface ISiteCrawlerService
{
    /// <summary>
    /// Crawls the given project URL and up to <paramref name="maxPages"/> same-domain pages linked
    /// from it, extracting JSON+LD, headings, and paragraph text to determine tone and focus.
    /// </summary>
    Task<SiteCrawlResult> CrawlAsync(string startUrl, int maxPages = 50, CancellationToken cancellationToken = default);
}
