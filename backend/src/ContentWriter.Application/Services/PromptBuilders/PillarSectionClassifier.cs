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
