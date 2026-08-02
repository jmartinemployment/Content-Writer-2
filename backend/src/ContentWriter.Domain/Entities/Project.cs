using System.Text.Json.Serialization;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Domain.Entities;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProjectUrl { get; set; } = string.Empty;
    public string TargetKeyword { get; set; } = string.Empty;

    /// <summary>Department/category slug (e.g. "accounting") — determines the published URL segment: /use-cases/{Department}/{slug}.</summary>
    public string Department { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public LlmProviderType PreferredProvider { get; set; } = LlmProviderType.LmStudio;

    /// <summary>When true, skip LLM title generation for the pillar article and use TargetKeyword verbatim as the title.</summary>
    public bool UseExactKeywordAsTitle { get; set; }

    /// <summary>Optional comma-separated desired headings that must appear in the pillar article outline.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Content Creator: operator content-approval timestamp (gates Repurpose / Mix).</summary>
    public DateTime? ContentApprovedAtUtc { get; set; }

    /// <summary>Back-reference to the owning row; not serialized (ClientId is the durable FK) — a populated value here forms a JSON cycle through Client.Projects.</summary>
    [JsonIgnore]
    public Client? Client { get; set; }
    public CrawledSite? CrawledSite { get; set; }
    public List<KeywordSource> KeywordSources { get; set; } = new();
    public List<GeneratedContent> GeneratedContents { get; set; } = new();
}
