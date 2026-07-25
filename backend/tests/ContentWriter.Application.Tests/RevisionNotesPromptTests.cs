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
