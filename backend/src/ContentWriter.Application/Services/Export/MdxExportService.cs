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
                or GeneratedContentType.ToolPost)
            .Where(IsApproved)
            .OrderBy(c => c.ContentType)
            .ThenBy(c => c.SourceAppOrder ?? int.MaxValue))
        {
            documents.Add(ToMdxDocument(row));
        }

        if (documents.Count == 0)
        {
            throw new ContentGenerationException(
                "Nothing to export. Generate content and run the review loop (POST .../review) first — " +
                "only rows with an Approved ReviewVerdict are eligible.");
        }

        return documents;
    }

    private MdxDocument ToMdxDocument(GeneratedContent row)
    {
        var slug = string.IsNullOrWhiteSpace(row.Slug) ? row.Id.ToString() : row.Slug;
        var title = string.IsNullOrWhiteSpace(row.DisplayTitle) ? row.Title : row.DisplayTitle!;
        var body = _htmlToMarkdown.Convert(row.BodyHtml ?? string.Empty).Trim();

        var frontmatter = new StringBuilder()
            .AppendLine("---")
            .Append("title: ").AppendLine(YamlString(title))
            .Append("description: ").AppendLine(YamlString(row.MetaDescription ?? string.Empty))
            .Append("slug: ").AppendLine(YamlString(slug))
            .Append("date: ").AppendLine(YamlString(row.CreatedAtUtc.ToString("O")))
            .AppendLine(YamlStringArray("tags", row.Keywords))
            .AppendLine("---")
            .ToString();

        return new MdxDocument($"{slug}.mdx", $"{frontmatter}\n{body}\n");
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
