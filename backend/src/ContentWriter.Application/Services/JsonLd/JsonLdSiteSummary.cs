namespace ContentWriter.Application.Services.JsonLd;

/// <summary>Parsed, high-confidence fields extracted from crawled schema.org JSON+LD blocks.</summary>
public sealed class JsonLdSiteSummary
{
    public List<string> Organizations { get; } = new();
    public List<string> People { get; } = new();
    public List<string> Services { get; } = new();
    public List<string> Topics { get; } = new();
    public List<string> ServiceAreas { get; } = new();
    public List<string> FaqEntries { get; } = new();
    public List<string> Articles { get; } = new();
    public List<string> WebPages { get; } = new();
    public List<string> SoftwareApplications { get; } = new();

    public bool HasContent =>
        Organizations.Count > 0
        || People.Count > 0
        || Services.Count > 0
        || Topics.Count > 0
        || ServiceAreas.Count > 0
        || FaqEntries.Count > 0
        || Articles.Count > 0
        || WebPages.Count > 0
        || SoftwareApplications.Count > 0;
}
