using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using Xunit;

namespace ContentWriter.Application.Tests;

public class LlmResponseJsonParserColdOutreachTests
{
    [Fact]
    public void ParseColdOutreach_ValidJson_ReturnsDraft()
    {
        var raw =
            """
            {"subject":"Quick idea on AI prospecting","bodyText":"Hi Alex — noticed your team is scaling outbound across multiple regions this quarter. Most B2B teams lose hours stitching buying signals from scattered tools and spreadsheets. We published a practical pillar on AI for prospecting and lead intelligence that shows a clean stack, a simple scoring model, and a first pilot you can run in two weeks. Worth a skim when you have two minutes before your next pipeline review.","ctaLabel":"Read the pillar"}
            """;

        var draft = LlmResponseJsonParser.ParseColdOutreach(raw, "cold outreach");

        Assert.Equal("Quick idea on AI prospecting", draft.Subject);
        Assert.Equal("Read the pillar", draft.CtaLabel);
        Assert.False(string.IsNullOrWhiteSpace(draft.BodyText));
        var words = draft.BodyText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.InRange(words, 50, 125);
    }

    [Fact]
    public void ParseColdOutreach_MissingSubject_Throws()
    {
        var raw =
            """
            {"subject":"","bodyText":"word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word word","ctaLabel":"Read more"}
            """;

        Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseColdOutreach(raw, "cold outreach"));
    }

    [Fact]
    public void ParseColdOutreach_BodyTooFewWords_Throws()
    {
        var body = string.Join(" ", Enumerable.Repeat("word", 49));
        var raw = $$"""{"subject":"Test subject","bodyText":"{{body}}","ctaLabel":"Read more"}""";

        Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseColdOutreach(raw, "cold outreach"));
    }

    [Fact]
    public void ParseColdOutreach_BodyTooManyWords_Throws()
    {
        var body = string.Join(" ", Enumerable.Repeat("word", 126));
        var raw = $$"""{"subject":"Test subject","bodyText":"{{body}}","ctaLabel":"Read more"}""";

        Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseColdOutreach(raw, "cold outreach"));
    }

    [Fact]
    public void ParseColdOutreach_EmptyCtaLabel_Throws()
    {
        var body = string.Join(" ", Enumerable.Repeat("word", 50));
        var raw = $$"""{"subject":"Test subject","bodyText":"{{body}}","ctaLabel":""}""";

        Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseColdOutreach(raw, "cold outreach"));
    }

    [Fact]
    public void ParseColdOutreach_MarkdownFencedJson_ReturnsDraft()
    {
        var body = string.Join(" ", Enumerable.Repeat("word", 80));
        var json = $$"""{"subject":"Quick idea","bodyText":"{{body}}","ctaLabel":"Read more"}""";
        var raw = $"""
            ```json
            {json}
            ```
            """;

        var draft = LlmResponseJsonParser.ParseColdOutreach(raw, "cold outreach");

        Assert.Equal("Quick idea", draft.Subject);
        Assert.Equal("Read more", draft.CtaLabel);
        var words = draft.BodyText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.InRange(words, 70, 90);
    }
}
