using System.Text.Json;
using System.Text.Json.Serialization;
using ContentWriter.Application.DTOs;

namespace ContentWriter.Application.Services.SchemaBuilders;

public sealed record SoftwareApplicationDescriptor(string Name, string? Description, string? Url = null);

public interface ISoftwareApplicationSchemaBuilder
{
    IReadOnlyList<Dictionary<string, object?>> BuildNodes(IReadOnlyList<SoftwareApplicationDescriptor> applications);
    string BuildGraph(IReadOnlyList<SoftwareApplicationDescriptor> applications);

    /// <summary>Full JSON+LD for a standalone tool overview page (primary node: SoftwareApplication).</summary>
    string BuildToolPage(ContentMetadata metadata, string pillarArticleUrl, SoftwareApplicationDescriptor about);
}

public class SoftwareApplicationSchemaBuilder : ISoftwareApplicationSchemaBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public IReadOnlyList<Dictionary<string, object?>> BuildNodes(IReadOnlyList<SoftwareApplicationDescriptor> applications)
    {
        return applications
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .Select(BuildNode)
            .ToList();
    }

    public string BuildGraph(IReadOnlyList<SoftwareApplicationDescriptor> applications)
    {
        var nodes = BuildNodes(applications);
        if (nodes.Count == 0)
        {
            return string.Empty;
        }

        var graph = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = nodes
        };

        return JsonSerializer.Serialize(graph, JsonOptions);
    }

    public string BuildToolPage(ContentMetadata metadata, string pillarArticleUrl, SoftwareApplicationDescriptor about)
    {
        var node = BuildNode(about);
        node["@context"] = "https://schema.org";
        node["headline"] = metadata.Headline;
        node["description"] = metadata.Description;
        node["url"] = metadata.CanonicalUrl;
        node["image"] = new[] { metadata.MainImageUrl };
        node["author"] = new Dictionary<string, object?>
        {
            ["@type"] = "Person",
            ["name"] = metadata.AuthorName
        };
        node["publisher"] = new Dictionary<string, object?>
        {
            ["@type"] = "Organization",
            ["name"] = metadata.PublisherName,
            ["logo"] = new Dictionary<string, object?>
            {
                ["@type"] = "ImageObject",
                ["url"] = metadata.PublisherLogoUrl
            }
        };
        node["datePublished"] = metadata.DatePublishedUtc.ToString("O");
        node["dateModified"] = metadata.DateModifiedUtc.ToString("O");
        node["mainEntityOfPage"] = new Dictionary<string, object?>
        {
            ["@type"] = "WebPage",
            ["@id"] = metadata.CanonicalUrl
        };
        node["keywords"] = string.Join(", ", metadata.Keywords);
        node["subjectOf"] = new Dictionary<string, object?>
        {
            ["@type"] = "TechArticle",
            ["@id"] = pillarArticleUrl
        };

        return JsonSerializer.Serialize(node, JsonOptions);
    }

    private static Dictionary<string, object?> BuildNode(SoftwareApplicationDescriptor application)
    {
        var node = new Dictionary<string, object?>
        {
            ["@type"] = "SoftwareApplication",
            ["name"] = application.Name.Trim(),
            ["applicationCategory"] = "BusinessApplication",
            ["operatingSystem"] = "Web"
        };

        if (!string.IsNullOrWhiteSpace(application.Description))
        {
            node["description"] = application.Description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(application.Url))
        {
            node["url"] = application.Url;
        }

        return node;
    }
}
