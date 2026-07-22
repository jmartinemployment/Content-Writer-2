using ContentWriter.Application.Services;
using ContentWriter.Application.Services.Export;
using ContentWriter.Domain.Entities;

namespace ContentWriter.Application.Tests;

public class ContentDocumentTests
{
    private static Section MakeSection(string tag, string heading, string? text = null, IReadOnlyList<Section>? children = null) =>
        new(tag, heading, text is null ? [] : [new TextParagraph([new Run(text)])], null, children ?? []);

    [Fact]
    public void BuildSectionTargets_leads_with_pillar_and_blog_hero_then_numbers_sections_independently()
    {
        var pillarLede = MakeSection("h2", "Pillar Lede", "intro");
        var pillar = new ContentDocument(pillarLede, [MakeSection("h2", "Pillar A"), MakeSection("h2", "Pillar B")]);
        var blogLede = MakeSection("h2", "Blog Lede", "intro");
        var blog = new ContentDocument(blogLede, [MakeSection("h2", "Blog one")]);

        var targets = ContentDocumentText.BuildSectionTargets("Pillar Title", pillar, "Blog Title", blog);

        Assert.Equal(5, targets.Count);
        Assert.Equal(("pillar-hero", "Pillar Title", 0), (targets[0].SourceType, targets[0].Heading, targets[0].Order));
        Assert.Equal(("blog-hero", "Blog Title", 0), (targets[1].SourceType, targets[1].Heading, targets[1].Order));
        Assert.Equal(("pillar", "Pillar A", 1), (targets[2].SourceType, targets[2].Heading, targets[2].Order));
        Assert.Equal(("pillar", "Pillar B", 2), (targets[3].SourceType, targets[3].Heading, targets[3].Order));
        Assert.Equal(("blog", "Blog one", 1), (targets[4].SourceType, targets[4].Heading, targets[4].Order));
    }

    [Fact]
    public void CountWords_walks_the_whole_tree_including_nested_children_and_lists()
    {
        var child = MakeSection("h3", "Child heading", "four words right here");
        var section = new Section(
            "h2",
            "Parent heading",
            [
                new TextParagraph([new Run("two words")]),
                new ListParagraph(false, [[new Run("one")], [new Run("two words")]]),
            ],
            null,
            [child]);
        var document = new ContentDocument(MakeSection("h2", "Lede", "lede text here"), [section]);

        // lede(1+3) + parent heading(2) + "two words"(2) + list("one"=1 + "two words"=2) + child heading(2) + "four words right here"(4)
        Assert.Equal(1 + 3 + 2 + 2 + 1 + 2 + 2 + 4, ContentDocumentText.CountWords(document));
    }

    [Fact]
    public void Flatten_never_reintroduces_markup_characters()
    {
        var section = MakeSection("h2", "Heading text", "Body text with an inline run");
        var document = new ContentDocument(MakeSection("h2", "Lede heading", "Lede body"), [section]);

        var flat = ContentDocumentText.Flatten(document);

        Assert.DoesNotContain("<", flat);
        Assert.DoesNotContain("##", flat);
    }

    [Fact]
    public void SectionHtmlRenderer_produces_a_valid_standalone_document_with_balanced_tags()
    {
        var lede = MakeSection("h2", "Opening hook");
        var child = MakeSection("h3", "Sub point", "Detail sentence.");
        var section = new Section(
            "h2",
            "Main Section",
            [new ListParagraph(true, [[new Run("First", Bold: true)], [new Run("Second", Href: "https://example.com")]])],
            null,
            [child]);
        var document = new ContentDocument(lede, [section]);

        var html = SectionHtmlRenderer.RenderDocument(
            "My Title", "My description", "https://example.com/page", "article", null, null,
            new Dictionary<string, string?>(), document);

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("<title>My Title</title>", html);
        Assert.Contains("<h1>My Title</h1>", html);
        Assert.Contains("<h2>Opening hook</h2>", html);
        Assert.Contains("<h2>Main Section</h2>", html);
        Assert.Contains("<h3>Sub point</h3>", html);
        Assert.Contains("<strong>First</strong>", html);
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.Contains("rel=\"canonical\"", html);
        Assert.Contains("property=\"og:title\"", html);
        Assert.Contains("name=\"twitter:card\"", html);
        Assert.Contains("name=\"robots\"", html);
        Assert.Contains("name=\"viewport\"", html);
        Assert.DoesNotContain("##", html);
    }

    [Fact]
    public void SectionHtmlRenderer_html_encodes_run_text_instead_of_injecting_markup()
    {
        var lede = MakeSection("h2", "Lede", "<script>alert(1)</script>");
        var document = new ContentDocument(lede, []);

        var html = SectionHtmlRenderer.RenderDocument(
            "Title", null, null, "website", null, null, new Dictionary<string, string?>(), document);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void SectionHtmlRenderer_embeds_json_ld_verbatim_without_html_encoding()
    {
        var lede = MakeSection("h2", "Lede", "body");
        var document = new ContentDocument(lede, []);
        const string jsonLd = """{"@context":"https://schema.org","@type":"TechnicalArticle","headline":"Title & More"}""";

        var html = SectionHtmlRenderer.RenderDocument(
            "Title", null, null, "article", null, jsonLd, new Dictionary<string, string?>(), document);

        Assert.Contains("<script type=\"application/ld+json\">", html);
        Assert.Contains("\"headline\":\"Title & More\"", html);
    }
}
