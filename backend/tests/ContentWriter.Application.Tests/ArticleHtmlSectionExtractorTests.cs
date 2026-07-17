using ContentWriter.Application.Services;

namespace ContentWriter.Application.Tests;

public class ArticleHtmlSectionExtractorTests
{
    [Fact]
    public void ExtractH2Headings_returns_plain_text_headings()
    {
        var html = """
            <p>Intro</p>
            <h2>First <strong>section</strong></h2>
            <p>Body</p>
            <h2 id="tools">Tools &amp; vendors</h2>
            """;

        var headings = ArticleHtmlSectionExtractor.ExtractH2Headings(html);

        Assert.Equal(["First  section", "Tools &amp; vendors"], headings);
    }

    [Fact]
    public void BuildSectionTargets_numbers_pillar_and_blog_independently()
    {
        var pillar = "<h2>Pillar A</h2><h2>Pillar B</h2>";
        var blog = "<h2>Blog one</h2>";

        var targets = ArticleHtmlSectionExtractor.BuildSectionTargets(pillar, blog);

        Assert.Equal(3, targets.Count);
        Assert.Equal(("pillar", "Pillar A", 1), (targets[0].SourceType, targets[0].Heading, targets[0].Order));
        Assert.Equal(("pillar", "Pillar B", 2), (targets[1].SourceType, targets[1].Heading, targets[1].Order));
        Assert.Equal(("blog", "Blog one", 1), (targets[2].SourceType, targets[2].Heading, targets[2].Order));
    }
}
