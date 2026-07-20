namespace ContentWriter.Domain.Entities;

public class WebPost
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ContentStructure ContentStructure { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class ContentStructure
{
    public List<ContentSection> Sections { get; set; } = new();
    public string? MainBody { get; set; }
}

public class ContentSection
{
    public string? HeadingText { get; set; }
    public string BodyContent { get; set; } = string.Empty;
    public string? MediaUrl { get; set; }
    public string? MediaAlt { get; set; }
}
