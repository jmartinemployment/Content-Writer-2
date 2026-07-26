using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Application.Tests;

public class FirstDraftReviewCriteriaPromptTests
{
    private static ProjectGenerationContext MakeContext() => new(
        ProjectName: "Project",
        ProjectUrl: "https://example.com",
        TargetKeyword: "intelligent tax compliance",
        Department: "accounting",
        SiteName: "Example",
        DetectedTone: "professional",
        DetectedFocus: "AI implementation",
        CrawledHeadings: [],
        CrawledParagraphs: [],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: "Geek At Your Spot",
        PublisherLogoUrl: "https://example.com/logo.png",
        AuthorName: "Author",
        ArticleBaseUrl: "https://example.com/articles",
        BlogBaseUrl: "https://example.com/blog",
        ToolBaseUrl: "https://example.com/tools",
        ImplementerPositioning: "hands-on AI implementer",
        Provider: LlmProviderType.OpenAi);

    private static string SystemPrompt(ChatCompletionRequest request) =>
        request.Messages.First(m => m.Role == ChatRole.System).Content;

    private static string UserPrompt(ChatCompletionRequest request) =>
        request.Messages.First(m => m.Role == ChatRole.User).Content;

    [Fact]
    public void BuildArticleLedePrompt_requires_pain_before_ai_solution()
    {
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Section A"]);
        var system = SystemPrompt(new ContentPromptBuilder().BuildArticleLedePrompt(MakeContext(), metadata));

        Assert.Contains("PAIN BEFORE SOLUTION", system);
        Assert.Contains("before naming AI", system);
        Assert.Contains("manual / status-quo", system);
    }

    [Fact]
    public void BuildArticleMetadataPrompt_requires_140_to_160_chars_and_target_keyword()
    {
        var request = new ContentPromptBuilder().BuildArticleMetadataPrompt(MakeContext());
        var system = SystemPrompt(request);
        var user = UserPrompt(request);

        Assert.Contains("140-160", system);
        Assert.Contains("140-160", user);
        Assert.Contains("intelligent tax compliance", user);
        Assert.Contains("target keyword naturally", system);
    }

    [Fact]
    public void BuildArticleSectionPrompt_requires_problem_first_opening()
    {
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Measuring ROI"]);
        var system = SystemPrompt(new ContentPromptBuilder().BuildArticleSectionPrompt(
            MakeContext(), metadata, "Measuring ROI", 0, 1, ["Measuring ROI"], isRegeneration: false));

        Assert.Contains("PROBLEM-FIRST OPENING", system);
        Assert.Contains("Do NOT open with \"AI enables…\"", system);
    }

    [Fact]
    public void BuildArticleSectionPrompt_introduction_adds_problem_focus_guidance()
    {
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Introduction to Intelligent Tax Compliance"]);
        var system = SystemPrompt(new ContentPromptBuilder().BuildArticleSectionPrompt(
            MakeContext(), metadata, "Introduction to Intelligent Tax Compliance", 0, 1,
            ["Introduction to Intelligent Tax Compliance"], isRegeneration: false));

        Assert.Contains("INTRODUCTION / OVERVIEW SECTION REQUIREMENTS", system);
        Assert.Contains("problems the approach solves", system);
        Assert.DoesNotContain("IMPLEMENTATION SECTION REQUIREMENTS", system);
    }

    [Fact]
    public void BuildArticleSectionPrompt_implementation_adds_implementer_pain_point_guidance()
    {
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Implementing AI for Tax Efficiency"]);
        var system = SystemPrompt(new ContentPromptBuilder().BuildArticleSectionPrompt(
            MakeContext(), metadata, "Implementing AI for Tax Efficiency", 0, 1,
            ["Implementing AI for Tax Efficiency"], isRegeneration: false));

        Assert.Contains("IMPLEMENTATION SECTION REQUIREMENTS", system);
        Assert.Contains("Data model design", system);
        Assert.Contains("Workflow / process configuration", system);
        Assert.Contains("Geek At Your Spot", system);
    }

    [Fact]
    public void BuildArticleSectionPrompt_benefits_includes_requirements_on_first_draft()
    {
        var metadata = new ArticleMetadataDraft("Title", "Meta", [], ["Benefits of AI in Tax Compliance"]);
        var system = SystemPrompt(new ContentPromptBuilder().BuildArticleSectionPrompt(
            MakeContext(), metadata, "Benefits of AI in Tax Compliance", 0, 1,
            ["Benefits of AI in Tax Compliance"], isRegeneration: false, revisionNotes: null));

        Assert.Contains("BENEFITS SECTION REQUIREMENTS", system);
        Assert.Contains("before→after operational change", system);
        Assert.DoesNotContain("CONCRETENESS REVISION", system);
    }
}
