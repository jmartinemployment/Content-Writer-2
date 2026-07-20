using ContentWriter.Domain.Enums;

namespace ContentWriter.Domain.Entities;

public class ReviewVerdict
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GeneratedContentId { get; set; }
    public GeneratedContent? GeneratedContent { get; set; }

    public ReviewVerdictStatus Status { get; set; }
    public string NotesJson { get; set; } = string.Empty;
    public LlmProviderType ReviewerProvider { get; set; }
    public string ReviewerModel { get; set; } = string.Empty;

    /// <summary>Live revision counter — increments on every review pass, capped at 3. The operator's visibility into a row that isn't converging.</summary>
    public int AttemptCount { get; set; }

    /// <summary>How many times the reviewer's LLM call itself was retried (e.g. rate-limited) before this verdict was produced.</summary>
    public int RetryCount { get; set; }

    /// <summary>Human-readable reason for the last retry, if any (e.g. "Groq rate limit (429)...").</summary>
    public string? RetryReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
