using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Application.Tests;

public class BrandTonesTests
{
    private static ProjectGenerationContext MakeContext() => new(
        ProjectName: "Project",
        ProjectUrl: "https://example.com",
        TargetKeyword: "invoice automation",
        Department: "accounting",
        SiteName: "Example",
        DetectedTone: "Conversational, approachable",
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

    private static string UserPrompt(ChatCompletionRequest request) =>
        request.Messages.First(m => m.Role == ChatRole.User).Content;

    [Fact]
    public void ForWebpages_includes_shared_base_and_reassuring_channel()
    {
        var prompt = BrandTones.ForWebpages();

        Assert.Contains("Clear and simple", prompt);
        Assert.Contains("do not hype AI like a magic trick", prompt);
        Assert.Contains("Webpages — Clear and Reassuring", prompt);
        Assert.Contains("bullet points", prompt);
    }

    [Fact]
    public void ForEmail_includes_personal_and_direct_channel()
    {
        var prompt = BrandTones.ForEmail();

        Assert.Contains("Clear and simple", prompt);
        Assert.Contains("Email — Personal and Direct", prompt);
        Assert.Contains("one specific problem", prompt);
    }

    [Theory]
    [InlineData("LinkedIn", "LinkedIn — Expert and Collaborative")]
    [InlineData("Facebook", "Facebook — Local and Approachable")]
    public void ForSocialPlatform_selects_matching_channel(string platform, string expectedLabel)
    {
        Assert.Contains(expectedLabel, BrandTones.ForSocialPlatform(platform));
    }

    [Fact]
    public void BuildArticleMetadataPrompt_embeds_webpages_brand_voice()
    {
        var system = SystemPrompt(new ContentPromptBuilder().BuildArticleMetadataPrompt(MakeContext()));

        Assert.Contains("Webpages — Clear and Reassuring", system);
        Assert.Contains("Results-focused", system);
        Assert.DoesNotContain("Email — Personal and Direct", system);
    }

    private static ArticleDraft MakeArticle()
    {
        var lede = new ContentWriter.Domain.Entities.Section("h2", "Lede", [], null, []);
        var body = new ContentWriter.Domain.Entities.ContentDocument(lede, []);
        return new ArticleDraft("Title", "Meta", body, [], 100, []);
    }

    [Fact]
    public void BuildColdOutreachPrompt_embeds_email_brand_voice()
    {
        var user = UserPrompt(new ContentPromptBuilder().BuildColdOutreachPrompt(
            MakeContext(), MakeArticle(), "https://example.com/a"));

        Assert.Contains("Email — Personal and Direct", user);
        Assert.DoesNotContain("Webpages — Clear and Reassuring", user);
    }

    [Fact]
    public void BuildSocialPrompt_linkedin_embeds_linkedin_brand_voice()
    {
        var system = SystemPrompt(new ContentPromptBuilder().BuildSocialPrompt(
            MakeContext(), MakeArticle(), "LinkedIn", "https://example.com/a"));

        Assert.Contains("LinkedIn — Expert and Collaborative", system);
        Assert.DoesNotContain("Facebook — Local and Approachable", system);
    }

    [Fact]
    public void BuildSocialPrompt_facebook_embeds_facebook_brand_voice()
    {
        var system = SystemPrompt(new ContentPromptBuilder().BuildSocialPrompt(
            MakeContext(), MakeArticle(), "Facebook", "https://example.com/a"));

        Assert.Contains("Facebook — Local and Approachable", system);
        Assert.DoesNotContain("LinkedIn — Expert and Collaborative", system);
    }
}
