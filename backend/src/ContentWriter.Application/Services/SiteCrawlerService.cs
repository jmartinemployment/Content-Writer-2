using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace ContentWriter.Application.Services;

public class SiteCrawlerService : ISiteCrawlerService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SiteCrawlerService> _logger;

    public SiteCrawlerService(HttpClient httpClient, ILogger<SiteCrawlerService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; ContentWriterBot/1.0; +https://www.geekatyourspot.com)");
        }
        _logger = logger;
    }

    public async Task<SiteCrawlResult> CrawlAsync(string startUrl, int maxPages = 50, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(startUrl, UriKind.Absolute, out var startUri))
        {
            throw new ArgumentException($"'{startUrl}' is not a valid absolute URL.", nameof(startUrl));
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toVisit = new Queue<Uri>();
        toVisit.Enqueue(startUri);
        await EnqueueSitemapUrlsAsync(startUri, toVisit, visited, cancellationToken);

        var jsonLdBlocks = new List<string>();
        var headings = new List<string>();
        var paragraphs = new List<string>();
        string siteName = startUri.Host;
        var pagesCrawled = 0;

        while (toVisit.Count > 0 && pagesCrawled < maxPages)
        {
            var current = toVisit.Dequeue();
            var normalizedKey = current.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (!visited.Add(normalizedKey))
            {
                continue;
            }

            HtmlDocument? doc;
            try
            {
                doc = await FetchAsync(current, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping {Url} due to fetch error", current);
                continue;
            }

            if (doc is null)
            {
                continue;
            }

            pagesCrawled++;

            if (pagesCrawled == 1)
            {
                var titleNode = doc.DocumentNode.SelectSingleNode("//title");
                var ogSiteName = doc.DocumentNode.SelectSingleNode("//meta[@property='og:site_name']")
                    ?.GetAttributeValue("content", null);
                siteName = ogSiteName ?? titleNode?.InnerText.Trim() ?? startUri.Host;
            }

            ExtractJsonLd(doc, jsonLdBlocks);
            ExtractHeadings(doc, headings);
            ExtractParagraphs(doc, paragraphs);

            if (pagesCrawled < maxPages)
            {
                EnqueueInternalLinks(doc, startUri, current, toVisit, visited);
            }
        }

        var tone = ToneFocusAnalyzer.DetectTone(paragraphs);
        var focus = ToneFocusAnalyzer.DetectFocus(headings, paragraphs);

        return new SiteCrawlResult(siteName, jsonLdBlocks, headings, paragraphs, tone, focus, pagesCrawled);
    }

    private async Task<HtmlDocument?> FetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Non-success status {Status} for {Url}", response.StatusCode, uri);
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    private static void ExtractJsonLd(HtmlDocument doc, List<string> jsonLdBlocks)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts is null)
        {
            return;
        }

        foreach (var script in scripts)
        {
            var content = HtmlEntity.DeEntitize(script.InnerText)?.Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                jsonLdBlocks.Add(content);
            }
        }
    }

    private static void ExtractHeadings(HtmlDocument doc, List<string> headings)
    {
        var nodes = doc.DocumentNode.SelectNodes("//h1 | //h2 | //h3");
        if (nodes is null)
        {
            return;
        }

        foreach (var node in nodes)
        {
            var text = HtmlEntity.DeEntitize(node.InnerText)?.Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text ?? string.Empty, @"\s+", " ");
            if (!string.IsNullOrWhiteSpace(text) && text.Length > 2)
            {
                headings.Add(text);
            }
        }
    }

    private static void ExtractParagraphs(HtmlDocument doc, List<string> paragraphs)
    {
        var nodes = doc.DocumentNode.SelectNodes("//p");
        if (nodes is null)
        {
            return;
        }

        foreach (var node in nodes)
        {
            if (node.Ancestors().Any(a => a.Name is "nav" or "footer" or "script" or "style"))
            {
                continue;
            }

            var text = HtmlEntity.DeEntitize(node.InnerText)?.Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text ?? string.Empty, @"\s+", " ");
            if (!string.IsNullOrWhiteSpace(text) && text.Length > 25)
            {
                paragraphs.Add(text);
            }
        }
    }

    private async Task EnqueueSitemapUrlsAsync(
        Uri startUri,
        Queue<Uri> toVisit,
        HashSet<string> visited,
        CancellationToken cancellationToken)
    {
        var sitemapUrl = new Uri(startUri, "/sitemap.xml");
        string xml;
        try
        {
            xml = await _httpClient.GetStringAsync(sitemapUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No sitemap found at {SitemapUrl}", sitemapUrl);
            return;
        }

        var urls = await ParseSitemapUrlsAsync(xml, startUri, cancellationToken);
        foreach (var url in urls)
        {
            var key = url.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (visited.Contains(key))
            {
                continue;
            }

            toVisit.Enqueue(url);
        }

        if (urls.Count > 0)
        {
            _logger.LogInformation("Seeded crawl queue with {Count} URL(s) from {SitemapUrl}", urls.Count, sitemapUrl);
        }
    }

    private async Task<List<Uri>> ParseSitemapUrlsAsync(string xml, Uri rootUri, CancellationToken cancellationToken)
    {
        var urls = new List<Uri>();
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse sitemap XML for {Host}", rootUri.Host);
            return urls;
        }

        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var sitemapLocs = doc.Descendants(ns + "sitemap")
            .Select(node => node.Element(ns + "loc")?.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (sitemapLocs.Count > 0)
        {
            foreach (var childSitemap in sitemapLocs)
            {
                try
                {
                    var childXml = await _httpClient.GetStringAsync(childSitemap, cancellationToken);
                    urls.AddRange(ParseUrlLocs(childXml, rootUri));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch child sitemap {ChildSitemap}", childSitemap);
                }
            }

            return urls;
        }

        urls.AddRange(ParseUrlLocs(xml, rootUri));
        return urls;
    }

    private static List<Uri> ParseUrlLocs(string xml, Uri rootUri)
    {
        var urls = new List<Uri>();
        foreach (Match match in Regex.Matches(xml, @"<loc>\s*(.*?)\s*</loc>", RegexOptions.IgnoreCase))
        {
            var loc = match.Groups[1].Value.Trim();
            if (!Uri.TryCreate(loc, UriKind.Absolute, out var absolute))
            {
                continue;
            }

            if (!absolute.Host.Equals(rootUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            urls.Add(absolute);
        }

        return urls;
    }

    private static void EnqueueInternalLinks(HtmlDocument doc, Uri rootUri, Uri currentUri, Queue<Uri> toVisit, HashSet<string> visited)
    {
        var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null)
        {
            return;
        }

        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#') || href.StartsWith("mailto:") || href.StartsWith("tel:"))
            {
                continue;
            }

            if (!Uri.TryCreate(currentUri, href, out var absolute))
            {
                continue;
            }

            if (!absolute.Host.Equals(rootUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = absolute.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (!visited.Contains(key))
            {
                toVisit.Enqueue(absolute);
            }
        }
    }
}
