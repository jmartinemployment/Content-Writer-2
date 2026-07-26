using System.Text;

namespace ContentWriter.Application.Services;

/// <summary>
/// Geek At Your Spot brand voice presets for the project tone dropdown.
/// <see cref="CrawledSite.DetectedTone"/> stores the preset <see cref="BrandTone.Id"/>.
/// </summary>
public static class BrandTones
{
    public const string DefaultId = Webpages;

    public const string Webpages = "webpages";
    public const string Email = "email";
    public const string LinkedIn = "linkedin";
    public const string Facebook = "facebook";

    public static readonly IReadOnlyList<BrandTone> All =
    [
        new(
            Webpages,
            "Webpages — Clear and Reassuring",
            "Build trust fast: put the benefit in the main headline, use bullet points for readability, " +
            "avoid blocks of text. State what you do within the first moments. Emphasize plain English, " +
            "affordable plans for small teams, and real results (saved time, fewer errors, happier customers)."),
        new(
            Email,
            "Email — Personal and Direct",
            "Keep emails structured, respectful of their time, and focused on one specific problem. " +
            "Informal but professional greeting. State the value in the first two sentences. One clear ask."),
        new(
            LinkedIn,
            "LinkedIn — Expert and Collaborative",
            "Share insights, industry trends, and practical lessons. Real examples about small-business growth. " +
            "Conversational formatting. Avoid sounding like a pushy salesperson."),
        new(
            Facebook,
            "Facebook — Local and Approachable",
            "Warm language, local connection, highly accessible. Keep paragraphs very short. " +
            "Friendly invitation to continue the conversation (e.g. DM) without hard selling."),
    ];

    public static readonly string SharedBaseGuidance =
        """
        Brand voice — always apply:
        - Clear and simple: skip heavy tech terms; use plain words to explain hard ideas.
        - Helpful and patient: act as a guide, not a teacher; respect what they already know.
        - Results-focused: talk about saved time, lower costs, and more sales — concrete outcomes.
        - Confident and honest: be real about what AI can do today and what it cannot.
        How to sound:
        - Be direct: short sentences; get right to the point.
        - Stay calm: do not hype AI like a magic trick; keep feet on the ground.
        - Show care: acknowledge specific pain points before pitching a tool.
        """;

    public static bool IsValid(string? toneId) =>
        !string.IsNullOrWhiteSpace(toneId) && All.Any(t => t.Id.Equals(toneId.Trim(), StringComparison.OrdinalIgnoreCase));

    public static BrandTone? TryGet(string? toneId)
    {
        if (string.IsNullOrWhiteSpace(toneId))
        {
            return null;
        }

        return All.FirstOrDefault(t => t.Id.Equals(toneId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static BrandTone GetOrDefault(string? toneId) => TryGet(toneId) ?? All.First(t => t.Id == DefaultId);

    /// <summary>Maps legacy crawl-heuristic labels (or unknown values) onto a brand preset id.</summary>
    public static string MapFromDetected(string? heuristicOrId)
    {
        if (IsValid(heuristicOrId))
        {
            return heuristicOrId!.Trim().ToLowerInvariant();
        }

        if (string.IsNullOrWhiteSpace(heuristicOrId))
        {
            return DefaultId;
        }

        var text = heuristicOrId.Trim();
        if (text.Contains("conversational", StringComparison.OrdinalIgnoreCase)
            || text.Contains("approachable", StringComparison.OrdinalIgnoreCase))
        {
            return Facebook;
        }

        if (text.Contains("technical", StringComparison.OrdinalIgnoreCase)
            || text.Contains("authoritative", StringComparison.OrdinalIgnoreCase))
        {
            return LinkedIn;
        }

        if (text.Contains("formal", StringComparison.OrdinalIgnoreCase))
        {
            return Webpages;
        }

        return DefaultId;
    }

    /// <summary>Prompt block: preset label + shared base + channel-specific guidance.</summary>
    public static string FormatForPrompt(string? toneId)
    {
        var tone = GetOrDefault(toneId);
        var sb = new StringBuilder();
        sb.AppendLine($"Brand tone preset: {tone.Label} (id: {tone.Id}).");
        sb.AppendLine(SharedBaseGuidance.Trim());
        sb.AppendLine($"Channel mode ({tone.Label}): {tone.PromptGuidance}");
        return sb.ToString().TrimEnd();
    }
}

public sealed record BrandTone(string Id, string Label, string PromptGuidance);
