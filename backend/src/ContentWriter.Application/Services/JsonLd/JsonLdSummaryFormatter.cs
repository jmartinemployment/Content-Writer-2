using System.Text;
using System.Text.Json;

namespace ContentWriter.Application.Services.JsonLd;

public static class JsonLdSummaryFormatter
{
    public static string Format(JsonLdSiteSummary summary)
    {
        if (!summary.HasContent)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== EXISTING STRUCTURED DATA (JSON+LD) ===");
        AppendSection(sb, "Organization / business", summary.Organizations);
        AppendSection(sb, "People", summary.People);
        AppendSection(sb, "Services & offers", summary.Services);
        AppendSection(sb, "Expertise / knowsAbout", summary.Topics);
        AppendSection(sb, "Service areas", summary.ServiceAreas);
        AppendSection(sb, "FAQ (from site schema)", summary.FaqEntries);
        AppendSection(sb, "Published articles (from site schema)", summary.Articles);
        AppendSection(sb, "Key web pages", summary.WebPages);
        AppendSection(sb, "Software applications (from site schema)", summary.SoftwareApplications);
        return sb.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder sb, string heading, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        sb.AppendLine(heading + ":");
        foreach (var line in lines)
        {
            sb.AppendLine($"- {line}");
        }
        sb.AppendLine();
    }
}
