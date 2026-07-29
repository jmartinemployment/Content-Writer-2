namespace ContentWriter.Application.Services;

// Extracted from Geek-SEO/GeekSeo.Application/Services/TopicClusteringService.cs — only the
// self-contained ClusterKeywordList path (and its private helpers) was pulled in; the rest of
// that file depends on GscQueryRow/GSC-specific types content-writer-v2 doesn't have. No shared
// package exists between these two separately-deployed repos, so this is a manual extraction,
// not a byte-identical copy — keep in sync by hand if the source method's logic changes.
public static class TopicClusteringService
{
    public static IReadOnlyList<(string ClusterName, string PillarKeyword, IReadOnlyList<string> Keywords)> ClusterKeywordList(
        IReadOnlyList<string> keywords,
        IReadOnlyDictionary<string, string>? serpSignatureByKeyword = null)
    {
        var distinct = keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0)
            return [];

        var groups = distinct
            .GroupBy(k => ResolveKeywordClusterKey(k, serpSignatureByKeyword), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var list = g.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                var pillar = list[0];
                var label = ClusterLabelFromKey(g.Key, pillar);
                return (label, pillar, (IReadOnlyList<string>)list);
            })
            .OrderByDescending(g => g.Item3.Count)
            .ToList();

        return groups;
    }

    private static string ResolveKeywordClusterKey(
        string keyword,
        IReadOnlyDictionary<string, string>? serpSignatureByKeyword)
    {
        if (serpSignatureByKeyword?.TryGetValue(keyword, out var signature) == true
            && !string.IsNullOrWhiteSpace(signature))
        {
            return $"serp:{signature}";
        }

        return $"token:{ClusterKeyFromQuery(keyword)}";
    }

    private static string ClusterKeyFromQuery(string query)
    {
        var words = query.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && !StopWords.Contains(w))
            .OrderBy(w => w, StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        return words.Length > 0 ? string.Join(' ', words) : query.ToLowerInvariant();
    }

    private static string ClusterLabelFromKey(string key, string fallbackKeyword)
    {
        if (key.StartsWith("page:", StringComparison.Ordinal))
            return TitleFromPageUrl(key["page:".Length..]);

        if (key.StartsWith("serp:", StringComparison.Ordinal))
            return TitleCaseSlug(fallbackKeyword);

        if (key.StartsWith("token:", StringComparison.Ordinal))
            return TitleCaseSlug(key["token:".Length..]);

        return TitleCaseSlug(fallbackKeyword);
    }

    private static string TitleFromPageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return TitleCaseSlug(url);

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return "Homepage";

        return TitleCaseSlug(segments[^1].Replace('-', ' ').Replace('_', ' '));
    }

    private static string TitleCaseSlug(string value) =>
        string.Join(
            ' ',
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "your", "that", "this", "what", "when", "where", "how", "best",
        "near", "local", "services", "service", "company", "companies",
    };
}
