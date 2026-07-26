namespace ContentWriter.Application.Services.PromptBuilders;

internal static class PillarSectionClassifier
{
    public static bool IsToolsSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        ReadOnlySpan<string> markers =
        [
            "tool", "platform", "software", "vendor", "solution", "stack", "technology"
        ];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsBestPracticesSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        ReadOnlySpan<string> markers = ["best practice", "checklist", "how to succeed", "successful"];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsBenefitsSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        ReadOnlySpan<string> markers = ["benefit", "advantage", "value of", "roi", "return on"];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsIntroductionSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        ReadOnlySpan<string> markers =
        [
            "introduction", "overview", "what is", "understanding", "getting started with"
        ];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsImplementationSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        // Avoid matching "how to succeed" best-practices headings — those use IsBestPracticesSection.
        if (text.Contains("how to succeed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<string> markers =
        [
            "implement", "implementation", "deploy", "deployment", "adoption", "getting started", "how to"
        ];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsFutureTrendsSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        ReadOnlySpan<string> markers = ["future", "trend", "what's next", "emerging", "outlook"];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
