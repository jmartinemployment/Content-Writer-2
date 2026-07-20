using ContentWriter.Application.Providers;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ContentWriter.Application.Services.Review;

public interface IEditorialReviewService
{
    /// <summary>
    /// Runs one review pass against a content row's current BodyHtml, using a different LLM than
    /// the one that wrote it. Scoped to qualitative judgment only — invented-feature/fact check,
    /// pillar-vs-tool consistency, brand voice vs ImplementerPositioning. Structural rules
    /// (heading hierarchy, word-count floors, etc.) are a separate, still-unbuilt concern — see
    /// DECISIONS.md / plan critique; this service does not attempt to enforce them.
    /// </summary>
    Task<ReviewOutcome> ReviewAsync(GeneratedContent content, string targetKeyword, CancellationToken cancellationToken = default);
}

public sealed record ReviewOutcome(
    ReviewVerdictStatus Status, string NotesJson, LlmProviderType ReviewerProvider, string ReviewerModel,
    int RetryCount = 0, string? RetryReason = null);

public sealed class EditorialReviewService : IEditorialReviewService
{
    private const string RubricSystemPrompt = """
        You are an editorial reviewer for B2B technical content. You did not write this piece —
        review it with a critical, skeptical eye. You are checking qualitative judgment only:
        structural rules (headings, word count) are already enforced elsewhere and are not your job.

        Check for:
        1. Invented features or unverifiable claims about named tools/platforms — flag anything
           that reads as fabricated capability.
        2. Consistency: does this content align with the stated brand positioning, or does it
           contradict/undercut it?
        3. Brand voice: professional, consultative, specific — not generic marketing filler.

        Respond with ONLY a JSON object, no markdown fences:
        {"verdict": "approved" | "revise", "notes": "<one paragraph, specific and actionable if revise>"}
        """;

    private readonly IContentProviderFactory _providerFactory;
    private readonly CompanyProfileOptions _companyProfile;

    public EditorialReviewService(IContentProviderFactory providerFactory, IOptions<CompanyProfileOptions> companyProfile)
    {
        _providerFactory = providerFactory;
        _companyProfile = companyProfile.Value;
    }

    public async Task<ReviewOutcome> ReviewAsync(GeneratedContent content, string targetKeyword, CancellationToken cancellationToken = default)
    {
        var reviewer = _providerFactory.Get(PickReviewerProvider(content.GeneratedByProvider));

        var userPrompt = $"""
            Target keyword: {targetKeyword}
            Content type: {content.ContentType}
            Brand positioning: {_companyProfile.ImplementerPositioning}

            Title: {content.Title}

            Body HTML:
            {content.BodyHtml}
            """;

        var request = new ChatCompletionRequest(
            Messages:
            [
                new ChatMessage(ChatRole.System, RubricSystemPrompt),
                new ChatMessage(ChatRole.User, userPrompt),
            ],
            Temperature: 0.2);

        var result = await reviewer.CompleteAsync(request, cancellationToken);
        var parsed = LlmResponseJsonParser.Parse<ReviewResponse>(result.Content, "editorial review");

        var status = parsed.Verdict?.Trim().ToLowerInvariant() switch
        {
            "approved" => ReviewVerdictStatus.Approved,
            "revise" => ReviewVerdictStatus.Revise,
            _ => throw new ContentGenerationException($"Reviewer returned an unrecognized verdict: '{parsed.Verdict}'."),
        };

        return new ReviewOutcome(status, result.Content, reviewer.ProviderType, result.ModelUsed, result.RetryCount, result.RetryReason);
    }

    /// <summary>Always a different, cheaper model than the writer — Groq (Llama) reviews everything except its own output, which OpenAI reviews instead.</summary>
    private static LlmProviderType PickReviewerProvider(LlmProviderType writerProvider) =>
        writerProvider == LlmProviderType.Groq ? LlmProviderType.OpenAi : LlmProviderType.Groq;

    private sealed record ReviewResponse(string? Verdict, string? Notes);
}
