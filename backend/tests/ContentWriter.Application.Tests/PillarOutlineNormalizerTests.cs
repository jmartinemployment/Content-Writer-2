using ContentWriter.Application.Services.PromptBuilders;

namespace ContentWriter.Application.Tests;

public class PillarOutlineNormalizerTests
{
    [Fact]
    public void Sanitize_inserts_missing_required_heading_before_faq_section()
    {
        var (main, _) = PillarOutlineNormalizer.Sanitize(
            sectionOutline: ["Overview", "Top AI Tools for Widgets"],
            paaFromResearch: [],
            targetKeyword: "widgets",
            requiredHeadings: ["Pricing"]);

        Assert.Contains("Pricing", main);
        var pricingIndex = main.IndexOf("Pricing");
        var faqIndex = main.FindIndex(h => h.Equals("People Also Ask", StringComparison.OrdinalIgnoreCase));
        Assert.True(faqIndex >= 0, "Expected a FAQ section to be present.");
        Assert.True(pricingIndex < faqIndex, "Required heading must appear before the FAQ section.");
    }

    [Fact]
    public void Sanitize_keeps_question_shaped_required_heading_as_main_h2_not_faq()
    {
        var (main, faq) = PillarOutlineNormalizer.Sanitize(
            sectionOutline: ["Overview", "How much does it cost?", "Top AI Tools for Widgets"],
            paaFromResearch: [],
            targetKeyword: "widgets",
            requiredHeadings: ["How much does it cost?"]);

        Assert.Contains("How much does it cost?", main);
        Assert.DoesNotContain(faq, q => q.Equals("How much does it cost?", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sanitize_does_not_duplicate_required_heading_already_present()
    {
        var (main, _) = PillarOutlineNormalizer.Sanitize(
            sectionOutline: ["Overview", "Pricing", "Top AI Tools for Widgets"],
            paaFromResearch: [],
            targetKeyword: "widgets",
            requiredHeadings: ["Pricing"]);

        Assert.Single(main, h => h.Equals("Pricing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sanitize_with_no_required_headings_behaves_as_before()
    {
        var (main, faq) = PillarOutlineNormalizer.Sanitize(
            sectionOutline: ["Overview", "What is a widget?", "Top AI Tools for Widgets"],
            paaFromResearch: [],
            targetKeyword: "widgets");

        Assert.DoesNotContain(main, h => h.Equals("What is a widget?", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(faq, q => q.Equals("What is a widget?", StringComparison.OrdinalIgnoreCase));
    }
}
