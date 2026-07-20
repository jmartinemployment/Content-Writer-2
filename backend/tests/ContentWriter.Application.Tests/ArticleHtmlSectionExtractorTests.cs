using ContentWriter.Application.Services;

namespace ContentWriter.Application.Tests;

public class ArticleHtmlSectionExtractorTests
{
    [Fact]
    public void ExtractH2Headings_returns_plain_text_headings()
    {
        var markdown = """
            Intro

            ## First **section**

            Body

            ## Tools & vendors
            """;

        var headings = ArticleHtmlSectionExtractor.ExtractH2Headings(markdown);

        Assert.Equal(["First **section**", "Tools & vendors"], headings);
    }

    [Fact]
    public void BuildSectionTargets_numbers_pillar_and_blog_independently()
    {
        var pillar = "## Pillar A\n\nbody\n\n## Pillar B\n\nbody";
        var blog = "## Blog one\n\nbody";

        var targets = ArticleHtmlSectionExtractor.BuildSectionTargets(pillar, blog);

        Assert.Equal(3, targets.Count);
        Assert.Equal(("pillar", "Pillar A", 1), (targets[0].SourceType, targets[0].Heading, targets[0].Order));
        Assert.Equal(("pillar", "Pillar B", 2), (targets[1].SourceType, targets[1].Heading, targets[1].Order));
        Assert.Equal(("blog", "Blog one", 1), (targets[2].SourceType, targets[2].Heading, targets[2].Order));
    }

    [Fact]
    public void Split_splits_on_h2_boundaries_with_leading_content_as_section_zero()
    {
        var markdown = """
            Intro paragraph.

            ## Section One

            Body one.

            ## Section Two

            Body two.
            """;

        var sections = ArticleHtmlSectionExtractor.Split(markdown);

        Assert.Equal(3, sections.Count);
        Assert.Null(sections[0].HeadingText);
        Assert.Equal("Intro paragraph.", sections[0].BodyContent);
        Assert.Equal("Section One", sections[1].HeadingText);
        Assert.Equal("Body one.", sections[1].BodyContent);
        Assert.Equal("Section Two", sections[2].HeadingText);
        Assert.Equal("Body two.", sections[2].BodyContent);
    }
}
