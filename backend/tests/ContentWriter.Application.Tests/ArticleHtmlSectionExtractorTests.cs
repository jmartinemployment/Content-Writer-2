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
    public void SplitTree_nests_h3_through_h6_under_their_parent_heading()
    {
        var markdown = """
            Lead paragraph.

            ## Understanding Predictive Cash Flow Forecasting

            ### The Evolution from Traditional to Predictive Forecasting

            Evolution body.

            #### A Deeper Point

            Deeper body.

            ### Core Components

            Core body.

            ## Benefits
            """;

        var tree = ArticleHtmlSectionExtractor.SplitTree(markdown);

        Assert.Equal(3, tree.Count);
        Assert.Null(tree[0].Heading);
        Assert.Equal("Lead paragraph.", tree[0].Body);

        var h2 = tree[1];
        Assert.Equal(2, h2.Level);
        Assert.Equal("Understanding Predictive Cash Flow Forecasting", h2.Heading);
        Assert.Equal(string.Empty, h2.Body);
        Assert.Equal(2, h2.Children.Count);

        var h3a = h2.Children[0];
        Assert.Equal(3, h3a.Level);
        Assert.Equal("The Evolution from Traditional to Predictive Forecasting", h3a.Heading);
        Assert.Equal("Evolution body.", h3a.Body);
        Assert.Single(h3a.Children);
        Assert.Equal(4, h3a.Children[0].Level);
        Assert.Equal("A Deeper Point", h3a.Children[0].Heading);
        Assert.Equal("Deeper body.", h3a.Children[0].Body);

        var h3b = h2.Children[1];
        Assert.Equal("Core Components", h3b.Heading);
        Assert.Equal("Core body.", h3b.Body);
        Assert.Empty(h3b.Children);

        Assert.Equal("Benefits", tree[2].Heading);
        Assert.Empty(tree[2].Children);
    }
}
