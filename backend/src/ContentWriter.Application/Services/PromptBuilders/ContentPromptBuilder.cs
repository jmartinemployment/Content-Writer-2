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

    ChatCompletionRequest BuildArticleLedePrompt(ProjectGenerationContext context, ArticleMetadataDraft metadata);

    ChatCompletionRequest BuildArticleSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections,
        IReadOnlyList<string> fullOutline,
        bool isRegeneration);

    ChatCompletionRequest BuildArticleFaqSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions,
        bool isRegeneration);

    ChatCompletionRequest BuildBlogMetadataPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle);

    ChatCompletionRequest BuildBlogBodyPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata);

    ChatCompletionRequest BuildBlogLedePrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata);

    ChatCompletionRequest BuildBlogSectionPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections);
    ChatCompletionRequest BuildBlogDepthExpansionPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogMetadataDraft metadata,
        IReadOnlyList<Section> currentSections,
        int currentWordCount);
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
        string toolSlug);

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

    ChatCompletionRequest BuildToolWordCountExpansionPrompt(
        ProjectGenerationContext context,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        IReadOnlyList<Section> currentSections,
        int currentWordCount);

    ChatCompletionRequest BuildToolWordCountTrimPrompt(
        ProjectGenerationContext context,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        IReadOnlyList<Section> currentSections,
        int currentWordCount);
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
        "\"paragraphs\": [" + ParagraphJsonShape + ", ...]}";

    private static ChatCompletionRequest WithSectionSchema(ChatCompletionRequest request) =>
        ContentSectionJsonSchema.SectionSchema is { } schema
            ? request with { JsonSchemaName = "section", JsonSchema = schema }
            : request;

    private static readonly JsonSerializerOptions ExpansionSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ParagraphJsonConverter() },
    };

    private static string SerializeForExpansion(Section section) =>
        JsonSerializer.Serialize(section, ExpansionSerializerOptions);

    private static string SerializeSectionsForExpansion(IReadOnlyList<Section> sections) =>
        JsonSerializer.Serialize(sections, ExpansionSerializerOptions);

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
        "{\"title\": string, \"metaDescription\": string (max 160 chars), \"keywords\": string[] (5-10 items), \"sectionOutline\": string[] (5-7 declarative H2 headings — exactly ONE tools section with a descriptive name like \"Top AI Tools for {topic}\" (never a bare \"Tools/Platforms\" label), plus final item: \"People Also Ask\")}";

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
            .AppendLine($"Detected brand tone: {context.DetectedTone}.")
            .AppendLine($"Detected site focus/topics: {context.DetectedFocus}.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences, no commentary.")
            .AppendLine(ArticleMetadataJsonContract)
            .AppendLine("GOOD sectionOutline example: [\"Overview of Enterprise AI\", \"Implementation Framework\", \"Top AI Platforms and Tools\", \"Measuring ROI\", \"People Also Ask\"]")
            .AppendLine("BAD sectionOutline example: [\"What is AI?\", \"How does it work?\"] — never use questions as main H2s.")
            .ToString();

        var user = ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleMetadata,
            $"Plan a comprehensive pillar TechnicalArticle targeting the keyword \"{context.TargetKeyword}\" for {context.PublisherName}. " +
            "Derive sectionOutline from keyword SERP and local pack headings (declarative topics like \"Benefits of X\", not questions). " +
            "REQUIRED: include exactly one tools H2 with a descriptive name (e.g. \"Top AI Tools for Sales Prospecting\") — platforms plus which problems an AI implementer solves. Never use a bare \"Tools/Platforms\" heading. " +
            "Title must NOT be a question and must NOT start with \"How\" — use a definitive statement (e.g. \"AI Prospecting and Lead Intelligence: Implementation Guide\"). " +
            "Meta description: concise factual summary for B2B readers. " +
            "End sectionOutline with exactly one FAQ section titled \"People Also Ask\" — PAA questions are answered there in the body step, not as main H2s. " +
            "Return title, metaDescription, keywords, and sectionOutline only (body is written separately).");

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.5,
            MaxOutputTokens: 1536);
    }

    public ChatCompletionRequest BuildArticleLedePrompt(ProjectGenerationContext context, ArticleMetadataDraft metadata)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine("Write the opening lede for a schema.org TechnicalArticle pillar — third person, expert, consultative, like a senior consultant advising a prospective client.")
            .AppendLine("Prefer a creative (hook/narrative) opening; use a summary (direct thesis-first) opening only if a creative angle genuinely doesn't fit this topic.")
            .AppendLine("The heading is a real written headline for the opening — never the literal words \"Creative Lead\"/\"Summary Lede\"; that label goes only in ledeType.")
            .AppendLine("Do NOT start with \"How\" or a question. 2-3 paragraphs: context and thesis for the article.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences, no commentary:")
            .AppendLine(LedeJsonContract)
            .ToString();

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

    public ChatCompletionRequest BuildArticleSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections,
        IReadOnlyList<string> fullOutline,
        bool isRegeneration)
    {
        var outlineContext = string.Join("\n", fullOutline.Select((h, i) => $"{i + 1}. {h}"));
        var isTools = PillarSectionClassifier.IsToolsSection(sectionHeading);
        var isBestPractices = PillarSectionClassifier.IsBestPracticesSection(sectionHeading);
        var isFutureTrends = PillarSectionClassifier.IsFutureTrendsSection(sectionHeading);

        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine("Write ONE section of a schema.org TechnicalArticle pillar — third person, expert, consultative, like a senior consultant advising a prospective client.")
            .AppendLine($"Pillar standard ({ContentLengthTargets.PillarRangeLabel} words): {ContentLengthTargets.PillarEditorialDefinition}")
            .AppendLine("Respond with ONLY a single valid JSON Section object for this section — no markdown fences, no commentary, no other sections.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's own tag is \"h2\". Do NOT write introductory paragraphs before it — the opening lede is generated separately.")
            .AppendLine("Include 2-3 h3 subsections nested in \"children\" with multiple text paragraphs, and at least one list paragraph where appropriate.")
            .AppendLine("Do not write this as a neutral textbook explainer of the general subject — every subsection should be framed through what an AI implementation " +
                $"consultancy like {context.PublisherName} ({context.ImplementerPositioning}) actually does about the problem being discussed, not just background education on it. " +
                "A reader should finish the section understanding a consultancy's specific angle on it, not just the general concept.")
            .AppendLine("If a hypothetical scenario is used, keep it to 1-2 sentences woven naturally into the surrounding paragraph — not a bolt-on closing paragraph that repeats what was already said.")
            .AppendLine("CRITICAL: there is no real case-study data available, so never present a named client, company, or engagement as if it were real. ")
            .AppendLine("A hypothetical scenario may still use a concrete quantified outcome for punch (e.g. \"a 40% drop in processing time\"), ")
            .AppendLine("but it MUST be explicitly labeled hypothetical/illustrative — e.g. \"a hypothetical mid-sized manufacturer\" or ")
            .AppendLine("\"in a representative scenario\". Never phrase it as something that already happened to a real client.")
            .AppendLine($"Target {ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words for this section. Do not write other sections.")
            .ToString();

        if (isTools)
        {
            system += Environment.NewLine + BuildToolsSectionGuidance(context);
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
            MaxOutputTokens: isTools ? 4096 : 2048));
    }

    public ChatCompletionRequest BuildArticleFaqSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions,
        bool isRegeneration)
    {
        var paaBlock = string.Join("\n", faqQuestions.Select((q, i) => $"  - Q{i + 1}: {q}"));

        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
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
            .AppendLine($"Detected brand tone: {context.DetectedTone}.")
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
            .AppendLine($"Detected brand tone: {context.DetectedTone}.")
            .AppendLine("Write the opening lede for a schema.org BlogPosting deep-dive — conversational but substantive; first/second person allowed.")
            .AppendLine("Prefer a creative (hook/narrative) opening; use a summary (direct thesis-first) opening only if a creative angle genuinely doesn't fit this topic.")
            .AppendLine("2-3 paragraphs: hook, stakes, and who this is for.")
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
        int totalSections)
    {
        var outlineContext = string.Join("\n", metadata.SectionOutline.Select((h, i) => $"{i + 1}. {h}"));
        var isLast = sectionIndex == totalSections - 1;

        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine($"Detected brand tone: {context.DetectedTone}.")
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
                      $"End with a paragraph CTA linking readers to the full technical pillar for implementation depth (use placeholder anchor text only — no href url). Minimum total blog length across all sections is {ContentLengthTargets.BlogMinWords:N0} words.";
        }

        var user = new StringBuilder()
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

    public ChatCompletionRequest BuildBlogDepthExpansionPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogMetadataDraft metadata,
        IReadOnlyList<Section> currentSections,
        int currentWordCount)
    {
        var wordsNeeded = ContentLengthTargets.BlogMinWords - currentWordCount;
        var system = new StringBuilder()
            .AppendLine("You are a senior content editor for an IT consulting firm.")
            .AppendLine("Expand the blog sections below to meet the minimum word count without changing the title or removing existing sections.")
            .AppendLine($"Editorial standard: {ContentLengthTargets.BlogEditorialDefinition}")
            .AppendLine("Add depth inside each section: more paragraphs, an extra h3 child subsection, examples, and a list paragraph where appropriate.")
            .AppendLine($"Current length: {currentWordCount:N0} words. Minimum required: {ContentLengthTargets.BlogMinWords:N0}. Add at least {Math.Max(wordsNeeded, 400):N0} words of substantive material.")
            .AppendLine("Respond with ONLY the full expanded sections array — no markdown fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Blog title: {metadata.Title}")
            .AppendLine($"Pillar reference: {sourceArticle.Title}")
            .AppendLine()
            .AppendLine("Current sections (JSON) to expand:")
            .AppendLine(SerializeSectionsForExpansion(currentSections))
            .ToString();

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.65,
            MaxOutputTokens: 8192);
    }

    public ChatCompletionRequest BuildBlogBodyPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata)
    {
        var pillarSections = sourceArticle.SectionOutline.Count > 0
            ? string.Join(", ", sourceArticle.SectionOutline)
            : "see pillar summary";

        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine($"Detected brand tone: {context.DetectedTone}.")
            .AppendLine("Write a deep-dive blog that teases the pillar — do NOT duplicate the pillar structure or reuse its H2 headings verbatim.")
            .AppendLine("Use fresh headings (5-6 top-level sections). Substantive paragraphs with examples; first/second person allowed.")
            .AppendLine($"Target at least {ContentLengthTargets.BlogMinWords:N0} words (aim for {ContentLengthTargets.BlogRangeLabel}). Do not stop early.")
            .AppendLine("Respond with ONLY the sections array — no markdown fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .ToString();

        var user = new StringBuilder()
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

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.7,
            MaxOutputTokens: 6144);
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
            .AppendLine($"Site tone: {context.DetectedTone}")
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
            .AppendLine("Return ONE prompt per listed item — the user pastes each into their image generator manually.")
            .AppendLine()
            .AppendLine("VISUAL STYLE:")
            .AppendLine("- Flat vector / infographic, professional fintech or B2B tech aesthetic.")
            .AppendLine($"- Default size: {ImagePromptDefaults.PillarWidth}x{ImagePromptDefaults.PillarHeight}. Style: {ImagePromptDefaults.PillarStylePreset}.")
            .AppendLine("- NO readable text, logos, or watermarks in the image.")
            .AppendLine("- pillar-hero / blog-hero (exactly one each): the H1/title hero banner image for that piece — represents it as a whole, not any one section. Wider establishing-shot composition (not a diagram), evokes the title's theme and stakes at a glance. blog-hero should look distinct from pillar-hero even on the same topic — warmer/more approachable, matching the blog's conversational tone vs. the pillar's technical one.")
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
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences:")
            .AppendLine(ImagePromptSectionsJsonContract)
            .AppendLine("Include every section listed below with matching sourceType, heading, and order.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Pillar title: {sourceArticle.Title}")
            .AppendLine($"Pillar URL: {articleUrl}")
            .AppendLine($"Blog title: {sourceBlog.Title}")
            .AppendLine($"Blog URL: {blogUrl}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Site tone: {context.DetectedTone}")
            .AppendLine()
            .AppendLine("Sections requiring image prompts:");

        foreach (var section in sections)
        {
            user.AppendLine($"- sourceType: {section.SourceType}, order: {section.Order}, heading: {section.Heading}");
        }

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
        string toolSlug)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer for an IT consulting firm.")
            .AppendLine($"Editorial standard: {ContentLengthTargets.ToolEditorialDefinition}")
            .AppendLine("Respond with ONLY the sections array for this tool overview page — no markdown fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .AppendLine("This page is published with schema.org SoftwareApplication metadata — expert technical tone, not breaking news.")
            .AppendLine("No introductory paragraphs before the first section.")
            .AppendLine("Required top-level (h2) sections, in order: Overview, Key Capabilities, Implementation Considerations, When to Use.")
            .AppendLine($"Target at least {ContentLengthTargets.ToolMinWords:N0} words (aim for {ContentLengthTargets.ToolTargetMinWords:N0}-{ContentLengthTargets.ToolTargetMaxWords:N0}). Hard maximum {ContentLengthTargets.ToolHardMaxWords:N0}. Do not stop early.")
            .AppendLine($"Only describe real, verifiable capabilities of {app.Name} — never invent a feature, integration, or claim to fill space.")
            .AppendLine($"Implementation Considerations must not be generic industry advice — cover, made concrete to {app.Name} specifically:")
            .AppendLine($"  1. Accelerated deployment — what shortens go-live for {app.Name} (pre-built connectors, templated setup, phased rollout).")
            .AppendLine($"  2. Data model design — what {app.Name}-specific data structure/mapping decisions matter upfront.")
            .AppendLine($"  3. Workflow/process configuration — what {app.Name}-specific approval chains, routing, or automation logic get configured.")
            .AppendLine($"  4. Custom code/development — {app.Name}'s own extension mechanism if it has one (API, scripting, SDK); if it's config-only, say so rather than inventing one.")
            .AppendLine($"Frame these as {context.PublisherName} ({context.ImplementerPositioning}) closing the gap for a client — consultative, not a sales pitch.")
            .AppendLine("There is no real case-study data available — never present a named client, company, or engagement as if it were real. " +
                "A quantified outcome (e.g. \"a 40% reduction\") is fine for narrative punch only if explicitly labeled hypothetical/illustrative.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword context: {context.TargetKeyword}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine($"Tool summary from pillar: {app.Description ?? "N/A"}")
            .AppendLine($"Public path: /tools/{toolSlug}")
            .AppendLine("Write expert third-person technical prose focused on this single platform.")
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.5,
            MaxOutputTokens: 8192);
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

    public ChatCompletionRequest BuildToolWordCountExpansionPrompt(
        ProjectGenerationContext context,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        IReadOnlyList<Section> currentSections,
        int currentWordCount)
    {
        var wordsNeeded = ContentLengthTargets.ToolMinWords - currentWordCount;
        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer. Expand the tool page sections below to meet the minimum word count.")
            .AppendLine("Respond with ONLY the full revised sections array — no markdown fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .AppendLine("Preserve all existing section headings and structure; add substantive depth under each section.")
            .AppendLine($"Minimum required: {ContentLengthTargets.ToolMinWords:N0} words. Hard maximum: {ContentLengthTargets.ToolHardMaxWords:N0} words.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Tool: {app.Name}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Current length: {currentWordCount:N0} words. Add at least {Math.Max(wordsNeeded, 400):N0} words of substantive material.")
            .AppendLine()
            .AppendLine("Current sections (JSON):")
            .AppendLine(SerializeSectionsForExpansion(currentSections))
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.45,
            MaxOutputTokens: 8192);
    }

    public ChatCompletionRequest BuildToolWordCountTrimPrompt(
        ProjectGenerationContext context,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        IReadOnlyList<Section> currentSections,
        int currentWordCount)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer. Trim the tool page sections below to meet the maximum word count.")
            .AppendLine("Respond with ONLY the full revised sections array — no markdown fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .AppendLine("Preserve all existing section headings; tighten prose without losing key facts.")
            .AppendLine($"Target range: {ContentLengthTargets.ToolMinWords:N0}-{ContentLengthTargets.ToolTargetMaxWords:N0} words. Hard maximum: {ContentLengthTargets.ToolHardMaxWords:N0} words.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Tool: {app.Name}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Current length: {currentWordCount:N0} words — trim to at most {ContentLengthTargets.ToolHardMaxWords:N0} words.")
            .AppendLine()
            .AppendLine("Current sections (JSON):")
            .AppendLine(SerializeSectionsForExpansion(currentSections))
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.35,
            MaxOutputTokens: 8192);
    }

    private static string TruncateExcerpt(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = Regex.Replace(text, @"\s+", " ").Trim();
        return normalized.Length <= maxChars ? normalized : normalized[..maxChars].TrimEnd() + "…";
    }

    private static string BuildToolsSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("TOOLS SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Cover 4-6 major platforms or tools relevant to the target keyword — only real, verifiable, well-known products. Never invent a tool name, vendor, feature, or capability; if unsure a feature exists, describe it generically instead of naming it.")
            .AppendLine("For EACH platform, add a child Section (tag h3, heading = platform name) under this Tools section:")
            .AppendLine("  - Its own paragraphs: a brief overview of what the platform does for this use case, then a list paragraph with 2-4 factual capability bullets.")
            .AppendLine("  - One further child Section (tag h4, heading \"How an AI implementer helps with {Platform}\") nested under it.")
            .AppendLine("  REQUIRED in that h4 child's paragraphs: cover all four of these mechanisms, each made concrete to THIS specific platform (not generic filler, not just Salesforce-flavored jargon reused for every platform):")
            .AppendLine("    1. Accelerated deployment — what specifically shortens go-live for {Platform} (e.g. pre-built connectors, templated setup, phased rollout plan).")
            .AppendLine("    2. Data model design — what {Platform}-specific data structure/mapping decisions an implementer gets right upfront (e.g. chart-of-accounts mapping, vendor master data, GL coding schema).")
            .AppendLine("    3. Workflow/process configuration — what {Platform}-specific approval chains, routing rules, or automation logic get configured.")
            .AppendLine("    4. Custom code/development — what {Platform}-specific extension mechanism exists if the platform supports one (its own scripting/API/SDK layer, e.g. custom connectors, API integrations, scripted validation rules); if {Platform} genuinely has no such layer, say so plainly instead of inventing one — don't force this point for a platform that's config-only.")
            .AppendLine("  Keep each of the 4 points to ONE tight sentence — do not write a full paragraph per point or restate the platform overview/capability bullets above. ")
            .AppendLine("  Write it as flowing prose (a single text paragraph) covering all four, not a numbered list — so it doesn't read as a mechanical template repeated per platform.")
            .AppendLine("  Tie these to outcomes: reduced time-to-value, fewer failed pilots, production-ready automation. Vary the language and sentence structure between platforms — do not reuse the same phrasing for each one.")
            .AppendLine($"Write from the perspective of {context.PublisherName} as the implementer where natural — without hard-selling.")
            .AppendLine($"Target {ContentLengthTargets.PillarToolsSectionMinWords}-{ContentLengthTargets.PillarToolsSectionTargetMaxWords} words for this Tools section (longer than other sections).")
            .AppendLine("Each platform h3 child should describe a real software product suitable for schema.org SoftwareApplication JSON+LD.")
            .AppendLine("There is no real case-study data available — never present a named client, company, or engagement as if it were real. " +
                "A quantified outcome (e.g. \"a 40% reduction\") is fine for narrative punch only if explicitly labeled hypothetical/illustrative.")
            .ToString();
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
                "A quantified outcome (e.g. \"a 40% reduction\") is fine for narrative punch only if explicitly labeled hypothetical/illustrative.")
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
                "A quantified outcome (e.g. \"a 40% reduction\") is fine for narrative punch only if explicitly labeled hypothetical/illustrative.")
            .ToString();
    }
}
