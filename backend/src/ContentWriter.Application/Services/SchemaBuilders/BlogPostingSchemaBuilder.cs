using System.Text.Json;
using System.Text.Json.Serialization;
using ContentWriter.Application.DTOs;

namespace ContentWriter.Application.Services.SchemaBuilders;

public interface IBlogPostingSchemaBuilder
{
    /// <summary>Builds a schema.org BlogPosting JSON+LD document that cites the source TechnicalArticle.</summary>
    string Build(ContentMetadata metadata, string relatedArticleUrl);
}

public class BlogPostingSchemaBuilder : IBlogPostingSchemaBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public string Build(ContentMetadata metadata, string relatedArticleUrl)
    {
        var schema = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
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
            // Cross-link back to the source TechArticle this post expands on.
            ["citation"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["@type"] = "TechArticle",
                    ["url"] = relatedArticleUrl
                }
            }
        };

        return JsonSerializer.Serialize(schema, JsonOptions);
    }
}
