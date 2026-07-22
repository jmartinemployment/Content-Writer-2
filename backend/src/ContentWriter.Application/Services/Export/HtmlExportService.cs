using ContentWriter.Application.Providers;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.InMemory;

namespace ContentWriter.Application.Services.Export;

public interface IHtmlExportService
{
    Task<IReadOnlyList<ExportedHtmlDocument>> ExportAsync(Guid projectId, bool includeRevise = true, CancellationToken cancellationToken = default);
}

/// <summary>
/// Renders approved generated content (article, blog, tool posts, social, email, image prompts) as
/// real standalone .html documents — no YAML frontmatter, no Keystatic contract. Metadata travels as
/// &lt;meta&gt; tags; the body is the literal rendered Section tree via <see cref="SectionHtmlRenderer"/>.
/// </summary>
public class HtmlExportService : IHtmlExportService
{
    private readonly IProjectStore _projectStore;

    public HtmlExportService(IProjectStore projectStore)
    {
        _projectStore = projectStore;
    }

    public async Task<IReadOnlyList<ExportedHtmlDocument>> ExportAsync(Guid projectId, bool includeRevise = true, CancellationToken cancellationToken = default)
    {
        var project = await _projectStore.GetAsync(projectId, cancellationToken)
            ?? throw new ContentGenerationException($"Project {projectId} was not found.");

        var documents = new List<ExportedHtmlDocument>();

        foreach (var row in project.GeneratedContents
            .Where(c => c.ContentType is GeneratedContentType.TechnicalArticle
                or GeneratedContentType.BlogPost
                or GeneratedContentType.ToolPost
                or GeneratedContentType.SocialFacebook
                or GeneratedContentType.SocialLinkedIn
                or GeneratedContentType.EmailColdOutreach
                or GeneratedContentType.ImagePromptPillarFigure
                or GeneratedContentType.ImagePromptBlogFigure
                or GeneratedContentType.ImagePromptSection
                or GeneratedContentType.ImagePromptSocialFacebook
                or GeneratedContentType.ImagePromptSocialLinkedIn)
            .Where(r => IsApproved(r, includeRevise))
            .OrderBy(c => c.ContentType)
            .ThenBy(c => c.SourceAppOrder ?? int.MaxValue))
        {
            documents.Add(ToHtmlDocument(row, project.Department));
        }

        if (documents.Count == 0)
        {
            throw new ContentGenerationException(
                "Nothing to export. Generate content first — review is optional, but unreviewed rows always export.");
        }

        return documents;
    }

    private static ExportedHtmlDocument ToHtmlDocument(GeneratedContent row, string department)
    {
        var slug = string.IsNullOrWhiteSpace(row.Slug) ? row.Id.ToString() : row.Slug;
        var title = string.IsNullOrWhiteSpace(row.DisplayTitle) ? row.Title : row.DisplayTitle!;
        var body = row.Body ?? new ContentDocument(
            new Section("h2", title, [], null, []), []);

        var meta = new Dictionary<string, string?>
        {
            ["slug"] = slug,
            ["department"] = department,
            ["date"] = row.CreatedAtUtc.ToString("O"),
            ["excerpt"] = row.Summary,
            ["mainSummary"] = row.MainSummary,
            ["heroSummary"] = row.HeroSummary,
            ["homeSummary"] = row.HomeSummary,
            ["blogSummary"] = row.BlogSummary,
            ["advertisingSummary"] = row.AdvertisingSummary,
            ["tags"] = row.Keywords.Count > 0 ? string.Join(",", row.Keywords) : null,
        };

        var html = SectionHtmlRenderer.RenderDocument(title, row.MetaDescription, meta, body);

        var folder = FolderFor(row.ContentType);
        return new ExportedHtmlDocument($"{folder}/{slug}.html", html);
    }

    /// <summary>Maps a content type to its output folder under content-writer-output/.</summary>
    private static string FolderFor(GeneratedContentType contentType) => contentType switch
    {
        GeneratedContentType.TechnicalArticle => "use-cases",
        GeneratedContentType.BlogPost => "blog",
        GeneratedContentType.ToolPost => "tools",
        GeneratedContentType.SocialFacebook => "social/facebook",
        GeneratedContentType.SocialLinkedIn => "social/linkedin",
        GeneratedContentType.EmailColdOutreach => "email",
        GeneratedContentType.ImagePromptPillarFigure => "image-prompts/pillar",
        GeneratedContentType.ImagePromptBlogFigure => "image-prompts/blog",
        GeneratedContentType.ImagePromptSection => "image-prompts/sections",
        GeneratedContentType.ImagePromptSocialFacebook => "image-prompts/social/facebook",
        GeneratedContentType.ImagePromptSocialLinkedIn => "image-prompts/social/linkedin",
        _ => "misc",
    };

    private static bool IsApproved(GeneratedContent row, bool includeRevise)
    {
        var latest = row.ReviewVerdicts.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
        if (latest is null)
            return true;

        return latest.Status == ReviewVerdictStatus.Approved || (includeRevise && latest.Status != ReviewVerdictStatus.Approved);
    }
}
