using System.Text.Json;

namespace ContentWriter.Application.Services.JsonLd;

public class JsonLdParserService : IJsonLdParserService
{
    private static readonly HashSet<string> OrganizationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Organization", "LocalBusiness", "ProfessionalService", "Corporation", "Store"
    };

    private static readonly HashSet<string> ArticleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Article", "BlogPosting", "NewsArticle", "TechnicalArticle", "ScholarlyArticle"
    };

    public JsonLdSiteSummary Summarize(IReadOnlyList<string> rawBlocks)
    {
        var summary = new JsonLdSiteSummary();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var block in rawBlocks)
        {
            if (string.IsNullOrWhiteSpace(block))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(block);
                foreach (var node in EnumerateNodes(document.RootElement))
                {
                    ExtractNode(node, summary, seen);
                }
            }
            catch (JsonException)
            {
                // Skip invalid JSON+LD blocks.
            }
        }

        return summary;
    }

    private static IEnumerable<JsonElement> EnumerateNodes(JsonElement root)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Object when root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array:
                foreach (var item in graph.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        yield return item;
                    }
                }
                yield break;

            case JsonValueKind.Object:
                yield return root;
                yield break;

            case JsonValueKind.Array:
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        yield return item;
                    }
                }
                yield break;
        }
    }

    private static void ExtractNode(JsonElement node, JsonLdSiteSummary summary, HashSet<string> seen)
    {
        var types = GetTypes(node).ToList();
        if (types.Count == 0)
        {
            return;
        }

        if (types.Any(t => OrganizationTypes.Contains(NormalizeType(t))))
        {
            AddUnique(summary.Organizations, FormatOrganization(node), seen);
            ExtractOfferCatalog(node, summary, seen);
            ExtractKnowsAbout(node, summary, seen);
            ExtractAreaServed(node, summary, seen);
        }

        if (types.Any(t => string.Equals(NormalizeType(t), "Person", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnique(summary.People, FormatPerson(node), seen);
        }

        if (types.Any(t => string.Equals(NormalizeType(t), "Service", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnique(summary.Services, FormatService(node), seen);
        }

        if (types.Any(t => string.Equals(NormalizeType(t), "Product", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnique(summary.Services, FormatProduct(node), seen);
        }

        if (types.Any(t => string.Equals(NormalizeType(t), "SoftwareApplication", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnique(summary.SoftwareApplications, FormatSoftwareApplication(node), seen);
        }

        if (types.Any(t => string.Equals(NormalizeType(t), "WebSite", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnique(summary.WebPages, FormatWebSite(node), seen);
        }

        if (types.Any(t => string.Equals(NormalizeType(t), "WebPage", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnique(summary.WebPages, FormatWebPage(node), seen);
        }

        if (types.Any(t => ArticleTypes.Contains(NormalizeType(t))))
        {
            AddUnique(summary.Articles, FormatArticle(node), seen);
        }

        if (types.Any(t => string.Equals(NormalizeType(t), "FAQPage", StringComparison.OrdinalIgnoreCase)))
        {
            ExtractFaqPage(node, summary, seen);
        }

        if (types.Any(t => string.Equals(NormalizeType(t), "Question", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnique(summary.FaqEntries, FormatQuestion(node), seen);
        }
    }

    private static void ExtractOfferCatalog(JsonElement node, JsonLdSiteSummary summary, HashSet<string> seen)
    {
        if (!node.TryGetProperty("hasOfferCatalog", out var catalog))
        {
            return;
        }

        WalkOfferCatalog(catalog, summary, seen);
    }

    private static void WalkOfferCatalog(JsonElement element, JsonLdSiteSummary summary, HashSet<string> seen)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                WalkOfferCatalog(item, summary, seen);
            }
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var name = GetString(element, "name");
        var description = GetString(element, "description");
        if (!string.IsNullOrWhiteSpace(name))
        {
            AddUnique(summary.Services, JoinNameDescription(name, description), seen);
        }

        if (element.TryGetProperty("itemListElement", out var items))
        {
            WalkOfferCatalog(items, summary, seen);
        }
    }

    private static void ExtractKnowsAbout(JsonElement node, JsonLdSiteSummary summary, HashSet<string> seen)
    {
        if (!node.TryGetProperty("knowsAbout", out var knowsAbout))
        {
            return;
        }

        foreach (var topic in EnumerateValues(knowsAbout))
        {
            AddUnique(summary.Topics, topic, seen);
        }
    }

    private static void ExtractAreaServed(JsonElement node, JsonLdSiteSummary summary, HashSet<string> seen)
    {
        if (!node.TryGetProperty("areaServed", out var areaServed))
        {
            return;
        }

        foreach (var area in EnumerateValues(areaServed))
        {
            AddUnique(summary.ServiceAreas, area, seen);
        }
    }

    private static void ExtractFaqPage(JsonElement node, JsonLdSiteSummary summary, HashSet<string> seen)
    {
        if (!node.TryGetProperty("mainEntity", out var mainEntity))
        {
            return;
        }

        foreach (var item in EnumerateObjectOrArray(mainEntity))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            AddUnique(summary.FaqEntries, FormatQuestion(item), seen);
        }
    }

    private static IEnumerable<JsonElement> EnumerateObjectOrArray(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                yield return item;
            }
            yield break;
        }

        yield return element;
    }

    private static string FormatOrganization(JsonElement node)
    {
        var name = GetString(node, "name");
        var description = GetString(node, "description");
        var telephone = GetString(node, "telephone");
        var address = FormatPostalAddress(node);
        var url = GetString(node, "url");

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
        if (!string.IsNullOrWhiteSpace(description)) parts.Add(description);
        if (!string.IsNullOrWhiteSpace(telephone)) parts.Add($"Phone: {telephone}");
        if (!string.IsNullOrWhiteSpace(address)) parts.Add($"Address: {address}");
        if (!string.IsNullOrWhiteSpace(url)) parts.Add($"URL: {url}");
        return string.Join(" | ", parts);
    }

    private static string FormatPerson(JsonElement node)
    {
        var name = GetString(node, "name");
        var jobTitle = GetString(node, "jobTitle");
        return JoinNameDescription(name, jobTitle);
    }

    private static string FormatService(JsonElement node) =>
        JoinNameDescription(GetString(node, "name"), GetString(node, "description"), GetString(node, "serviceType"));

    private static string FormatProduct(JsonElement node) =>
        JoinNameDescription(GetString(node, "name"), GetString(node, "description"));

    private static string FormatSoftwareApplication(JsonElement node) =>
        JoinNameDescription(
            GetString(node, "name"),
            GetString(node, "description"),
            GetString(node, "applicationCategory"));

    private static string FormatWebSite(JsonElement node) =>
        JoinNameDescription(GetString(node, "name"), GetString(node, "description"), GetString(node, "url"));

    private static string FormatWebPage(JsonElement node)
    {
        var headline = GetString(node, "headline") ?? GetString(node, "name");
        var description = GetString(node, "description");
        var url = GetString(node, "url");
        return JoinNameDescription(headline, description, url);
    }

    private static string FormatArticle(JsonElement node)
    {
        var headline = GetString(node, "headline") ?? GetString(node, "name");
        var description = GetString(node, "description");
        var keywords = GetStringList(node, "keywords");
        var keywordSuffix = keywords.Count > 0 ? $"Keywords: {string.Join(", ", keywords)}" : null;
        return JoinNameDescription(headline, description, keywordSuffix);
    }

    private static string FormatQuestion(JsonElement node)
    {
        var question = GetString(node, "name");
        var answer = GetString(node, "acceptedAnswer", "text")
                     ?? GetString(node, "acceptedAnswer", "description");
        if (string.IsNullOrWhiteSpace(question))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(answer)
            ? $"Q: {question}"
            : $"Q: {question} | A: {answer}";
    }

    private static string FormatPostalAddress(JsonElement node)
    {
        if (!node.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var parts = new[]
        {
            GetString(address, "streetAddress"),
            GetString(address, "addressLocality"),
            GetString(address, "addressRegion"),
            GetString(address, "postalCode"),
            GetString(address, "addressCountry")
        }.Where(p => !string.IsNullOrWhiteSpace(p));

        return string.Join(", ", parts);
    }

    private static IEnumerable<string> GetTypes(JsonElement node)
    {
        if (!node.TryGetProperty("@type", out var typeElement))
        {
            yield break;
        }

        if (typeElement.ValueKind == JsonValueKind.String)
        {
            var value = typeElement.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
            yield break;
        }

        if (typeElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in typeElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text.Trim();
                }
                yield break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var value in EnumerateValues(item))
                    {
                        yield return value;
                    }
                }
                yield break;

            case JsonValueKind.Object:
                var name = GetString(element, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    yield return name.Trim();
                }
                break;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => NullIfWhiteSpace(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? GetString(JsonElement element, string objectProperty, string nestedProperty)
    {
        if (!element.TryGetProperty(objectProperty, out var nested) || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(nested, nestedProperty);
    }

    private static List<string> GetStringList(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return new List<string>();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text)
                ? new List<string>()
                : text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim())
            .ToList();
    }

    private static string NormalizeType(string type)
    {
        var trimmed = type.Trim();
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private static string JoinNameDescription(params string?[] parts) =>
        string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddUnique(List<string> target, string value, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        if (seen.Add(normalized))
        {
            target.Add(normalized);
        }
    }
}
