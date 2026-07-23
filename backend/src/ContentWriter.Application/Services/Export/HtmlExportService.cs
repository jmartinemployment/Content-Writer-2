using ContentWriter.Application.Providers;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.InMemory;
using Microsoft.Extensions.Options;

namespace ContentWriter.Application.Services.Export;

public interface IHtmlExportService
{
    Task<IReadOnlyList<ExportedHtmlDocument>> ExportAsync(Guid projectId, bool includeRevise = true, CancellationToken cancellationToken = default);
}

/// <summary>
/// Renders approved generated content (article, blog, tool posts, social, email, image prompts) as
/// real standalone .html documents — no YAML frontmatter, no Keystatic contract. Metadata travels as
/// &lt;meta&gt;/&lt;link&gt;/&lt;script type="application/ld+json"&gt; tags; the body is the literal
/// rendered Section tree via <see cref="SectionHtmlRenderer"/>.
/// </summary>
public class HtmlExportService : IHtmlExportService
{
    private readonly IProjectStore _projectStore;
    private readonly CompanyProfileOptions _companyProfile;

    public HtmlExportService(IProjectStore projectStore, IOptions<CompanyProfileOptions> companyProfile)
    {
        _projectStore = projectStore;
        _companyProfile = companyProfile.Value;
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

            var imagePromptDocs = ExtractImagePrompts(row);
            foreach (var imagePromptDoc in imagePromptDocs)
            {
                documents.Add(imagePromptDoc);
            }
        }

        if (documents.Count == 0)
        {
            throw new ContentGenerationException(
                "Nothing to export. Generate content first — review is optional, but unreviewed rows always export.");
        }

        return documents;
    }

    private static readonly HashSet<GeneratedContentType> ImagePromptContentTypes =
    [
        GeneratedContentType.ImagePromptPillarFigure,
        GeneratedContentType.ImagePromptBlogFigure,
        GeneratedContentType.ImagePromptSection,
        GeneratedContentType.ImagePromptSocialFacebook,
        GeneratedContentType.ImagePromptSocialLinkedIn,
    ];

    private ExportedHtmlDocument ToHtmlDocument(GeneratedContent row, string department)
    {
        var slug = string.IsNullOrWhiteSpace(row.Slug) ? row.Id.ToString() : row.Slug;
        var folder = FolderFor(row.ContentType);

        if (ImagePromptContentTypes.Contains(row.ContentType))
        {
            return new ExportedHtmlDocument($"{folder}/{slug}.txt", PlainTextOf(row.Body));
        }

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

        var canonicalUrl = CanonicalUrlFor(row, department);
        var ogType = row.ContentType switch
        {
            GeneratedContentType.TechnicalArticle or GeneratedContentType.BlogPost => "article",
            GeneratedContentType.ToolPost => "website",
            _ => "website",
        };
        // JSON+LD is only ever built for these three types (TechnicalArticleSchemaBuilder /
        // BlogPostingSchemaBuilder / SoftwareApplicationSchemaBuilder) — JsonLdSchema is null/"{}"
        // for social, email, and image-prompt rows, so RenderDocument's own guard skips the script
        // tag for those without needing a type check here too.
        var html = SectionHtmlRenderer.RenderDocument(
            title, row.MetaDescription, canonicalUrl, ogType, _companyProfile.PublisherLogoUrl, row.JsonLdSchema, meta, body);

        return new ExportedHtmlDocument($"{folder}/{slug}.html", html);
    }

    /// <summary>Recovers the raw prompt string a <see cref="ContentDocumentText.FromPlainText"/> body wraps —
    /// image-prompt rows export as plain .txt, not a rendered HTML page.</summary>
    private static string PlainTextOf(ContentDocument? body) =>
        body?.Lede.Paragraphs.OfType<TextParagraph>().FirstOrDefault() is { } paragraph
            ? string.Join(" ", paragraph.Runs.Select(r => r.Text))
            : string.Empty;

    /// <summary>Matches the URL construction each JSON+LD schema builder already uses
    /// (<c>ContentGenerationOrchestrator.CombineUrl</c>) — the canonical tag and JSON+LD "url" field
    /// must agree, since a mismatch there is exactly the kind of thing search engines flag.</summary>
    private string? CanonicalUrlFor(GeneratedContent row, string department) => row.ContentType switch
    {
        GeneratedContentType.TechnicalArticle => CombineUrl(_companyProfile.ArticleBaseUrl, department, row.Slug),
        GeneratedContentType.BlogPost => CombineUrl(_companyProfile.BlogBaseUrl, department, row.Slug),
        GeneratedContentType.ToolPost => CombineUrl(_companyProfile.ToolBaseUrl, department, row.Slug),
        _ => null,
    };

    private static string CombineUrl(string baseUrl, string department, string slug) =>
        $"{baseUrl.TrimEnd('/')}/{department}/{slug}";

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

    private static IReadOnlyList<ExportedHtmlDocument> ExtractImagePrompts(GeneratedContent row)
    {
        var prompts = new List<ExportedHtmlDocument>();
        var body = row.Body;
        if (body is null)
            return prompts;

        var slug = string.IsNullOrWhiteSpace(row.Slug) ? row.Id.ToString() : row.Slug;

        CollectImagePrompts(body.Lede, 0, slug, prompts);
        foreach (var section in body.Sections)
        {
            CollectImagePrompts(section, 0, slug, prompts);
        }

        return prompts;
    }

    private static void CollectImagePrompts(Section section, int sectionIndex, string slug, List<ExportedHtmlDocument> prompts)
    {
        if (!string.IsNullOrWhiteSpace(section.ImagePrompt))
        {
            var fileName = $"image-prompts/sections/{slug}-{sectionIndex}.txt";
            prompts.Add(new ExportedHtmlDocument(fileName, section.ImagePrompt));
        }

        for (int i = 0; i < section.Children.Count; i++)
        {
            CollectImagePrompts(section.Children[i], sectionIndex + 1 + i, slug, prompts);
        }
    }
}
