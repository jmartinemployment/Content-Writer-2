using System.Text;

namespace ContentWriter.Application.Services;

/// <summary>
/// Geek At Your Spot brand voice applied automatically to all generation.
/// Shared base is always on; channel guidance is selected by content type
/// (webpages for pillar/blog/tools, email for cold outreach, LinkedIn/Facebook for social).
/// </summary>
public static class BrandTones
{
    public const string Webpages = "webpages";
    public const string Email = "email";
    public const string LinkedIn = "linkedin";
    public const string Facebook = "facebook";

    private static readonly BrandTone WebpagesTone = new(
        Webpages,
        "Webpages — Clear and Reassuring",
        "Build trust fast: put the benefit in the main headline, use bullet points for readability, " +
        "avoid blocks of text. State what you do within the first moments. Emphasize plain English, " +
        "affordable plans for small teams, and real results (saved time, fewer errors, happier customers).");

    private static readonly BrandTone EmailTone = new(
        Email,
        "Email — Personal and Direct",
        "Keep emails structured, respectful of their time, and focused on one specific problem. " +
        "Informal but professional greeting. State the value in the first two sentences. One clear ask.");

    private static readonly BrandTone LinkedInTone = new(
        LinkedIn,
        "LinkedIn — Expert and Collaborative",
        "Share insights, industry trends, and practical lessons. Real examples about small-business growth. " +
        "Conversational formatting. Avoid sounding like a pushy salesperson.");

    private static readonly BrandTone FacebookTone = new(
        Facebook,
        "Facebook — Local and Approachable",
        "Warm language, local connection, highly accessible. Keep paragraphs very short. " +
        "Friendly invitation to continue the conversation (e.g. DM) without hard selling.");

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

    public static string FormatSharedForPrompt() => SharedBaseGuidance.Trim();

    public static string ForWebpages() => FormatForChannel(WebpagesTone);

    public static string ForEmail() => FormatForChannel(EmailTone);

    public static string ForLinkedIn() => FormatForChannel(LinkedInTone);

    public static string ForFacebook() => FormatForChannel(FacebookTone);

    /// <summary>Maps a social platform name to the matching channel voice block.</summary>
    public static string ForSocialPlatform(string platform) =>
        platform.Contains("linkedin", StringComparison.OrdinalIgnoreCase) ? ForLinkedIn()
        : platform.Contains("facebook", StringComparison.OrdinalIgnoreCase) ? ForFacebook()
        : ForWebpages();

    private static string FormatForChannel(BrandTone tone)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SharedBaseGuidance.Trim());
        sb.AppendLine($"Channel mode ({tone.Label}): {tone.PromptGuidance}");
        return sb.ToString().TrimEnd();
    }
}

public sealed record BrandTone(string Id, string Label, string PromptGuidance);
