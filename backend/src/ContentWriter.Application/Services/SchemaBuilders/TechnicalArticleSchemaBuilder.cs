using System.Text.Json;
using System.Text.Json.Serialization;
using ContentWriter.Application.DTOs;

namespace ContentWriter.Application.Services.SchemaBuilders;

public interface ITechnicalArticleSchemaBuilder
{
    /// <summary>Builds a schema.org TechnicalArticle JSON+LD document that cites the companion blog post.</summary>
    string Build(
        ContentMetadata metadata,
        string relatedBlogPostUrl,
        IReadOnlyList<SoftwareApplicationDescriptor>? softwareApplications = null);
}

public class TechnicalArticleSchemaBuilder : ITechnicalArticleSchemaBuilder
{
    private readonly ISoftwareApplicationSchemaBuilder _softwareApplicationSchemaBuilder;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public TechnicalArticleSchemaBuilder(ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder)
    {
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
    }

    public string Build(
        ContentMetadata metadata,
        string relatedBlogPostUrl,
        IReadOnlyList<SoftwareApplicationDescriptor>? softwareApplications = null)
    {
        var articleNode = BuildArticleNode(metadata, relatedBlogPostUrl);
        var softwareNodes = softwareApplications is { Count: > 0 }
            ? _softwareApplicationSchemaBuilder.BuildNodes(softwareApplications)
            : [];

        if (softwareNodes.Count == 0)
        {
            return JsonSerializer.Serialize(articleNode, JsonOptions);
        }

        var graph = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new List<Dictionary<string, object?>>([articleNode, ..softwareNodes])
        };

        return JsonSerializer.Serialize(graph, JsonOptions);
    }

    private static Dictionary<string, object?> BuildArticleNode(ContentMetadata metadata, string relatedBlogPostUrl)
    {
        return new Dictionary<string, object?>
        {
            ["@type"] = "TechnicalArticle",
            ["headline"] = metadata.Headline,
            ["description"] = metadata.Description,
            ["image"] = new[] { metadata.MainImageUrl },
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = metadata.AuthorName
            },
            ["publisher"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = metadata.PublisherName,
                ["logo"] = new Dictionary<string, object?>
                {
                    ["@type"] = "ImageObject",
                    ["url"] = metadata.PublisherLogoUrl
                }
            },
            ["datePublished"] = metadata.DatePublishedUtc.ToString("O"),
            ["dateModified"] = metadata.DateModifiedUtc.ToString("O"),
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = metadata.CanonicalUrl
            },
            ["keywords"] = string.Join(", ", metadata.Keywords),
            ["wordCount"] = metadata.WordCount,
            ["proficiencyLevel"] = "Beginner",
            ["citation"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["@type"] = "BlogPosting",
                    ["url"] = relatedBlogPostUrl
                }
            }
        };
    }
}
