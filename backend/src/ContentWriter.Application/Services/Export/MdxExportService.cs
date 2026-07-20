using System.Text;
using ContentWriter.Application.Providers;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Repositories;
using ReverseMarkdown;

namespace ContentWriter.Application.Services.Export;

public interface IMdxExportService
{
    Task<IReadOnlyList<MdxDocument>> ExportAsync(Guid projectId, CancellationToken cancellationToken = default);
}

/// <summary>Renders approved generated content (article, blog, tool posts) as .mdx files: YAML frontmatter over a Markdown body converted from BodyHtml.</summary>
public class MdxExportService : IMdxExportService
{
    private readonly IProjectRepository _projectRepository;
#pragma warning disable CS0618 // ReverseMarkdown's replacement properties aren't available in the installed 6.1.0 API surface.
    private readonly Converter _htmlToMarkdown = new(new Config
    {
        GithubFlavored = true,
        RemoveComments = true,
        SmartHrefHandling = true,
    });
#pragma warning restore CS0618

    public MdxExportService(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<IReadOnlyList<MdxDocument>> ExportAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetWithDetailsAsync(projectId, cancellationToken)
            ?? throw new ContentGenerationException($"Project {projectId} was not found.");

        var documents = new List<MdxDocument>();

        foreach (var row in project.GeneratedContents
            .Where(c => c.ContentType is GeneratedContentType.TechnicalArticle
                or GeneratedContentType.BlogPost
                or GeneratedContentType.ToolPost
                or GeneratedContentType.SocialFacebook
                or GeneratedContentType.SocialLinkedIn
                or GeneratedContentType.EmailColdOutreach)
            .Where(IsApproved)
            .OrderBy(c => c.ContentType)
            .ThenBy(c => c.SourceAppOrder ?? int.MaxValue))
        {
            documents.Add(ToMdxDocument(row, project.Department));
        }

        if (documents.Count == 0)
        {
            throw new ContentGenerationException(
                "Nothing to export. Generate content and run the review loop (POST .../review) first — " +
                "only rows with an Approved ReviewVerdict are eligible.");
        }

        return documents;
    }

    private MdxDocument ToMdxDocument(GeneratedContent row, string department)
    {
        var slug = string.IsNullOrWhiteSpace(row.Slug) ? row.Id.ToString() : row.Slug;
        var title = string.IsNullOrWhiteSpace(row.DisplayTitle) ? row.Title : row.DisplayTitle!;
        var body = _htmlToMarkdown.Convert(row.BodyHtml ?? string.Empty).Trim();
        var sections = ArticleHtmlSectionExtractor.Split(row.BodyHtml);

        var frontmatter = new StringBuilder()
            .AppendLine("---")
            .Append("title: ").AppendLine(YamlString(title))
            .Append("description: ").AppendLine(YamlString(row.MetaDescription ?? string.Empty))
            .Append("slug: ").AppendLine(YamlString(slug))
            .Append("department: ").AppendLine(YamlString(department))
            .Append("date: ").AppendLine(YamlString(row.CreatedAtUtc.ToString("O")))
            .Append("excerpt: ").AppendLine(YamlString(row.Summary))
            .Append("mainSummary: ").AppendLine(YamlString(row.MainSummary))
            .Append("heroSummary: ").AppendLine(YamlString(row.HeroSummary))
            .Append("homeSummary: ").AppendLine(YamlString(row.HomeSummary))
            .Append("blogSummary: ").AppendLine(YamlString(row.BlogSummary))
            .Append("advertisingSummary: ").AppendLine(YamlString(row.AdvertisingSummary))
            .AppendLine(YamlStringArray("tags", row.Keywords));
        AppendSectionsYaml(frontmatter, sections);
        frontmatter.AppendLine("---");

        var folder = FolderFor(row.ContentType);
        return new MdxDocument($"{folder}/{slug}.mdx", $"{frontmatter}\n{body}\n");
    }

    /// <summary>Maps a content type to its Keystatic collection folder under content-writer-output/.</summary>
    private static string FolderFor(GeneratedContentType contentType) => contentType switch
    {
        GeneratedContentType.TechnicalArticle => "use-cases",
        GeneratedContentType.BlogPost => "blog",
        GeneratedContentType.ToolPost => "tools",
        GeneratedContentType.SocialFacebook => "social/facebook",
        GeneratedContentType.SocialLinkedIn => "social/linkedin",
        GeneratedContentType.EmailColdOutreach => "email",
        _ => "misc",
    };

    /// <summary>
    /// Emits H2-bound sections (heading + Markdown body per section) as a YAML array, so a layout
    /// that composes fragments from specific entries — e.g. "blog[1].sections[0].heading" — can address
    /// a single section without parsing the full MDX body. Mirrors the split GeekBackend publish uses.
    /// </summary>
    private void AppendSectionsYaml(StringBuilder frontmatter, IReadOnlyList<HtmlSection> sections)
    {
        if (sections.Count == 0)
        {
            frontmatter.AppendLine("sections: []");
            return;
        }

        frontmatter.AppendLine("sections:");
        foreach (var section in sections)
        {
            var heading = section.HeadingText ?? string.Empty;
            var sectionBody = _htmlToMarkdown.Convert(section.BodyContent).Trim();

            frontmatter.Append("  - heading: ").AppendLine(YamlString(heading));
            frontmatter.AppendLine("    body: |");
            foreach (var line in sectionBody.Split('\n'))
            {
                frontmatter.Append("      ").AppendLine(line.TrimEnd('\r'));
            }
        }
    }

    private static string YamlString(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ")}\"";

    private static string YamlStringArray(string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return $"{key}: []";

        return $"{key}: [{string.Join(", ", values.Select(YamlString))}]";
    }

    /// <summary>Export gate: a row exports only if its most recent ReviewVerdict is Approved, matching the GeekBackend publish gate.</summary>
    private static bool IsApproved(GeneratedContent row) =>
        row.ReviewVerdicts.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault()?.Status == ReviewVerdictStatus.Approved;
}
