using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.SchemaBuilders;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Application.Tests;

public class RevisionNotesPromptTests
{
    private static ProjectGenerationContext MakeContext() => new(
        ProjectName: "Project",
        ProjectUrl: "https://example.com",
        TargetKeyword: "invoice automation",
        Department: "accounting",
        SiteName: "Example",
        DetectedTone: "professional",
        DetectedFocus: "AI implementation",
        CrawledHeadings: [],
        CrawledParagraphs: [],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: "Acme Consulting",
        PublisherLogoUrl: "https://example.com/logo.png",
        AuthorName: "Author",
        ArticleBaseUrl: "https://example.com/articles",
        BlogBaseUrl: "https://example.com/blog",
        ToolBaseUrl: "https://example.com/tools",
        ImplementerPositioning: "hands-on AI implementer",
        Provider: LlmProviderType.OpenAi);

    private static string SystemPrompt(ChatCompletionRequest request) =>
        request.Messages.First(m => m.Role == ChatRole.System).Content;

    [Fact]
    public void BuildToolsPlatformListPrompt_requests_four_to_five_platforms_with_modest_token_budget()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Top AI Tools for Tax"]);

        var request = builder.BuildToolsPlatformListPrompt(
            MakeContext(), metadata, "Top AI Tools for Tax", isRegeneration: false, revisionNotes: null);

        Assert.Equal(512, request.MaxOutputTokens);
        Assert.Contains("4-5 major platforms", SystemPrompt(request));
        Assert.Contains("\"platforms\": string[]", SystemPrompt(request));
    }

    [Fact]
    public void BuildToolsPlatformChildPrompt_uses_2048_budget_and_platform_guidance()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Top AI Tools for Tax"]);
        var platforms = new[] { "Avalara", "Thomson Reuters", "Intuit", "Drake Software" };

        var request = builder.BuildToolsPlatformChildPrompt(
            MakeContext(), metadata, "Top AI Tools for Tax", "Avalara", platforms, 0, platforms.Length,
            isRegeneration: false, revisionNotes: null);

        Assert.Equal(2048, request.MaxOutputTokens);
        var system = SystemPrompt(request);
        Assert.Contains("tag is \"h3\"", system);
        Assert.Contains("How an AI implementer helps with {Platform}", system);
        Assert.Contains("Avalara", system);
    }

    [Fact]
    public void BuildArticleSectionPrompt_no_longer_special_cases_tools_with_8192_budget()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Top AI Tools for Tax"]);

        var request = builder.BuildArticleSectionPrompt(
            MakeContext(), metadata, "Top AI Tools for Tax", 0, 1, ["Top AI Tools for Tax"],
            isRegeneration: false, revisionNotes: null);

        Assert.Equal(2048, request.MaxOutputTokens);
        Assert.DoesNotContain("TOOLS SECTION REQUIREMENTS", SystemPrompt(request));
    }

    [Fact]
    public void BuildArticleLedePrompt_with_notes_targeting_lede_includes_revision_block()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Section A", "People Also Ask"]);
        var notes =
            "1. [Section: \"Why invoice chaos costs more than software\"] Lead with the cost of manual matching.\n" +
            "2. [Section: \"Section A\"] Add a labeled hypothetical outcome.\n" +
            "3. [Section: \"Meta description\"] Shorten to under 160 characters.";

        var request = builder.BuildArticleLedePrompt(
            MakeContext(), metadata, notes, existingLedeHeading: "Why invoice chaos costs more than software");

        var system = SystemPrompt(request);
        Assert.Contains("REVISION REQUIRED", system);
        Assert.Contains("Lead with the cost of manual matching", system);
        Assert.DoesNotContain("labeled hypothetical outcome", system);
        Assert.DoesNotContain("Shorten to under 160", system);
    }

    [Fact]
    public void BuildArticleLedePrompt_includes_notes_whose_heading_is_not_in_section_outline()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Section A"]);
        var notes = "1. [Section: \"A surprising cost most teams ignore\"] Reframe as a hook, not a summary.";

        var request = builder.BuildArticleLedePrompt(MakeContext(), metadata, notes, existingLedeHeading: null);

        Assert.Contains("REVISION REQUIRED", SystemPrompt(request));
        Assert.Contains("Reframe as a hook", SystemPrompt(request));
    }

    [Fact]
    public void BuildArticleMetaRevisionPrompt_returns_request_when_notes_mention_meta()
    {
        var builder = new ContentPromptBuilder();
        var notes = "1. [Section: \"Meta description\"] Shorten to under 160 characters and remove \"cutting-edge.\"";

        var request = builder.BuildArticleMetaRevisionPrompt(
            MakeContext(), "Title", "A cutting-edge overview of invoice automation for mid-market teams that want results now and forevermore beyond limits.", notes);

        Assert.NotNull(request);
        var system = SystemPrompt(request!);
        Assert.Contains("REVISION REQUIRED", system);
        Assert.Contains("Shorten to under 160", system);
        Assert.Contains("140-160 characters", system);
    }

    [Fact]
    public void BuildArticleMetaRevisionPrompt_returns_null_when_notes_are_body_only()
    {
        var builder = new ContentPromptBuilder();
        var notes = "1. [Section: \"Section A\"] Lead with the practitioner's pain point.";

        var request = builder.BuildArticleMetaRevisionPrompt(MakeContext(), "Title", "Meta", notes);

        Assert.Null(request);
    }

    [Fact]
    public void BuildArticleSectionPrompt_benefits_section_includes_operational_guidance()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Benefits of AI-Driven Tax Compliance"]);

        var system = SystemPrompt(builder.BuildArticleSectionPrompt(
            MakeContext(), metadata, "Benefits of AI-Driven Tax Compliance", 0, 1,
            ["Benefits of AI-Driven Tax Compliance"], isRegeneration: false, revisionNotes: null));

        Assert.Contains("BENEFITS SECTION REQUIREMENTS", system);
        Assert.Contains("before→after operational change", system);
        Assert.DoesNotContain("CONCRETENESS REVISION", system);
    }

    [Fact]
    public void BuildArticleSectionPrompt_benefits_with_concreteness_notes_adds_amplifier()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Benefits of AI-Driven Tax Compliance"]);
        var notes =
            "1. [Section: \"Benefits of AI-Driven Tax Compliance\"] Provide more specific, concrete examples " +
            "of how AI-driven tax compliance benefits organizations, rather than relying on generic claims.";

        var system = SystemPrompt(builder.BuildArticleSectionPrompt(
            MakeContext(), metadata, "Benefits of AI-Driven Tax Compliance", 0, 1,
            ["Benefits of AI-Driven Tax Compliance"], isRegeneration: false, revisionNotes: notes));

        Assert.Contains("BENEFITS SECTION REQUIREMENTS", system);
        Assert.Contains("CONCRETENESS REVISION", system);
        Assert.Contains("Do NOT rephrase the same claims with nicer adjectives", system);
    }

    [Fact]
    public void BuildArticleSectionPrompt_concreteness_amplifier_only_when_notes_target_that_section()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Introduction", "Benefits of AI-Driven Tax Compliance"]);
        var notes =
            "1. [Section: \"Benefits of AI-Driven Tax Compliance\"] Provide more specific, concrete examples.\n" +
            "2. [Section: \"Introduction\"] Lead with pain.";

        var intro = SystemPrompt(builder.BuildArticleSectionPrompt(
            MakeContext(), metadata, "Introduction", 0, 2, metadata.SectionOutline,
            isRegeneration: false, revisionNotes: notes));
        var benefits = SystemPrompt(builder.BuildArticleSectionPrompt(
            MakeContext(), metadata, "Benefits of AI-Driven Tax Compliance", 1, 2, metadata.SectionOutline,
            isRegeneration: false, revisionNotes: notes));

        Assert.DoesNotContain("CONCRETENESS REVISION", intro);
        Assert.Contains("CONCRETENESS REVISION", benefits);
    }

    [Fact]
    public void BuildArticleSectionPrompt_with_no_revision_notes_omits_revision_block()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Section A"]);

        var request = builder.BuildArticleSectionPrompt(
            MakeContext(), metadata, "Section A", 0, 1, ["Section A"], isRegeneration: false, revisionNotes: null);

        Assert.DoesNotContain("REVISION REQUIRED", SystemPrompt(request));
    }

    [Fact]
    public void BuildArticleSectionPrompt_with_structured_notes_adds_self_filter_for_its_own_heading()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Section A"]);
        var notes = "1. [Section: \"Section A\"] Lead with the practitioner's pain point.";

        var request = builder.BuildArticleSectionPrompt(
            MakeContext(), metadata, "Section A", 0, 1, ["Section A"], isRegeneration: false, revisionNotes: notes);

        var system = SystemPrompt(request);
        Assert.Contains("REVISION REQUIRED", system);
        Assert.Contains(notes, system);
        Assert.Contains("If none of the notes above reference this section (\"Section A\")", system);
    }

    [Fact]
    public void BuildArticleSectionPrompt_with_unstructured_notes_falls_back_to_generic_instruction_with_no_self_filter()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Section A"]);
        var notes = "The content needs more specificity throughout.";

        var request = builder.BuildArticleSectionPrompt(
            MakeContext(), metadata, "Section A", 0, 1, ["Section A"], isRegeneration: false, revisionNotes: notes);

        var system = SystemPrompt(request);
        Assert.Contains("REVISION REQUIRED — address the reviewer's feedback:", system);
        Assert.Contains(notes, system);
        Assert.DoesNotContain("If none of the notes above reference this section", system);
    }

    [Fact]
    public void BuildToolBodyPrompt_includes_per_section_word_budgets()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Pillar", "Meta", [], []);

        var system = SystemPrompt(builder.BuildToolBodyPrompt(MakeContext(), metadata, MakeApp("HubSpot"), "hubspot"));

        Assert.Contains("Per-section budgets", system);
        Assert.Contains("Overview: ~350-450 words", system);
        Assert.Contains("Key Capabilities: ~400-550 words", system);
        Assert.Contains("Implementation Considerations: ~450-600 words", system);
        Assert.Contains("When to Use: ~300-400 words", system);
    }

    private static SoftwareApplicationDescriptor MakeApp(string name) =>
        new(name, $"{name} description");

    [Fact]
    public void BuildToolBodyPrompt_scopes_combined_notes_to_the_matching_tool_slug_only()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Pillar", "Meta", [], []);
        var combinedNotes =
            "Tool: hubspot\n1. [Section: \"Key Capabilities\"] Tighten the HubSpot capability list.\n" +
            "Tool: salesforce\n1. [Section: \"Key Capabilities\"] Add a Salesforce-specific workflow example.";

        var request = builder.BuildToolBodyPrompt(MakeContext(), metadata, MakeApp("HubSpot"), "hubspot", combinedNotes);

        var system = SystemPrompt(request);
        Assert.Contains("Tighten the HubSpot capability list", system);
        Assert.DoesNotContain("Salesforce-specific workflow example", system);
    }

    [Fact]
    public void BuildToolBodyPrompt_with_notes_for_a_different_tool_only_omits_revision_block_entirely()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Pillar", "Meta", [], []);
        var combinedNotes = "Tool: salesforce\n1. [Section: \"Key Capabilities\"] Add a Salesforce-specific workflow example.";

        var request = builder.BuildToolBodyPrompt(MakeContext(), metadata, MakeApp("HubSpot"), "hubspot", combinedNotes);

        Assert.DoesNotContain("REVISION REQUIRED", SystemPrompt(request));
    }

    private static string UserPrompt(ChatCompletionRequest request) =>
        request.Messages.First(m => m.Role == ChatRole.User).Content;

    private static List<KeywordSourceSummary> MakeAuthoritativeSources(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new KeywordSourceSummary(
                KeywordSourceCategory.Wikipedia,
                $"Source {i}",
                $"source-{i}.html",
                Headings: [$"Heading {i}"],
                Paragraphs: [$"Paragraph {i}"]))
            .ToList();

    [Fact]
    public void BuildToolBodyPrompt_caps_authoritative_sources_to_one_even_when_project_has_several()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Pillar", "Meta", [], []);
        var context = MakeContext() with { KeywordSources = MakeAuthoritativeSources(3) };

        var request = builder.BuildToolBodyPrompt(context, metadata, MakeApp("HubSpot"), "hubspot");

        var user = UserPrompt(request);
        Assert.Contains("Source 1", user);
        Assert.DoesNotContain("Source 2", user);
        Assert.DoesNotContain("Source 3", user);
    }

    [Fact]
    public void BuildArticleSectionPrompt_does_not_cap_authoritative_sources_existing_pillar_behavior_unchanged()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Section A"]);
        var context = MakeContext() with { KeywordSources = MakeAuthoritativeSources(3) };

        var request = builder.BuildArticleSectionPrompt(
            context, metadata, "Section A", 0, 1, ["Section A"], isRegeneration: false);

        var user = UserPrompt(request);
        Assert.Contains("Source 1", user);
        Assert.Contains("Source 2", user);
        Assert.Contains("Source 3", user);
    }
}
