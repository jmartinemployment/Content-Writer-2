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
}
