using ContentWriter.Application.Services;
using ContentWriter.Application.Services.PromptBuilders;

namespace ContentWriter.Application.Tests;

public class BrandTonesTests
{
    [Theory]
    [InlineData("webpages", true)]
    [InlineData("email", true)]
    [InlineData("linkedin", true)]
    [InlineData("facebook", true)]
    [InlineData("Conversational, approachable", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_accepts_only_preset_ids(string? toneId, bool expected) =>
        Assert.Equal(expected, BrandTones.IsValid(toneId));

    [Theory]
    [InlineData("Conversational, approachable", BrandTones.Facebook)]
    [InlineData("Technical, authoritative", BrandTones.LinkedIn)]
    [InlineData("Formal, professional", BrandTones.Webpages)]
    [InlineData("webpages", BrandTones.Webpages)]
    [InlineData("unknown", BrandTones.DefaultId)]
    [InlineData(null, BrandTones.DefaultId)]
    public void MapFromDetected_maps_heuristics_and_ids(string? input, string expected) =>
        Assert.Equal(expected, BrandTones.MapFromDetected(input));

    [Fact]
    public void FormatForPrompt_includes_shared_base_and_channel_guidance()
    {
        var prompt = BrandTones.FormatForPrompt(BrandTones.Email);

        Assert.Contains("Email — Personal and Direct", prompt);
        Assert.Contains("Clear and simple", prompt);
        Assert.Contains("one specific problem", prompt);
        Assert.Contains("do not hype AI like a magic trick", prompt);
    }

    [Fact]
    public void FormatForPrompt_webpages_includes_reassuring_channel_mode()
    {
        var prompt = BrandTones.FormatForPrompt(BrandTones.Webpages);
        Assert.Contains("Clear and Reassuring", prompt);
        Assert.Contains("bullet points", prompt);
    }

    [Fact]
    public void BuildArticleMetadataPrompt_embeds_brand_tone_guidance()
    {
        var builder = new ContentPromptBuilder();
        var context = new ContentWriter.Application.DTOs.ProjectGenerationContext(
            ProjectName: "Project",
            ProjectUrl: "https://example.com",
            TargetKeyword: "invoice automation",
            Department: "accounting",
            SiteName: "Example",
            DetectedTone: BrandTones.Webpages,
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
            Provider: ContentWriter.Domain.Enums.LlmProviderType.OpenAi);

        var system = builder.BuildArticleMetadataPrompt(context).Messages
            .First(m => m.Role == ContentWriter.Application.Providers.ChatRole.System).Content;

        Assert.Contains("Brand tone preset: Webpages — Clear and Reassuring", system);
        Assert.Contains("Results-focused", system);
    }
}
