namespace ContentWriter.Domain.Entities;

public class CrawledSite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public string SourceUrl { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;

    /// <summary>Raw JSON+LD blocks found on the crawled site, one entry per &lt;script type="application/ld+json"&gt; tag.</summary>
    public List<string> JsonLdBlocks { get; set; } = new();

    public List<string> Headings { get; set; } = new();
    public List<string> Paragraphs { get; set; } = new();

    public string DetectedTone { get; set; } = string.Empty;
    public string DetectedFocus { get; set; } = string.Empty;

    public int PagesCrawled { get; set; }
    public DateTime CrawledAtUtc { get; set; } = DateTime.UtcNow;
}
