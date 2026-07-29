using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using ContentWriter.Domain.Entities;

namespace ContentWriter.Application.Services.PromptBuilders;

public interface IContentPromptBuilder
{
    ChatCompletionRequest BuildTopicFocusPrompt(string siteName, IReadOnlyList<string> headings, IReadOnlyList<string> paragraphs);

    ChatCompletionRequest BuildArticleMetadataPrompt(ProjectGenerationContext context);

    ChatCompletionRequest BuildArticleLedePrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string? revisionNotes = null,
        string? existingLedeHeading = null);

    /// <summary>
    /// Small revise pass for meta description (and title only when notes explicitly demand it).
    /// Returns null when <paramref name="revisionNotes"/> do not mention meta/title hygiene.
    /// </summary>
    ChatCompletionRequest? BuildArticleMetaRevisionPrompt(
        ProjectGenerationContext context,
        string title,
        string metaDescription,
        string revisionNotes);

    ChatCompletionRequest BuildArticleSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections,
        IReadOnlyList<string> fullOutline,
        bool isRegeneration,
        string? revisionNotes = null);

    ChatCompletionRequest BuildArticleFaqSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions,
        bool isRegeneration,
        string? revisionNotes = null);

    /// <summary>Lightweight first Tools call: ordered list of 4–5 real platform names (no nested section JSON).</summary>
    ChatCompletionRequest BuildToolsPlatformListPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        bool isRegeneration,
        string? revisionNotes = null);

    /// <summary>One platform h3 subtree (overview, capability list, implementer h4) for the Tools section.</summary>
    ChatCompletionRequest BuildToolsPlatformChildPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        string platformName,
        IReadOnlyList<string> allPlatforms,
        int platformIndex,
        int platformCount,
        bool isRegeneration,
        string? revisionNotes = null);

    ChatCompletionRequest BuildBlogMetadataPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle);

    ChatCompletionRequest BuildBlogBodyPrompt(
        ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata, string? revisionNotes = null);

    ChatCompletionRequest BuildBlogLedePrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata);

    ChatCompletionRequest BuildBlogSectionPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections,
        string? revisionNotes = null);
    ChatCompletionRequest BuildSocialPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, string platform, string articleUrl);
    ChatCompletionRequest BuildColdOutreachPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, string articleUrl);
    ChatCompletionRequest BuildSectionImagePromptsPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogDraft sourceBlog,
        string articleUrl,
        string blogUrl,
        IReadOnlyList<ImagePromptSectionTarget> sections);

    ChatCompletionRequest BuildToolBodyPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        string toolSlug,
        string? revisionNotes = null);

    ChatCompletionRequest BuildToolMetadataPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        ContentDocument body);

    ChatCompletionRequest BuildSummaryVariantsPrompt(
        ProjectGenerationContext context,
        string title,
        ContentDocument body,
        string? metaDescription,
        string contentTypeLabel);

}

public class ContentPromptBuilder : IContentPromptBuilder
{
    private const string TopicFocusJsonContract =
        "{\"focus\": string[] (4-8 short topic phrases, 1-4 words each, describing the site's real services/subject matter — no generic filler words)}";

    /// <summary>
    /// The structured-output contract every section-body call uses instead of Markdown. No tag
    /// characters, no "##"/"###"/"- "/"[text](url)" syntax — headings are a plain string field,
    /// emphasis/links are boolean/url fields on a run, lists are their own paragraph variant. This
    /// is what actually eliminates truncated/malformed markup and leaked Markdown habit: there is no
    /// markup syntax available for the model to get wrong.
    /// </summary>
    private const string RunJsonShape =
        "{\"text\": string (plain text only — never markup or Markdown syntax), \"bold\": boolean?, \"italic\": boolean?, \"href\": string?}";

    private const string ParagraphJsonShape =
        "{\"type\":\"text\",\"runs\":[" + RunJsonShape + ", ...]} " +
        "OR {\"type\":\"list\",\"ordered\":boolean,\"items\":[[" + RunJsonShape + ", ...], ...]}";

    private const string SectionJsonContract =
        "{\"tag\": \"h2\"|\"h3\"|\"h4\"|\"h5\"|\"h6\", \"heading\": string (plain text, no markup), " +
        "\"paragraphs\": [" + ParagraphJsonShape + ", ...], \"href\": null, " +
        "\"children\": [Section, ...] (nested subsections, same shape, one level deeper tag)}";

    private const string SectionsArrayJsonContract =
        "{\"sections\": [" + SectionJsonContract + ", ...] (top-level h2 sections, in order)}";

    private const string LedeJsonContract =
        "{\"ledeType\": \"creative\"|\"summary\", \"heading\": string (a real written headline — never the literal words \"Creative Lead\"/\"Summary Lede\"), " +
        "\"paragraphs\": [" + ParagraphJsonShape + ", ...], " +
        "\"imagePrompt\": string (40-400 words describing an image to accompany this opening — subject, setting, style; no text/words rendered in the image)}";

    private static ChatCompletionRequest WithSectionSchema(ChatCompletionRequest request) =>
        request with { JsonSchemaName = "section", JsonSchema = ContentSectionJsonSchema.SectionSchema };

    private static ChatCompletionRequest WithSectionsArraySchema(ChatCompletionRequest request) =>
        request with { JsonSchemaName = "sections", JsonSchema = ContentSectionJsonSchema.SectionsArraySchema };

    public ChatCompletionRequest BuildTopicFocusPrompt(string siteName, IReadOnlyList<string> headings, IReadOnlyList<string> paragraphs)
    {
        var headingBlock = string.Join("\n", headings.Take(60).Select(h => $"- {h}"));
        var paragraphBlock = string.Join("\n\n", paragraphs.Take(30).Select(p => p.Length > 300 ? p[..300] + "…" : p));

        var system = new StringBuilder()
            .AppendLine("You extract the real topical focus of a business website from its crawled headings and body text.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences, no commentary.")
            .AppendLine(TopicFocusJsonContract)
            .AppendLine("Each phrase must name a real service, product, industry, or subject the site actually covers (e.g. \"managed IT services\", ")
            .AppendLine("\"AI implementation\", \"Salesforce consulting\") — never a generic word like \"business\", \"solutions\", \"help\", \"choose\", or \"build\" on its own.")
            .AppendLine("Prefer multi-word phrases over single words. If the site is thin/generic, return fewer, more honest phrases rather than padding with filler.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Site name: {siteName}")
            .AppendLine()
            .AppendLine("Headings crawled from the site:")
            .AppendLine(headingBlock)
            .AppendLine()
            .AppendLine("Body text excerpts crawled from the site:")
            .AppendLine(paragraphBlock)
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.2,
            MaxOutputTokens: 512);
    }

    private const string ArticleMetadataJsonContract =
        "{\"title\": string, \"metaDescription\": string (140-160 characters, must include the target keyword naturally, no hype), \"keywords\": string[] (5-10 items), \"sectionOutline\": string[] (5-7 declarative H2 headings — exactly ONE tools section with a descriptive name like \"Top AI Tools for {topic}\" (never a bare \"Tools/Platforms\" label), plus final item: \"People Also Ask\")}";

    private const string SocialJsonContract =
        "{\"text\": string}";

    private const string ColdOutreachJsonContract =
        "{\"subject\": string, \"bodyText\": string (50-125 words), \"ctaLabel\": string}";

    private const string ImagePromptSectionItemJsonContract =
        "{\"sourceType\": \"pillar-hero|blog-hero|pillar|blog\", \"heading\": string (exact H2 text, or the exact title for a -hero item), \"order\": number, \"prompt\": string (40-400 words), \"width\": number, \"height\": number, \"imageModel\": string, \"stylePreset\": string, \"alchemy\": boolean, \"photoReal\": boolean, \"notes\": string|null}";

    private const string ImagePromptSectionsJsonContract =
        "{\"sections\": [" + ImagePromptSectionItemJsonContract + ", ...]}";

    public ChatCompletionRequest BuildArticleMetadataPrompt(ProjectGenerationContext context)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine($"Detected site focus/topics: {context.DetectedFocus}.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences, no commentary.")
            .AppendLine(ArticleMetadataJsonContract)
            .AppendLine("GOOD sectionOutline example: [\"Overview of Enterprise AI\", \"Implementation Framework\", \"Top AI Platforms and Tools\", \"Measuring ROI\", \"People Also Ask\"]")
            .AppendLine("BAD sectionOutline example: [\"What is AI?\", \"How does it work?\"] — never use questions as main H2s.")
            .AppendLine("Meta description MUST be 140-160 characters, include the target keyword naturally, and stay factual — no hype words like \"cutting-edge\".")
            .ToString();

        var desiredHeadingsInstruction = context.DesiredHeadings is { Count: > 0 }
            ? "REQUIRED: after building your own comprehensive sectionOutline per the instructions above, ADD one extra H2 for each of these client-requested topics on top of that plan (worded naturally but preserving the topic). " +
              "Do NOT shrink, drop, or replace any of your own planned sections to make room for these — they are additions, not the whole outline: " +
              $"{string.Join(", ", context.DesiredHeadings.Select(h => $"\"{h}\""))}. "
            : string.Empty;

        var user = ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleMetadata,
            $"Plan a comprehensive pillar TechnicalArticle use case targeting the keyword \"{context.TargetKeyword}\" for {context.PublisherName}. " +
            "Derive sectionOutline from keyword SERP and local pack headings (declarative topics like \"Benefits of X\", not questions). " +
            "Frame this as a use case showing how AI implementation services solve the client problem — not just generic background. " +
            "REQUIRED: include exactly one tools H2 with a descriptive name (e.g. \"Top AI Tools for Sales Prospecting\") — platforms plus which problems an AI implementer solves. Never use a bare \"Tools/Platforms\" heading. " +
            desiredHeadingsInstruction +
            "Title must NOT be a question and must NOT start with \"How\" — use a definitive statement (e.g. \"AI Prospecting and Lead Intelligence: Implementation Guide\"). " +
            $"Meta description: 140-160 characters, include \"{context.TargetKeyword}\" naturally, concise factual summary for B2B readers, no hype. " +
            "End sectionOutline with exactly one FAQ section titled \"People Also Ask\" — PAA questions are answered there in the body step, not as main H2s. " +
            "Return title, metaDescription, keywords, and sectionOutline only (body is written separately).");

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.5,
            MaxOutputTokens: 1536);
    }

    public ChatCompletionRequest BuildArticleLedePrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string? revisionNotes = null,
        string? existingLedeHeading = null)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write the opening lede for a schema.org TechnicalArticle pillar — third person, expert, consultative, like a senior consultant advising a prospective client.")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Prefer a creative (hook/narrative) opening; use a summary (direct thesis-first) opening only if a creative angle genuinely doesn't fit this topic.")
            .AppendLine("The heading is a real written headline for the opening — never the literal words \"Creative Lead\"/\"Summary Lede\"; that label goes only in ledeType.")
            .AppendLine("Do NOT start with \"How\" or a question.")
            .AppendLine("PAIN BEFORE SOLUTION (required): the first paragraph must open on the practitioner's pain with the manual / status-quo process ")
            .AppendLine("for the target keyword (cost, delay, error, risk, wasted hours) — before naming AI or an intelligent solution.")
            .AppendLine("Only after that pain is established, introduce how an AI-assisted approach changes the situation. 2-3 paragraphs total.")
            .AppendLine("Also write imagePrompt: a prompt for an image-generation model to illustrate this opening.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences, no commentary:")
            .AppendLine(LedeJsonContract)
            .ToString();

        var ledeNotes = ScopeRevisionNotesForLede(revisionNotes, existingLedeHeading, metadata.SectionOutline);
        var revisionBlock = BuildRevisionNotesBlock(ledeNotes, sectionHeading: existingLedeHeading);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Meta description: {metadata.MetaDescription}")
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.65,
            MaxOutputTokens: 1024);
    }

    public ChatCompletionRequest? BuildArticleMetaRevisionPrompt(
        ProjectGenerationContext context,
        string title,
        string metaDescription,
        string revisionNotes)
    {
        var metaNotes = ScopeRevisionNotesForMeta(revisionNotes);
        if (string.IsNullOrWhiteSpace(metaNotes))
        {
            return null;
        }

        var system = new StringBuilder()
            .AppendLine("You revise SEO title and meta description for a TechnicalArticle pillar.")
            .AppendLine("Apply ONLY the reviewer's meta/title notes below. Keep the plan/outline unchanged.")
            .AppendLine("Meta description must be 140-160 characters, factual, no hype words like \"cutting-edge\".")
            .AppendLine("Change the title only when a note explicitly targets Title; otherwise return the current title unchanged.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences, no commentary:")
            .AppendLine("{\"title\": string, \"metaDescription\": string}")
            .AppendLine()
            .AppendLine("REVISION REQUIRED — address each of the following:")
            .AppendLine(metaNotes.Trim())
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Current title: {title}")
            .AppendLine($"Current meta description ({metaDescription.Length} chars): {metaDescription}")
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.3,
            MaxOutputTokens: 512);
    }

    public ChatCompletionRequest BuildArticleSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections,
        IReadOnlyList<string> fullOutline,
        bool isRegeneration,
        string? revisionNotes = null)
    {
        var outlineContext = string.Join("\n", fullOutline.Select((h, i) => $"{i + 1}. {h}"));
        var isBestPractices = PillarSectionClassifier.IsBestPracticesSection(sectionHeading);
        var isBenefits = PillarSectionClassifier.IsBenefitsSection(sectionHeading);
        var isIntroduction = PillarSectionClassifier.IsIntroductionSection(sectionHeading);
        var isImplementation = PillarSectionClassifier.IsImplementationSection(sectionHeading);
        var isFutureTrends = PillarSectionClassifier.IsFutureTrendsSection(sectionHeading);

        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write ONE section of a schema.org TechnicalArticle pillar — third person, expert, consultative, like a senior consultant advising a prospective client.")
            .AppendLine($"Pillar standard ({ContentLengthTargets.PillarRangeLabel} words): {ContentLengthTargets.PillarEditorialDefinition}")
            .AppendLine("Respond with ONLY a single valid JSON Section object for this section — no markdown fences, no commentary, no other sections.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's own tag is \"h2\". Do NOT write introductory paragraphs before it — the opening lede is generated separately.")
            .AppendLine("Include 2-3 h3 subsections nested in \"children\" with multiple text paragraphs, and at least one list paragraph where appropriate.")
            .AppendLine("Each h3 is a keyword-level topic and MUST itself nest 1-3 h4 children covering concrete subtopics of that h3 ")
            .AppendLine("(e.g. h3 \"AI Marketing Workflow\" → h4 \"AI Content Creation and Repurposing for Small Businesses\"). ")
            .AppendLine("Do not leave an h3 as a leaf with only paragraphs — every h3 needs at least one substantive h4 child.")
            .AppendLine("PROBLEM-FIRST OPENING (required): the first paragraph under this H2 must open on the practitioner problem this section addresses ")
            .AppendLine("(cost, delay, error, risk, wasted effort). Technology and capability come after that pain is clear.")
            .AppendLine("Do NOT open with \"AI enables…\", \"Intelligent X is…\", a product capability list, or a definition of the technology.")
            .AppendLine("Do not write this as a neutral textbook explainer of the general subject — every subsection should be framed through what an AI implementation " +
                $"consultancy like {context.PublisherName} ({context.ImplementerPositioning}) actually does about the problem being discussed, not just background education on it. " +
                "A reader should finish the section understanding a consultancy's specific angle on it, not just the general concept.")
            .AppendLine("If a hypothetical scenario is used, keep it to 1-2 sentences woven naturally into the surrounding paragraph — not a bolt-on closing paragraph that repeats what was already said.")
            .AppendLine("CRITICAL: there is no real case-study data available, so never present a named client, company, or engagement as if it were real. ")
            .AppendLine("A hypothetical scenario may still use a concrete operational outcome for punch (e.g. \"month-end close compressed from two weeks to three days\"), ")
            .AppendLine("but it MUST be explicitly labeled hypothetical/illustrative — e.g. \"a hypothetical mid-sized manufacturer\" or ")
            .AppendLine("\"in a representative scenario\". Never phrase it as something that already happened to a real client. ")
            .AppendLine("Do not reuse a stock \"40% reduction\" (or similar) percentage across sections — vary outcomes and make them operationally specific.")
            .AppendLine($"Target {ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words for this section. Do not write other sections.")
            .ToString();

        if (isIntroduction)
        {
            system += Environment.NewLine + BuildIntroductionSectionGuidance(context);
        }

        if (isBenefits)
        {
            system += Environment.NewLine + BuildBenefitsSectionGuidance(context);
        }

        if (isImplementation)
        {
            system += Environment.NewLine + BuildImplementationSectionGuidance(context);
        }

        if (isBestPractices)
        {
            system += Environment.NewLine + BuildBestPracticesSectionGuidance(context);
        }

        if (isFutureTrends)
        {
            system += Environment.NewLine + BuildFutureTrendsSectionGuidance(context);
        }

        if (isRegeneration)
        {
            system += Environment.NewLine + "REGENERATION: use fresh prose and examples.";
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: sectionHeading);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
            if (isBenefits || NotesAskForConcreteness(revisionNotes, sectionHeading))
            {
                system += Environment.NewLine + BuildConcretenessRevisionAmplifier();
            }
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection,
                $"Write section {sectionIndex + 1} of {totalSections}: \"{sectionHeading}\"."))
            .AppendLine()
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Section to write: {sectionHeading}")
            .AppendLine()
            .AppendLine("Full article outline (for context only — write ONLY the assigned section):")
            .AppendLine(outlineContext)
            .ToString();

        return WithSectionSchema(new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: isRegeneration ? 0.72 : 0.65,
            MaxOutputTokens: 2048));
    }

    public ChatCompletionRequest BuildToolsPlatformListPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        bool isRegeneration,
        string? revisionNotes = null)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine("Choose 4-5 major platforms or tools for the Tools H2 of a TechnicalArticle pillar.")
            .AppendLine("Only real, verifiable, well-known products relevant to the target keyword. Never invent a tool name or vendor.")
            .AppendLine("Prefer depth on 4 platforms over shallow coverage of 6.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences, no commentary:")
            .AppendLine("{\"platforms\": string[] (4-5 product names, display order)}")
            .ToString();

        if (isRegeneration)
        {
            system += Environment.NewLine + "REGENERATION: pick a fresh but still accurate platform set when the notes call for it; otherwise keep strong existing choices.";
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: toolsSectionHeading);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection,
                $"List platforms for Tools section \"{toolsSectionHeading}\"."))
            .AppendLine()
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Tools section heading: {toolsSectionHeading}")
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: isRegeneration ? 0.55 : 0.4,
            MaxOutputTokens: 512);
    }

    public ChatCompletionRequest BuildToolsPlatformChildPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        string platformName,
        IReadOnlyList<string> allPlatforms,
        int platformIndex,
        int platformCount,
        bool isRegeneration,
        string? revisionNotes = null)
    {
        var perPlatformTarget =
            $"{ContentLengthTargets.PillarToolsSectionMinWords / Math.Max(platformCount, 1)}" +
            $"-{ContentLengthTargets.PillarToolsSectionTargetMaxWords / Math.Max(platformCount, 1)}";

        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write ONE platform subsection for the Tools H2 of a TechnicalArticle pillar — third person, expert, consultative.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no markdown fences, no commentary, no other platforms.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's own tag is \"h3\". Heading must be exactly the assigned platform name.")
            .AppendLine("Include: a brief overview paragraph of what the platform does for this use case, then a list paragraph with 2-4 factual capability bullets.")
            .AppendLine("Then one child Section (tag h4, heading \"How an AI implementer helps with {Platform}\").")
            .AppendLine(BuildToolsPlatformChildGuidance(context, platformName))
            .AppendLine($"Target ~{perPlatformTarget} words for this platform subtree so the full Tools section lands near {ContentLengthTargets.PillarToolsSectionMinWords}-{ContentLengthTargets.PillarToolsSectionTargetMaxWords} words.")
            .AppendLine("Never invent a feature or capability; if unsure a feature exists, describe it generically instead of naming it.")
            .AppendLine("CRITICAL: there is no real case-study data available, so never present a named client, company, or engagement as if it were real. ")
            .AppendLine("A quantified outcome is fine only if explicitly labeled hypothetical/illustrative.")
            .ToString();

        if (isRegeneration)
        {
            system += Environment.NewLine + "REGENERATION: use fresh prose and examples.";
        }

        // Scope by platform name so Tool:/section notes about other platforms do not leak in.
        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: platformName);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var platformList = string.Join(", ", allPlatforms.Select((p, i) => i == platformIndex ? $"[{p}]" : p));
        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection,
                $"Write Tools platform {platformIndex + 1} of {platformCount}: \"{platformName}\"."))
            .AppendLine()
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Tools section heading: {toolsSectionHeading}")
            .AppendLine($"Platforms in this Tools section (write ONLY the bracketed one): {platformList}")
            .AppendLine($"Platform to write: {platformName}")
            .ToString();

        return WithSectionSchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: isRegeneration ? 0.72 : 0.65,
            MaxOutputTokens: 2048));
    }

    public ChatCompletionRequest BuildArticleFaqSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions,
        bool isRegeneration,
        string? revisionNotes = null)
    {
        var paaBlock = string.Join("\n", faqQuestions.Select((q, i) => $"  - Q{i + 1}: {q}"));

        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write ONLY the \"People Also Ask\" FAQ section of a TechnicalArticle pillar.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no markdown fences, no commentary.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's tag is \"h2\" and heading is exactly \"People Also Ask\". Each question is a child Section: tag \"h3\", heading is the question verbatim, paragraphs holds a 2-4 sentence answer.")
            .AppendLine("Direct, factual answers. Third person.")
            .AppendLine($"Answers must sound like {context.PublisherName} ({context.ImplementerPositioning}), not a generic textbook FAQ — reflect the same consultative brand voice as the rest of the article, not interchangeable boilerplate.")
            .ToString();

        if (isRegeneration)
        {
            system += Environment.NewLine + "REGENERATION: use fresh phrasing.";
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: "People Also Ask");
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleFaq,
                "Write the People Also Ask FAQ section."))
            .AppendLine()
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine()
            .AppendLine("Questions to answer:")
            .AppendLine(paaBlock)
            .ToString();

        return WithSectionSchema(new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: isRegeneration ? 0.7 : 0.6,
            MaxOutputTokens: 3072));
    }

    private const string BlogMetadataJsonContract =
        "{\"title\": string, \"metaDescription\": string (max 160 chars), \"keywords\": string[] (5-10 items), \"sectionOutline\": string[] (5-6 conversational H2 headings — hooks, numbered angles, or how-to framing; do NOT copy pillar H2s verbatim)}";

    public ChatCompletionRequest BuildBlogMetadataPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle)
    {
        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences, no commentary.")
            .AppendLine(BlogMetadataJsonContract)
            .AppendLine("The blog title MUST be different from the pillar title — use a conversational hook, question, or numbered angle (e.g. \"3 Ways...\", \"Why...\"). Never copy the pillar title verbatim.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar article title (do NOT reuse): {sourceArticle.Title}")
            .AppendLine($"Pillar summary: {sourceArticle.MetaDescription}")
            .AppendLine()
            .AppendLine($"Plan a deep-dive companion blog ({ContentLengthTargets.BlogRangeLabel} words) with a distinct title, angle, and {ContentLengthTargets.BlogSectionCountMin}-{ContentLengthTargets.BlogSectionCountTarget} fresh H2 section headings.")
            .AppendLine($"Editorial standard: {ContentLengthTargets.BlogEditorialDefinition}")
            .AppendLine("Each section must support substantive depth — data points, examples, and implementation context, not surface summaries.")
            .AppendLine("Return title, metaDescription, keywords, and sectionOutline only (body is written separately).")
            .ToString();

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.6,
            MaxOutputTokens: 1536);
    }

    public ChatCompletionRequest BuildBlogLedePrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata)
    {
        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write the opening lede for a schema.org BlogPosting deep-dive — conversational but substantive; first/second person allowed.")
            .AppendLine("Prefer a creative (hook/narrative) opening; use a summary (direct thesis-first) opening only if a creative angle genuinely doesn't fit this topic.")
            .AppendLine("2-3 paragraphs: hook, stakes, and who this is for.")
            .AppendLine("Also write imagePrompt: a prompt for an image-generation model to illustrate this opening.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences, no commentary:")
            .AppendLine(LedeJsonContract)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar title (reference only): {sourceArticle.Title}")
            .AppendLine($"Blog title: {metadata.Title}")
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.7,
            MaxOutputTokens: 1024);
    }

    public ChatCompletionRequest BuildBlogSectionPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections,
        string? revisionNotes = null)
    {
        var outlineContext = string.Join("\n", metadata.SectionOutline.Select((h, i) => $"{i + 1}. {h}"));
        var isLast = sectionIndex == totalSections - 1;

        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write ONE section of a schema.org BlogPosting deep-dive article — conversational but substantive; first/second person allowed.")
            .AppendLine($"Editorial standard ({ContentLengthTargets.BlogRangeLabel} words): {ContentLengthTargets.BlogEditorialDefinition}")
            .AppendLine("Respond with ONLY a single valid JSON Section object for this section — no markdown fences, no commentary, no other sections.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's own tag is \"h2\". Do NOT write introductory paragraphs before it — the opening lede is generated separately.")
            .AppendLine("Include 2-3 h3 subsections nested in \"children\" where helpful, multiple substantive paragraphs, at least one list paragraph with concrete tips, and a specific example or data point.")
            .AppendLine($"Target {ContentLengthTargets.BlogSectionMinWords}-{ContentLengthTargets.BlogSectionTargetMaxWords} words for this section alone. Shorter sections fail editorial review — add depth, not filler.")
            .AppendLine("Do NOT duplicate the pillar article structure or reuse its H2 headings verbatim.")
            .ToString();

        if (isLast)
        {
            system += Environment.NewLine +
                      $"Do not write a CTA/link to the pillar article yourself — that's appended afterward with the real URL. " +
                      $"Just close out this section's own content. Minimum total blog length across all sections is {ContentLengthTargets.BlogMinWords:N0} words.";
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: sectionHeading);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.BlogSection,
                $"Write section {sectionIndex + 1} of {totalSections}: \"{sectionHeading}\"."))
            .AppendLine()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar title (reference only): {sourceArticle.Title}")
            .AppendLine($"Blog title: {metadata.Title}")
            .AppendLine($"Section to write ({sectionIndex + 1}/{totalSections}): {sectionHeading}")
            .AppendLine()
            .AppendLine("Blog outline (write ONLY the assigned section):")
            .AppendLine(outlineContext)
            .ToString();

        return WithSectionSchema(new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.72,
            MaxOutputTokens: 4096));
    }

    public ChatCompletionRequest BuildBlogBodyPrompt(
        ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata, string? revisionNotes = null)
    {
        var pillarSections = sourceArticle.SectionOutline.Count > 0
            ? string.Join(", ", sourceArticle.SectionOutline)
            : "see pillar summary";

        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write a deep-dive blog that teases the pillar — do NOT duplicate the pillar structure or reuse its H2 headings verbatim.")
            .AppendLine("Use fresh headings (5-6 top-level sections). Substantive paragraphs with examples; first/second person allowed.")
            .AppendLine($"Target at least {ContentLengthTargets.BlogMinWords:N0} words (aim for {ContentLengthTargets.BlogRangeLabel}). Do not stop early.")
            .AppendLine("Respond with ONLY the sections array — no markdown fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .ToString();

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.BlogSection,
                "Write the full blog body (all sections)."))
            .AppendLine()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar title (link target — do not reuse as blog title): {sourceArticle.Title}")
            .AppendLine($"Pillar summary: {sourceArticle.MetaDescription}")
            .AppendLine($"Pillar section topics (for reference only — do not copy as H2s): {pillarSections}")
            .AppendLine()
            .AppendLine($"Blog title: {metadata.Title}")
            .AppendLine($"Blog meta description: {metadata.MetaDescription}")
            .AppendLine()
            .AppendLine("Write the blog body sections. Summarize 2-3 key takeaways, add a practical tip or short story, and end with a CTA to read the full technical article for implementation depth.")
            .ToString();

        return WithSectionsArraySchema(new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.7,
            MaxOutputTokens: 6144));
    }

    public ChatCompletionRequest BuildSocialPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, string platform, string articleUrl)
    {
        var (styleGuidance, lengthGuidance, maxTokens) = platform switch
        {
            "Facebook" => (
                "Casual B2B link-share post: 30-50 words (~40-250 characters). Put the hook in the first line before \"See more\" truncates (~200 chars). 1 emoji max. End with the URL and a light CTA.",
                "Keep under 250 characters total when possible.",
                512),
            "LinkedIn" => (
                "Professional thought-leadership post: 200-300 words. Structure: (1) hook in first 30 words — mobile \"see more\" folds at ~210 chars, (2) context/problem, (3) 1-2 insights from the article, (4) CTA + URL. No emojis or at most one.",
                "Aim for 1,300-1,900 characters. Maximum 3,000 characters.",
                2048),
            _ => ("Professional tone, concise, end with the link.", "Keep concise.", 1024)
        };

        var system = new StringBuilder()
            .AppendLine($"You write {platform} posts for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForSocialPlatform(platform))
            .AppendLine(styleGuidance)
            .AppendLine(lengthGuidance)
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences:")
            .AppendLine(SocialJsonContract)
            .AppendLine("JSON rules: one string value for text. Use \\n for line breaks. Plain URL only — no [text](url) markdown.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Article title: {sourceArticle.Title}")
            .AppendLine($"Article summary: {sourceArticle.MetaDescription}")
            .AppendLine($"Link to include verbatim: {articleUrl}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .ToString();

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.65,
            MaxOutputTokens: maxTokens);
    }

    public ChatCompletionRequest BuildColdOutreachPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        string articleUrl)
    {
        var system = new StringBuilder()
            .AppendLine("You write cold outreach / sales emails for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(ContentLengthTargets.EmailColdOutreachEditorialDefinition)
            .AppendLine($"Body must be {ContentLengthTargets.EmailColdOutreachMinWords}-{ContentLengthTargets.EmailColdOutreachMaxWords} words.")
            .AppendLine("Pitch ONE clear idea. No HTML. No markdown links. Do not invent URLs.")
            .AppendLine("ctaLabel is short button/link text (e.g. \"Read the full guide\"). The destination URL is injected by the app.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences:")
            .AppendLine(ColdOutreachJsonContract)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Article title: {sourceArticle.Title}")
            .AppendLine($"Article summary: {sourceArticle.MetaDescription}")
            .AppendLine($"Pillar URL (for context only — do not put in JSON): {articleUrl}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine(BrandTones.ForEmail())
            .ToString();

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.65,
            MaxOutputTokens: 1024);
    }

    public ChatCompletionRequest BuildSectionImagePromptsPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogDraft sourceBlog,
        string articleUrl,
        string blogUrl,
        IReadOnlyList<ImagePromptSectionTarget> sections)
    {
        var system = new StringBuilder()
            .AppendLine("You write AI image-generation prompts for B2B article figures.")
            .AppendLine("CRITICAL: Return EXACTLY ONE prompt for EACH listed section, in the exact order listed.")
            .AppendLine()
            .AppendLine("VISUAL STYLE:")
            .AppendLine("- Flat vector / infographic, professional fintech or B2B tech aesthetic.")
            .AppendLine($"- Default size: {ImagePromptDefaults.PillarWidth}x{ImagePromptDefaults.PillarHeight}. Style: {ImagePromptDefaults.PillarStylePreset}.")
            .AppendLine("- NO readable text, logos, or watermarks in the image.")
            .AppendLine("- pillar-hero / blog-hero (ALWAYS required): the H1/title hero banner image — represents the entire piece at a glance. Wider establishing-shot composition (not a diagram), evokes the title's theme and stakes. blog-hero warmer/more approachable than pillar-hero.")
            .AppendLine("- Pillar H2 sections: teaching diagram, slightly more technical.")
            .AppendLine("- Blog sections: warmer step-by-step feel, still no readable text.")
            .AppendLine("- People Also Ask: abstract Q&A bubbles/shapes without words.")
            .AppendLine("- Tools sections: generic software tiles/icons — no brand names.")
            .AppendLine()
            .AppendLine("IMAGE SETTINGS (include in JSON for each section):")
            .AppendLine($"- imageModel: \"{ImagePromptDefaults.DefaultImageModel}\"")
            .AppendLine("- stylePreset: Illustration")
            .AppendLine("- alchemy: true, photoReal: false")
            .AppendLine("- notes: one short image-gen tip (negative prompt, no text, etc.)")
            .AppendLine()
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences, no preamble, no trailing text:")
            .AppendLine(ImagePromptSectionsJsonContract)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Pillar title: {sourceArticle.Title}")
            .AppendLine($"Pillar URL: {articleUrl}")
            .AppendLine($"Blog title: {sourceBlog.Title}")
            .AppendLine($"Blog URL: {blogUrl}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine()
            .AppendLine("Sections requiring image prompts (DO NOT SKIP ANY):");

        foreach (var section in sections)
        {
            user.AppendLine($"- sourceType: {section.SourceType}, order: {section.Order}, heading: {section.Heading}");
        }

        user.AppendLine()
            .AppendLine("REQUIRED: All sections above must have a prompt in the JSON response.");

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user.ToString()) },
            Temperature: 0.7,
            MaxOutputTokens: 8192);
    }

    private const string ToolMetadataJsonContract =
        "{\"departmentListExcerpt\": string (1-2 sentences for tools hub cards), \"summary\": string (1-2 sentences, general-purpose blurb used on listings), \"mainSummary\": string (1-2 sentences, main-page summary), \"heroSummary\": string (1-2 sentences, blurb under tool page H1), \"homeSummary\": string (1-2 sentences, home-page feature card copy), \"blogSummary\": string (1-2 sentences, blog-listing teaser), \"toolPageExcerpt\": string (1-2 sentences for newspaper tool content column), \"advertisingSummary\": string (2-4 sentences, longer sponsored promotional copy — not an excerpt), \"metaDescription\": string (max 160 chars, SEO only, distinct from the other eight)}";

    public ChatCompletionRequest BuildToolBodyPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        string toolSlug,
        string? revisionNotes = null)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer for an IT consulting firm.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine($"Editorial standard: {ContentLengthTargets.ToolEditorialDefinition}")
            .AppendLine("Respond with ONLY the sections array for this tool overview page — no markdown fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .AppendLine("This page is published with schema.org SoftwareApplication metadata — expert technical tone, not breaking news.")
            .AppendLine("No introductory paragraphs before the first section.")
            .AppendLine("Required top-level (h2) sections, in order: Overview, Key Capabilities, Implementation Considerations, When to Use.")
            .AppendLine($"Target at least {ContentLengthTargets.ToolMinWords:N0} words (aim for {ContentLengthTargets.ToolTargetMinWords:N0}-{ContentLengthTargets.ToolTargetMaxWords:N0}). Hard maximum {ContentLengthTargets.ToolHardMaxWords:N0}. Do not stop early.")
            .AppendLine("Per-section budgets (approximate — hit the page floor by covering each thoroughly, not by padding):")
            .AppendLine("  - Overview: ~350-450 words")
            .AppendLine("  - Key Capabilities: ~400-550 words")
            .AppendLine("  - Implementation Considerations: ~450-600 words")
            .AppendLine("  - When to Use: ~300-400 words")
            .AppendLine($"Only describe real, verifiable capabilities of {app.Name} — never invent a feature, integration, or claim to fill space.")
            .AppendLine($"Implementation Considerations must not be generic industry advice — cover, made concrete to {app.Name} specifically:")
            .AppendLine($"  1. Accelerated deployment — what shortens go-live for {app.Name} (pre-built connectors, templated setup, phased rollout).")
            .AppendLine($"  2. Data model design — what {app.Name}-specific data structure/mapping decisions matter upfront.")
            .AppendLine($"  3. Workflow/process configuration — what {app.Name}-specific approval chains, routing, or automation logic get configured.")
            .AppendLine($"  4. Custom code/development — {app.Name}'s own extension mechanism if it has one (API, scripting, SDK); if it's config-only, say so rather than inventing one.")
            .AppendLine($"Frame these as {context.PublisherName} ({context.ImplementerPositioning}) closing the gap for a client — consultative, not a sales pitch.")
            .AppendLine("There is no real case-study data available — never present a named client, company, or engagement as if it were real. " +
                "A quantified outcome is fine for narrative punch only if explicitly labeled hypothetical/illustrative — avoid recycling a stock 40% line.")
            .ToString();

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, toolSlug: toolSlug);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ToolBody, $"Write the tool overview page for {app.Name}."))
            .AppendLine()
            .AppendLine($"Target keyword context: {context.TargetKeyword}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine($"Tool summary from pillar: {app.Description ?? "N/A"}")
            .AppendLine($"Public path: /tools/{toolSlug}")
            .AppendLine("Write expert third-person technical prose focused on this single platform.")
            .ToString();

        return WithSectionsArraySchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.5,
            MaxOutputTokens: 8192));
    }

    public ChatCompletionRequest BuildToolMetadataPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        ContentDocument body)
    {
        var system = new StringBuilder()
            .AppendLine("You write presentation metadata for a B2B tool overview page (schema.org SoftwareApplication).")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences:")
            .AppendLine(ToolMetadataJsonContract)
            .AppendLine("departmentListExcerpt, summary, mainSummary, heroSummary, homeSummary, blogSummary, toolPageExcerpt, advertisingSummary, and metaDescription must each use different wording — no field may restate another's sentence structure or lede.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine()
            .AppendLine("Tool page body (for context):")
            .AppendLine(TruncateExcerpt(ContentDocumentText.Flatten(body), 2000))
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.55,
            MaxOutputTokens: 1024);
    }

    private const string SummaryVariantsJsonContract =
        "{\"summary\": string (1-2 sentences, general-purpose blurb used on listings), \"mainSummary\": string (1-2 sentences, main-page summary), \"heroSummary\": string (1-2 sentences, blurb under the page H1), \"homeSummary\": string (1-2 sentences, home-page feature card copy), \"blogSummary\": string (1-2 sentences, blog-listing teaser), \"advertisingSummary\": string (2-4 sentences, longer sponsored promotional copy — not an excerpt)}";

    public ChatCompletionRequest BuildSummaryVariantsPrompt(
        ProjectGenerationContext context,
        string title,
        ContentDocument body,
        string? metaDescription,
        string contentTypeLabel)
    {
        var system = new StringBuilder()
            .AppendLine($"You write presentation summary copy for a {contentTypeLabel} page.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences:")
            .AppendLine(SummaryVariantsJsonContract)
            .AppendLine("summary, mainSummary, heroSummary, homeSummary, blogSummary, and advertisingSummary must each use different wording from each other and from the meta description provided below — no field may restate another's sentence structure or lede.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Title: {title}")
            .AppendLine($"Meta description (do not repeat this wording): {metaDescription ?? "N/A"}")
            .AppendLine()
            .AppendLine("Page body (for context):")
            .AppendLine(TruncateExcerpt(ContentDocumentText.Flatten(body), 2000))
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.55,
            MaxOutputTokens: 1024);
    }

    private static readonly Regex SectionTagRegex = new(
        @"\[Section:\s*""(?<heading>[^""]+)""\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Lede-scoped notes: tagged with the current lede heading, or a section title that is not in
    /// the body outline (lede-only titles). Meta/title and FAQ notes are excluded — those have
    /// their own revise paths.
    /// </summary>
    internal static string? ScopeRevisionNotesForLede(
        string? revisionNotes,
        string? existingLedeHeading,
        IReadOnlyList<string> sectionOutline)
    {
        if (string.IsNullOrWhiteSpace(revisionNotes))
        {
            return null;
        }

        if (!revisionNotes.Contains("[Section:", StringComparison.Ordinal))
        {
            return revisionNotes;
        }

        var ownedElsewhere = new HashSet<string>(sectionOutline, StringComparer.OrdinalIgnoreCase)
        {
            "People Also Ask",
            "Meta description",
            "Meta Description",
            "Title",
        };

        var kept = new List<string>();
        foreach (var item in SplitNumberedNoteItems(revisionNotes))
        {
            var match = SectionTagRegex.Match(item);
            if (!match.Success)
            {
                kept.Add(item);
                continue;
            }

            var heading = match.Groups["heading"].Value.Trim();
            var isCurrentLede = !string.IsNullOrWhiteSpace(existingLedeHeading)
                && heading.Equals(existingLedeHeading, StringComparison.OrdinalIgnoreCase);
            var isLedeOnlyTitle = !ownedElsewhere.Contains(heading);

            if (isCurrentLede || isLedeOnlyTitle)
            {
                kept.Add(item);
            }
        }

        return kept.Count == 0 ? null : string.Join('\n', kept);
    }

    /// <summary>Notes whose [Section: ...] heading targets meta description or title hygiene.</summary>
    internal static string? ScopeRevisionNotesForMeta(string? revisionNotes)
    {
        if (string.IsNullOrWhiteSpace(revisionNotes))
        {
            return null;
        }

        if (!revisionNotes.Contains("[Section:", StringComparison.Ordinal))
        {
            // Unstructured feedback: only treat as meta work when it clearly mentions meta/title hygiene.
            return MentionsMetaOrTitleHygiene(revisionNotes) ? revisionNotes : null;
        }

        var kept = new List<string>();
        foreach (var item in SplitNumberedNoteItems(revisionNotes))
        {
            var match = SectionTagRegex.Match(item);
            if (!match.Success)
            {
                continue;
            }

            var heading = match.Groups["heading"].Value.Trim();
            if (heading.Equals("Meta description", StringComparison.OrdinalIgnoreCase)
                || heading.Equals("Meta Description", StringComparison.OrdinalIgnoreCase)
                || heading.Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(item);
            }
        }

        return kept.Count == 0 ? null : string.Join('\n', kept);
    }

    private static bool MentionsMetaOrTitleHygiene(string notes) =>
        notes.Contains("meta description", StringComparison.OrdinalIgnoreCase)
        || notes.Contains("metaDescription", StringComparison.OrdinalIgnoreCase)
        || (notes.Contains("title", StringComparison.OrdinalIgnoreCase)
            && (notes.Contains("140", StringComparison.Ordinal)
                || notes.Contains("160", StringComparison.Ordinal)
                || notes.Contains("cutting-edge", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Splits reviewer notes into numbered items (lines starting with "N. ") preserving multi-line items.</summary>
    private static IEnumerable<string> SplitNumberedNoteItems(string notes)
    {
        var lines = notes.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^\s*\d+\.\s+") && current.Length > 0)
            {
                yield return current.ToString().TrimEnd();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.AppendLine();
            }

            current.Append(line);
        }

        if (current.Length > 0)
        {
            yield return current.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Renders the "REVISION REQUIRED" system-prompt addendum from a reviewer's revise notes, or
    /// null if there's nothing to append. When <paramref name="toolSlug"/> is supplied (tool-batch
    /// regeneration), scopes the notes down to that tool's "Tool: {slug}" block first — notes
    /// tagged for a different tool must never leak into this tool's regeneration prompt. When
    /// <paramref name="sectionHeading"/> is supplied (per-section builders), adds a self-filter
    /// instruction so a section not referenced by any note is left untouched, but only when the
    /// (possibly tool-scoped) notes actually contain "[Section:" markers to match against —
    /// otherwise every generation call would silently discard unstructured feedback.
    /// </summary>
    private static string? BuildRevisionNotesBlock(string? revisionNotes, string? sectionHeading = null, string? toolSlug = null)
    {
        if (string.IsNullOrWhiteSpace(revisionNotes))
        {
            return null;
        }

        var scoped = revisionNotes;
        if (toolSlug is not null)
        {
            var toolBlock = ExtractToolNotesBlock(revisionNotes, toolSlug);
            // "Tool:" tags present but none match this slug -> nothing applies to this tool.
            // No "Tool:" tags at all (e.g. single-tool regeneration test) -> nothing to scope
            // down from, so the whole text is in scope.
            scoped = toolBlock ?? (revisionNotes.Contains("Tool:", StringComparison.Ordinal) ? null! : revisionNotes);
            if (scoped is null)
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(scoped))
        {
            return null;
        }

        var hasSectionTags = scoped.Contains("[Section:", StringComparison.Ordinal);
        if (!hasSectionTags)
        {
            // Format guard: the reviewer didn't produce the structured [Section: "..."] format —
            // fall back to a generic, unfiltered instruction rather than injecting raw prose as if
            // it were structured, and apply no section/tool self-filter since there's no tag to
            // match against.
            return $"REVISION REQUIRED — address the reviewer's feedback: {scoped.Trim()}";
        }

        var sb = new StringBuilder();
        sb.AppendLine("REVISION REQUIRED — address each of the following before returning your section. Only rewrite the");
        sb.AppendLine("section(s) referenced below; leave everything else in your usual writing process unaffected.");
        sb.AppendLine();
        sb.AppendLine(scoped.Trim());

        if (sectionHeading is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"If none of the notes above reference this section (\"{sectionHeading}\"), ignore them and write normally.");
        }

        return sb.ToString();
    }

    /// <summary>Extracts the slice of tagged revision notes (see ReviewLoopService) belonging to one tool, by "Tool: {slug}" marker.</summary>
    private static string? ExtractToolNotesBlock(string taggedNotes, string toolSlug)
    {
        var lines = taggedNotes.Split('\n');
        var marker = $"Tool: {toolSlug}";
        var start = Array.FindIndex(lines, l => l.Trim().Equals(marker, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            return null;
        }

        var end = start + 1;
        while (end < lines.Length && !lines[end].TrimStart().StartsWith("Tool: ", StringComparison.OrdinalIgnoreCase))
        {
            end++;
        }

        return string.Join('\n', lines[(start + 1)..end]).Trim();
    }

    private static string TruncateExcerpt(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = Regex.Replace(text, @"\s+", " ").Trim();
        return normalized.Length <= maxChars ? normalized : normalized[..maxChars].TrimEnd() + "…";
    }

    private static string BuildToolsPlatformChildGuidance(ProjectGenerationContext context, string platformName)
    {
        return new StringBuilder()
            .AppendLine("PLATFORM SUBSECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine($"Platform: {platformName}")
            .AppendLine("In the h4 child's paragraphs, cover all four of these mechanisms, each made concrete to THIS specific platform (not generic filler):")
            .AppendLine($"  1. Accelerated deployment — what specifically shortens go-live for {platformName}.")
            .AppendLine($"  2. Data model design — what {platformName}-specific data structure/mapping decisions an implementer gets right upfront.")
            .AppendLine($"  3. Workflow/process configuration — what {platformName}-specific approval chains, routing rules, or automation logic get configured.")
            .AppendLine($"  4. Custom code/development — what {platformName}-specific extension mechanism exists if the platform supports one; if it genuinely has no such layer, say so plainly instead of inventing one.")
            .AppendLine("Keep each of the 4 points to ONE tight sentence — flowing prose in a single text paragraph, not a numbered list.")
            .AppendLine("Tie these to outcomes: reduced time-to-value, fewer failed pilots, production-ready automation.")
            .AppendLine($"Write from the perspective of {context.PublisherName} as the implementer where natural — without hard-selling.")
            .AppendLine("This h3 child should describe a real software product suitable for schema.org SoftwareApplication JSON+LD.")
            .ToString();
    }

    private static string BuildIntroductionSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("INTRODUCTION / OVERVIEW SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Frame this section around the problems the approach solves for practitioners — not a textbook definition of the technology.")
            .AppendLine("Open with the costs of the status-quo process (errors, delays, manual effort, compliance risk), then explain what intelligent / AI-assisted ")
            .AppendLine($"compliance or automation changes. Tie the framing to what {context.PublisherName} helps clients address, without hard-selling.")
            .AppendLine("Ban openings that define \"what AI is\" or tour features before naming a concrete business pain.")
            .ToString();
    }

    private static string BuildImplementationSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("IMPLEMENTATION SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Lead with the pain of DIY rollout, failed pilots, opaque vendor setup, or stalled go-lives — not a generic numbered \"steps to implement AI\" list.")
            .AppendLine($"Emphasize the concrete work {context.PublisherName} does for clients on this topic, covering at least:")
            .AppendLine("  - Data model design — what must be mapped correctly upfront for this use case")
            .AppendLine("  - Workflow / process configuration — approval chains, routing, automation logic")
            .AppendLine("  - Integration and change management — connecting systems and getting teams to adopt the new process")
            .AppendLine("Make each point specific to the target keyword / article topic — not interchangeable implementer filler.")
            .ToString();
    }

    private static string BuildBenefitsSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("BENEFITS SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("This section fails if it reads as polished marketing claims (\"streamlined\", \"enhanced efficiency\", \"competitive edge\") without ")
            .AppendLine("naming what changes in day-to-day work. For EACH benefit, state a concrete before→after operational change: who does what differently, ")
            .AppendLine("what error/delay/handoff disappears, or what decision becomes possible that was not before.")
            .AppendLine($"Tie at least half of the benefits to what {context.PublisherName} actually configures or designs (data model, workflow, integration, ")
            .AppendLine("change management) — not to abstract \"AI capabilities\".")
            .AppendLine("Use at most ONE labeled hypothetical in the whole section. Make it operationally specific and unique to this section — ")
            .AppendLine("never recycle a stock \"40% reduction for a mid-sized retailer/manufacturer\" line used elsewhere in the article.")
            .AppendLine("List bullets must be operational outcomes (e.g. \"exemption certificates validated before filing, not after notice\") — not slogan phrases.")
            .AppendLine("Ban filler: cutting-edge, paradigm shift, transformative potential, seamless transition, maximize ROI, unlock value.")
            .ToString();
    }

    private static string BuildConcretenessRevisionAmplifier() =>
        """
        CONCRETENESS REVISION (mandatory for this pass):
        The reviewer flagged generic claims. Do NOT rephrase the same claims with nicer adjectives.
        Replace each generic benefit with a specific operational example: role or team, task that changes, and the failure mode avoided.
        Prefer distinct before→after details over percentages. If you use one quantified hypothetical, label it clearly and do not reuse a stock 40% line.
        """;

    /// <summary>
    /// True when revision notes that apply to <paramref name="sectionHeading"/> ask for concrete /
    /// specific examples or call out generic claims.
    /// </summary>
    internal static bool NotesAskForConcreteness(string? revisionNotes, string sectionHeading)
    {
        if (string.IsNullOrWhiteSpace(revisionNotes))
        {
            return false;
        }

        var scoped = revisionNotes;
        if (revisionNotes.Contains("[Section:", StringComparison.Ordinal))
        {
            var matching = new List<string>();
            foreach (var item in SplitNumberedNoteItems(revisionNotes))
            {
                var match = SectionTagRegex.Match(item);
                if (match.Success
                    && match.Groups["heading"].Value.Trim().Equals(sectionHeading, StringComparison.OrdinalIgnoreCase))
                {
                    matching.Add(item);
                }
            }

            if (matching.Count == 0)
            {
                return false;
            }

            scoped = string.Join('\n', matching);
        }

        ReadOnlySpan<string> markers =
        [
            "concrete", "specific", "generic", "example", "examples", "specificity", "vague", "measurable"
        ];
        foreach (var marker in markers)
        {
            if (scoped.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildBestPracticesSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("BEST PRACTICES SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Do not write generic industry advice divorced from the publisher. For each practice, tie it explicitly to how ")
            .AppendLine($"{context.PublisherName} solves that problem for clients — name the concrete mechanism: accelerated deployment timelines, ")
            .AppendLine("data model design, workflow/process configuration, integration setup, or change management (training, adoption, rollout).")
            .AppendLine("Example pattern: state the practice, then 1-2 sentences on what goes wrong without it, then how an experienced implementer ")
            .AppendLine("closes that gap (e.g. \"Without a documented data model, teams re-map fields after go-live; an implementer front-loads this ")
            .AppendLine("during discovery so config work doesn't get redone.\").")
            .AppendLine("Any tool or platform named here must be real and verifiable — never invent a feature or product to illustrate a practice.")
            .AppendLine("There is no real case-study data available — never present a named client, company, or engagement as if it were real. " +
                "A quantified outcome is fine for narrative punch only if explicitly labeled hypothetical/illustrative — avoid recycling a stock 40% line.")
            .ToString();
    }

    private static string BuildFutureTrendsSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("FUTURE TRENDS SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Do not end this section as neutral industry commentary. For each trend, add 1-2 sentences on how ")
            .AppendLine($"{context.PublisherName} is positioned to help clients act on it now — e.g. evaluating/piloting the trend, adapting existing ")
            .AppendLine("data models or workflows to it, or guiding change management as teams adopt it. Keep it consultative, not a sales pitch.")
            .AppendLine("Only cite real, verifiable tools, vendors, or capabilities when discussing a trend — never invent one to make the trend concrete.")
            .AppendLine("There is no real case-study data available — never present a named client, company, or engagement as if it were real. " +
                "A quantified outcome is fine for narrative punch only if explicitly labeled hypothetical/illustrative — avoid recycling a stock 40% line.")
            .ToString();
    }
}
