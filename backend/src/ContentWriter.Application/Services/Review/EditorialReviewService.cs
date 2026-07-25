using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.Export;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ContentWriter.Application.Services.Review;

public interface IEditorialReviewService
{
    /// <summary>
    /// Runs one review pass against a content row's current Body, using a different LLM than
    /// the one that wrote it. Evaluates against a 5-point rubric — pain-first framing,
    /// specificity, keyword-as-pain-point, invented-fact checking against supplied research
    /// context, and structural hygiene. Structural word-count floors etc. are a separate,
    /// still-unbuilt concern; this service does not attempt to enforce them.
    /// </summary>
    Task<ReviewOutcome> ReviewAsync(GeneratedContent content, ProjectGenerationContext context, CancellationToken cancellationToken = default);
}

public sealed record ReviewOutcome(
    ReviewVerdictStatus Status, string NotesJson, LlmProviderType ReviewerProvider, string ReviewerModel,
    int RetryCount = 0, string? RetryReason = null);

public sealed class EditorialReviewService : IEditorialReviewService
{
    private const string RubricSystemPrompt = """
        You are an editorial reviewer for AI implementation content targeting small business
        department heads. You did not write this piece — review it with a critical, skeptical eye.
        Evaluate against this rubric, in this order:

        1. Pain-first framing: does every H2 section open from the practitioner's problem before
           introducing the solution? Flag any section that leads with technology or capability
           rather than pain.
        2. Specificity: does the content name concrete outcomes, measurable reductions, or real
           workflow changes — or does it make generic claims that could apply to any software
           product? Flag any generic claim.
        3. Keyword as pain point: does the content treat the target keyword as a problem to be
           solved, not a topic to describe? Flag any passage that reads as feature description
           rather than problem resolution.
        4. No invented facts: does the content make specific claims (statistics, named product
           features, regulatory citations) that cannot be verified from the supplied research
           context? Flag any unverifiable specific claim.
        5. Structural hygiene: no preamble before the first H2, no "Related Links" or
           "Further Reading" H2, no duplicate heading text, meta description between 140-160
           characters.

        Respond with ONLY a JSON object, no markdown fences:
        {"verdict": "approved" | "revise", "notes": "<string|null>"}

        Verdict rules:
        - "approved": content meets all rubric criteria. Set notes to null.
        - "revise": content fails one or more criteria. Set notes to a numbered list of specific,
          actionable changes the writer must make — one item per failure, no prose evaluation, no
          explanation of why it matters. Each item must name the exact section or paragraph it
          applies to and state precisely what must change. Copy heading text exactly as it appears
          in the content you were given — do not paraphrase or normalize it.

        Notes format when verdict is "revise" — use exactly this structure:
        1. [Section: "{exact heading text}"] {one sentence stating what must change, not why}
        2. [Section: "{exact heading text}"] {one sentence stating what must change, not why}

        Example of a correct notes entry:
        1. [Section: "Automating Invoice Matching"] Rewrite the opening paragraph to begin with the
           cost of manual matching errors before introducing the AI solution.
        2. [Section: "Meta description"] Shorten to under 160 characters and remove the phrase
           "cutting-edge."

        Example of an incorrect notes entry (too vague, prose evaluation, no section reference):
        "The content is too generic and doesn't feel pain-focused enough. Consider rewriting some
        sections."
        """;

    private readonly IContentProviderFactory _providerFactory;
    private readonly CompanyProfileOptions _companyProfile;

    public EditorialReviewService(IContentProviderFactory providerFactory, IOptions<CompanyProfileOptions> companyProfile)
    {
        _providerFactory = providerFactory;
        _companyProfile = companyProfile.Value;
    }

    public async Task<ReviewOutcome> ReviewAsync(GeneratedContent content, ProjectGenerationContext context, CancellationToken cancellationToken = default)
    {
        var reviewer = _providerFactory.Get(PickReviewerProvider(content.GeneratedByProvider));

        var researchBrief = ResearchBriefBuilder.Build(
            context, ResearchBriefPhase.Review,
            "Use the sources above only to judge whether specific claims in the content are verifiable (rubric point 4).");

        var userPrompt = $"""
            Target keyword: {context.TargetKeyword}
            Content type: {content.ContentType}
            Brand positioning: {_companyProfile.ImplementerPositioning}

            {researchBrief}

            Title: {content.Title}

            Body HTML:
            {(content.Body is null ? string.Empty : SectionHtmlRenderer.RenderFragment(content.Body))}
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
