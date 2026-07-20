using System.Text;
using System.Text.RegularExpressions;
using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;

namespace ContentWriter.Application.Services.PromptBuilders;

public interface IContentPromptBuilder
{
    ChatCompletionRequest BuildArticleMetadataPrompt(ProjectGenerationContext context);
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

    ChatCompletionRequest BuildArticleBodyPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions,
        bool isRegeneration = false);
    ChatCompletionRequest BuildBlogMetadataPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle);
    ChatCompletionRequest BuildBlogSectionPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections);
    ChatCompletionRequest BuildBlogBodyPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata);
    ChatCompletionRequest BuildBlogDepthExpansionPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogMetadataDraft metadata,
        string currentBodyHtml,
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
        string bodyHtml);

    ChatCompletionRequest BuildSummaryVariantsPrompt(
        ProjectGenerationContext context,
        string title,
        string bodyHtml,
        string? metaDescription,
        string contentTypeLabel);

    ChatCompletionRequest BuildToolWordCountExpansionPrompt(
        ProjectGenerationContext context,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        string currentBodyHtml,
        int currentWordCount);

    ChatCompletionRequest BuildToolWordCountTrimPrompt(
        ProjectGenerationContext context,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        string currentBodyHtml,
        int currentWordCount);
}

public class ContentPromptBuilder : IContentPromptBuilder
{
    private const string ArticleMetadataJsonContract =
        "{\"title\": string, \"metaDescription\": string (max 160 chars), \"keywords\": string[] (5-10 items), \"sectionOutline\": string[] (5-7 declarative H2 headings — exactly ONE tools section with a descriptive name like \"Top AI Tools for {topic}\" (never a bare \"Tools/Platforms\" label), plus final item: \"People Also Ask\")}";

    private const string SocialJsonContract =
        "{\"text\": string}";

    private const string ColdOutreachJsonContract =
        "{\"subject\": string, \"bodyText\": string (50-125 words), \"ctaLabel\": string}";

    private const string ImagePromptSectionItemJsonContract =
        "{\"sourceType\": \"pillar|blog\", \"heading\": string (exact H2 text), \"order\": number, \"prompt\": string (40-400 words), \"width\": number, \"height\": number, \"imageModel\": string, \"stylePreset\": string, \"alchemy\": boolean, \"photoReal\": boolean, \"notes\": string|null}";

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
        var isFirst = sectionIndex == 0;
        var isTools = PillarSectionClassifier.IsToolsSection(sectionHeading);

        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine("Write ONE section of a schema.org TechnicalArticle pillar — third person, expert, implementation-focused.")
            .AppendLine($"Pillar standard ({ContentLengthTargets.PillarRangeLabel} words): {ContentLengthTargets.PillarEditorialDefinition}")
            .AppendLine("Output ONLY GitHub-Flavored Markdown for this section. No JSON. No code fences wrapping the output.")
            .AppendLine(isFirst
                ? "Start with 2-3 introductory paragraphs (context and thesis). Do NOT start with \"How\" or a question. Then \"## \" for this section."
                : "Start with \"## \" for this section only — no intro paragraphs.")
            .AppendLine("Include 2-3 \"### \" subsections with multiple paragraphs and at least one \"- \" bullet list where appropriate.")
            .AppendLine($"Target {ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words for this section. Do not write other sections.")
            .ToString();

        if (isTools)
        {
            system += Environment.NewLine + BuildToolsSectionGuidance(context);
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

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: isRegeneration ? 0.72 : 0.65,
            MaxOutputTokens: isTools ? 4096 : 2048);
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
            .AppendLine("Start with \"## People Also Ask\". Each question is a \"### \" heading followed by a 2-4 sentence answer paragraph.")
            .AppendLine("Direct, factual answers. Third person. GitHub-Flavored Markdown only. No JSON.")
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

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: isRegeneration ? 0.7 : 0.6,
            MaxOutputTokens: 3072);
    }

    public ChatCompletionRequest BuildArticleBodyPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions,
        bool isRegeneration = false)
    {
        var outline = metadata.SectionOutline.Count > 0
            ? string.Join("\n", metadata.SectionOutline.Select((h, i) => $"{i + 1}. {h}"))
            : "(no outline provided — use strong declarative H2 structure)";

        var paaBlock = faqQuestions.Count > 0
            ? string.Join("\n", faqQuestions.Select((q, i) => $"  - Q{i + 1}: {q}"))
            : "  (none provided)";

        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine($"Detected brand tone: {context.DetectedTone}.")
            .AppendLine($"Detected site focus/topics: {context.DetectedFocus}.")
            .AppendLine("Content type: schema.org TechnicalArticle — a deep technical pillar, NOT a BlogPosting or FAQ page.")
            .AppendLine($"Editorial standard ({ContentLengthTargets.PillarRangeLabel} words): {ContentLengthTargets.PillarEditorialDefinition}")
            .AppendLine("Write an authoritative pillar article. Tone: third person, expert, implementation-focused.")
            .AppendLine("ANTI-PATTERNS (do NOT do these): first/second person blog voice; short 2-sentence sections; question-mark H2s outside the FAQ section; turning the whole article into Q&A.")
            .AppendLine("REQUIRED STRUCTURE:")
            .AppendLine("  1. Opening: 2-3 paragraphs before the first \"## \" heading (context, problem, thesis).")
            .AppendLine("  2. Main \"## \" sections (from outline, excluding FAQ): each with multiple \"### \" subsections, 3+ paragraphs, and at least one \"- \" bullet list where appropriate.")
            .AppendLine("  3. Final \"## People Also Ask\" only: each FAQ as a \"### \" heading + 2-4 sentence answer paragraph. FAQ must NOT appear earlier in the article.")
            .AppendLine("Ground factual claims in AUTHORITATIVE SOURCES — paraphrase and attribute where appropriate.")
            .AppendLine($"Target at least {ContentLengthTargets.PillarMinWords:N0} words (aim for {ContentLengthTargets.PillarTargetMinWords:N0}-{ContentLengthTargets.PillarTargetMaxWords:N0}). Do not stop early.")
            .AppendLine("Respond with ONLY GitHub-Flavored Markdown. No JSON wrapper. No code fences wrapping the output.")
            .ToString();

        if (isRegeneration)
        {
            system += Environment.NewLine +
                      "This is a REGENERATION: keep the same outline topics but write substantially new prose, examples, and section openings. Do not reuse prior phrasing.";
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleBody,
                $"Write the full pillar article body for {context.PublisherName}. Target keyword: \"{context.TargetKeyword}\"."))
            .AppendLine()
            .AppendLine($"Title: {metadata.Title}")
            .AppendLine($"Meta description: {metadata.MetaDescription}")
            .AppendLine($"Keywords: {string.Join(", ", metadata.Keywords)}")
            .AppendLine()
            .AppendLine("Section outline (mandatory H2 order — declarative headings except the FAQ section):")
            .AppendLine(outline)
            .AppendLine()
            .AppendLine("People Also Ask questions (answer under the final H2 as H3 + paragraph each):")
            .AppendLine(paaBlock)
            .ToString();

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: isRegeneration ? 0.75 : 0.65,
            MaxOutputTokens: 8192);
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

    public ChatCompletionRequest BuildBlogSectionPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections)
    {
        var outlineContext = string.Join("\n", metadata.SectionOutline.Select((h, i) => $"{i + 1}. {h}"));
        var isFirst = sectionIndex == 0;
        var isLast = sectionIndex == totalSections - 1;

        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine($"Detected brand tone: {context.DetectedTone}.")
            .AppendLine("Write ONE section of a schema.org BlogPosting deep-dive article — conversational but substantive; first/second person allowed.")
            .AppendLine($"Editorial standard ({ContentLengthTargets.BlogRangeLabel} words): {ContentLengthTargets.BlogEditorialDefinition}")
            .AppendLine("Output ONLY GitHub-Flavored Markdown for this section. No JSON. No code fences wrapping the output.")
            .AppendLine(isFirst
                ? "Start with 2-3 introductory paragraphs (hook, stakes, and who this is for). Then \"## \" for this section."
                : "Start with \"## \" for this section only — no intro paragraphs.")
            .AppendLine("Include 2-3 \"### \" subsections where helpful, multiple substantive paragraphs, at least one \"- \" bullet list with concrete tips, and a specific example or data point.")
            .AppendLine($"Target {ContentLengthTargets.BlogSectionMinWords}-{ContentLengthTargets.BlogSectionTargetMaxWords} words for this section alone. Shorter sections fail editorial review — add depth, not filler.")
            .AppendLine("Do NOT duplicate the pillar article structure or reuse its H2 headings verbatim.")
            .ToString();

        if (isLast)
        {
            system += Environment.NewLine +
                      $"End with a CTA <p> linking readers to the full technical pillar for implementation depth (use placeholder anchor text only — no href URL). Minimum total blog length across all sections is {ContentLengthTargets.BlogMinWords:N0} words.";
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

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.72,
            MaxOutputTokens: 4096);
    }

    public ChatCompletionRequest BuildBlogDepthExpansionPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogMetadataDraft metadata,
        string currentBodyHtml,
        int currentWordCount)
    {
        var wordsNeeded = ContentLengthTargets.BlogMinWords - currentWordCount;
        var system = new StringBuilder()
            .AppendLine("You are a senior content editor for an IT consulting firm.")
            .AppendLine("Expand the blog Markdown below to meet the minimum word count without changing the title or removing existing sections.")
            .AppendLine($"Editorial standard: {ContentLengthTargets.BlogEditorialDefinition}")
            .AppendLine("Add depth inside each \"## \" section: more paragraphs, an extra \"### \" subsection, examples, and a bullet list where appropriate.")
            .AppendLine($"Current length: {currentWordCount:N0} words. Minimum required: {ContentLengthTargets.BlogMinWords:N0}. Add at least {Math.Max(wordsNeeded, 400):N0} words of substantive material.")
            .AppendLine("Respond with ONLY the full expanded Markdown body. No JSON wrapper. No code fences wrapping the output.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Blog title: {metadata.Title}")
            .AppendLine($"Pillar reference: {sourceArticle.Title}")
            .AppendLine()
            .AppendLine("Current Markdown to expand:")
            .AppendLine(currentBodyHtml)
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
            .AppendLine("Use fresh \"## \" headings (5-6 sections). Substantive paragraphs with examples; first/second person allowed.")
            .AppendLine($"Target at least {ContentLengthTargets.BlogMinWords:N0} words (aim for {ContentLengthTargets.BlogRangeLabel}). Do not stop early.")
            .AppendLine("Respond with ONLY GitHub-Flavored Markdown. No JSON wrapper. No code fences wrapping the output.")
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
            .AppendLine("Write the blog body. Summarize 2-3 key takeaways, add a practical tip or short story, and end with a CTA to read the full technical article for implementation depth.")
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
            .AppendLine("You write AI image-generation prompts for inline B2B article figures.")
            .AppendLine("Return ONE prompt per listed <h2> section — the user pastes each into their image generator manually.")
            .AppendLine()
            .AppendLine("VISUAL STYLE:")
            .AppendLine("- Flat vector / infographic, professional fintech or B2B tech aesthetic.")
            .AppendLine($"- Default size: {ImagePromptDefaults.PillarWidth}x{ImagePromptDefaults.PillarHeight}. Style: {ImagePromptDefaults.PillarStylePreset}.")
            .AppendLine("- NO readable text, logos, or watermarks in the image.")
            .AppendLine("- Pillar sections: teaching diagram, slightly more technical.")
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
            .AppendLine("Write a tool overview page as GitHub-Flavored Markdown only (no JSON wrapper, no code fences wrapping the output).")
            .AppendLine("This page is published with schema.org SoftwareApplication metadata — expert technical tone, not breaking news.")
            .AppendLine("Use \"## \" for main sections and \"### \" for subsections with multiple paragraphs.")
            .AppendLine("Start at the first \"## \" heading — no introductory paragraphs before it.")
            .AppendLine("Required \"## \" sections: Overview, Key Capabilities, Implementation Considerations, When to Use.")
            .AppendLine($"Target at least {ContentLengthTargets.ToolMinWords:N0} words (aim for {ContentLengthTargets.ToolTargetMinWords:N0}-{ContentLengthTargets.ToolTargetMaxWords:N0}). Hard maximum {ContentLengthTargets.ToolHardMaxWords:N0}. Do not stop early.")
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
        string bodyHtml)
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
            .AppendLine(StripHtmlExcerpt(bodyHtml, 2000))
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
        string bodyHtml,
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
            .AppendLine(StripHtmlExcerpt(bodyHtml, 2000))
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.55,
            MaxOutputTokens: 1024);
    }

    public ChatCompletionRequest BuildToolWordCountExpansionPrompt(
        ProjectGenerationContext context,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        string currentBodyHtml,
        int currentWordCount)
    {
        var wordsNeeded = ContentLengthTargets.ToolMinWords - currentWordCount;
        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer. Expand the tool page Markdown to meet the minimum word count.")
            .AppendLine("Return ONLY the full revised Markdown body — no JSON, no code fences wrapping the output.")
            .AppendLine("Preserve all existing \"## \" section headings and structure; add substantive depth under each section.")
            .AppendLine($"Minimum required: {ContentLengthTargets.ToolMinWords:N0} words. Hard maximum: {ContentLengthTargets.ToolHardMaxWords:N0} words.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Tool: {app.Name}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Current length: {currentWordCount:N0} words. Add at least {Math.Max(wordsNeeded, 400):N0} words of substantive material.")
            .AppendLine()
            .AppendLine("Current Markdown:")
            .AppendLine(currentBodyHtml)
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.45,
            MaxOutputTokens: 8192);
    }

    public ChatCompletionRequest BuildToolWordCountTrimPrompt(
        ProjectGenerationContext context,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        string currentBodyHtml,
        int currentWordCount)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer. Trim the tool page Markdown to meet the maximum word count.")
            .AppendLine("Return ONLY the full revised Markdown body — no JSON, no code fences wrapping the output.")
            .AppendLine("Preserve all existing \"## \" section headings; tighten prose without losing key facts.")
            .AppendLine($"Target range: {ContentLengthTargets.ToolMinWords:N0}-{ContentLengthTargets.ToolTargetMaxWords:N0} words. Hard maximum: {ContentLengthTargets.ToolHardMaxWords:N0} words.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Tool: {app.Name}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Current length: {currentWordCount:N0} words — trim to at most {ContentLengthTargets.ToolHardMaxWords:N0} words.")
            .AppendLine()
            .AppendLine("Current Markdown:")
            .AppendLine(currentBodyHtml)
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.35,
            MaxOutputTokens: 8192);
    }

    private static string StripHtmlExcerpt(string html, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= maxChars ? text : text[..maxChars].TrimEnd() + "…";
    }

    private static string BuildToolsSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("TOOLS SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Cover 4-6 major platforms or tools relevant to the target keyword (only well-known products; do not invent feature names).")
            .AppendLine("For EACH platform use this Markdown pattern (heading levels matter — do not deviate):")
            .AppendLine("  ### {Platform name}")
            .AppendLine("  Brief overview of what the platform does for this use case.")
            .AppendLine("  - 2-4 factual capability bullets")
            .AppendLine("  #### How an AI implementer helps with {Platform}")
            .AppendLine("  Yes — an AI implementer can facilitate {Platform} deployments. State which client problems are solved ")
            .AppendLine("  (e.g. faster configuration, data models, integrations, Apex/LWC, Agentforce/agents, governance, training). ")
            .AppendLine("  Tie problems to outcomes: reduced time-to-value, fewer failed pilots, production-ready automation.")
            .AppendLine("Example (Salesforce): AI implementers accelerate Salesforce rollouts via AI-assisted data model design, ")
            .AppendLine("configuration workflows, Apex/LWC development, and autonomous agents such as Agentforce.")
            .AppendLine($"Write from the perspective of {context.PublisherName} as the implementer where natural — without hard-selling.")
            .AppendLine($"Target {ContentLengthTargets.PillarToolsSectionMinWords}-{ContentLengthTargets.PillarToolsSectionTargetMaxWords} words for this Tools section (longer than other sections).")
            .AppendLine("Each platform <h3> should describe a real software product suitable for schema.org SoftwareApplication JSON+LD.")
            .ToString();
    }
}
