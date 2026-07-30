using System.Text.Json.Serialization;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Domain.Entities;

/// <summary>
/// One manually-scraped input file: a keyword SERP result, an .edu/.gov/wikipedia page,
/// a local pack result, a competitor crawl, or a People-Also-Asked text dump.
/// </summary>
public class KeywordSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }

    /// <summary>Back-reference to the owning row; not serialized (ProjectId is the durable FK) — a populated value here forms a JSON cycle through Project.KeywordSources.</summary>
    [JsonIgnore]
    public Project? Project { get; set; }

    public KeywordSourceCategory Category { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string RawContent { get; set; } = string.Empty;

    public string? ExtractedTitle { get; set; }
    public List<string> ExtractedHeadings { get; set; } = new();
    public List<string> ExtractedParagraphs { get; set; } = new();
    public List<string> ExtractedQuestions { get; set; } = new();

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
