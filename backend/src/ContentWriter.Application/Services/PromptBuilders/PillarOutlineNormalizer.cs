namespace ContentWriter.Application.Services.PromptBuilders;

/// <summary>
/// Keeps pillar outlines technical: declarative main H2s + a single FAQ section.
/// Question-shaped outline items are moved to the FAQ list (common when SERP/PAA dominate research).
/// </summary>
public static class PillarOutlineNormalizer
{
    private const string FaqSectionTitle = "People Also Ask";
    public const int MaxFaqQuestions = 12;

    public static (List<string> MainOutline, List<string> FaqQuestions) Sanitize(
        IReadOnlyList<string> sectionOutline,
        IReadOnlyList<string> paaFromResearch,
        string? targetKeyword = null)
    {
        var main = new List<string>();
        var faqFromOutline = new List<string>();

        foreach (var raw in sectionOutline)
        {
            var item = raw.Trim();
            if (item.Length == 0)
            {
                continue;
            }

            if (IsFaqSectionTitle(item))
            {
                continue;
            }

            if (LooksLikeQuestion(item))
            {
                faqFromOutline.Add(NormalizeQuestion(item));
            }
            else
            {
                main.Add(item);
            }
        }

        main = DeduplicateToolsSections(main);
        EnsureToolsSection(main, targetKeyword);

        if (main.Count == 0)
        {
            main.AddRange(DefaultMainSections());
        }

        if (!main.Any(IsFaqSectionTitle))
        {
            main.Add(FaqSectionTitle);
        }

        var allFaq = faqFromOutline
            .Concat(paaFromResearch.Select(NormalizeQuestion))
            .Where(q => q.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxFaqQuestions)
            .ToList();

        return (main, allFaq);
    }

    public static bool LooksLikeQuestion(string heading)
    {
        var text = heading.Trim();
        if (text.EndsWith('?'))
        {
            return true;
        }

        ReadOnlySpan<string> prefixes =
        [
            "what ", "how ", "why ", "when ", "where ", "who ",
            "is ", "are ", "can ", "does ", "do ", "should ", "will "
        ];

        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsFaqSectionTitle(string heading)
    {
        var text = heading.Trim();
        return text.Equals(FaqSectionTitle, StringComparison.OrdinalIgnoreCase)
               || text.Equals("Frequently Asked Questions", StringComparison.OrdinalIgnoreCase)
               || text.Equals("FAQ", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsToolsSection(string heading) => PillarSectionClassifier.IsToolsSection(heading);

    private static bool IsGenericToolsLabel(string heading)
    {
        var text = heading.Trim();
        return text.Equals("Tools/Platforms", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Tools", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Platforms", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Tools and Platforms", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> DeduplicateToolsSections(List<string> main)
    {
        var toolsIndices = main
            .Select((heading, index) => (heading, index))
            .Where(x => IsToolsSection(x.heading))
            .ToList();

        if (toolsIndices.Count <= 1)
        {
            return main;
        }

        var keepIndex = toolsIndices
            .OrderByDescending(x => x.heading.Length)
            .ThenByDescending(x => IsGenericToolsLabel(x.heading) ? 0 : 1)
            .First()
            .index;

        return main
            .Where((heading, index) => !IsToolsSection(heading) || index == keepIndex)
            .ToList();
    }

    private static void EnsureToolsSection(List<string> main, string? targetKeyword)
    {
        if (main.Any(IsToolsSection))
        {
            return;
        }

        var heading = string.IsNullOrWhiteSpace(targetKeyword)
            ? "Top AI Tools and Platforms"
            : $"Top AI Tools for {FormatKeywordForHeading(targetKeyword)}";

        var insertAt = main.FindIndex(h =>
            h.Contains("case stud", StringComparison.OrdinalIgnoreCase)
            || h.Contains("roi", StringComparison.OrdinalIgnoreCase)
            || h.Contains("measur", StringComparison.OrdinalIgnoreCase)
            || h.Contains("pilot", StringComparison.OrdinalIgnoreCase));

        if (insertAt < 0)
        {
            insertAt = main.Count;
        }

        main.Insert(insertAt, heading);
    }

    private static string FormatKeywordForHeading(string keyword)
    {
        var words = keyword.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "This Topic";
        }

        return string.Join(' ', words.Select(w =>
            w.Length == 0 ? w : char.ToUpper(w[0]) + w[1..].ToLowerInvariant()));
    }

    private static string NormalizeQuestion(string question)
    {
        var text = question.Trim();
        return text.EndsWith('?') ? text : $"{text}?";
    }

    private static IEnumerable<string> DefaultMainSections() =>
    [
        "Overview and Key Concepts",
        "Technical Architecture and Implementation",
        "Best Practices and Common Pitfalls",
        "Business Impact and ROI"
    ];
}
