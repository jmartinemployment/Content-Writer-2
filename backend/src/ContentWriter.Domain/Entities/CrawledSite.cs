using System.Text.Json.Serialization;

namespace ContentWriter.Domain.Entities;

public class CrawledSite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }

    /// <summary>Back-reference to the owning row; not serialized (ProjectId is the durable FK) — a populated value here forms a JSON cycle through Project.CrawledSite.</summary>
    [JsonIgnore]
    public Project? Project { get; set; }

    public string SourceUrl { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;

    /// <summary>Raw JSON+LD blocks found on the crawled site, one entry per &lt;script type="application/ld+json"&gt; tag.</summary>
    public List<string> JsonLdBlocks { get; set; } = new();

    public List<string> Headings { get; set; } = new();
    public List<string> Paragraphs { get; set; } = new();

    public string DetectedTone { get; set; } = string.Empty;
    public string DetectedFocus { get; set; } = string.Empty;

    /// <summary>Named items extracted from the Home page's own use-case/service listing (if any), used to
    /// ground generation when a project's TargetKeyword matches one by name.</summary>
    public List<UseCaseItem> UseCases { get; set; } = new();

    public int PagesCrawled { get; set; }
    public DateTime CrawledAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record UseCaseItem(string Category, string Name, string? Description, string? Href);
