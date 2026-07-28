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
    public void FromPlainText_Flatten_returns_body_once_without_truncated_heading_prefix()
    {
        var body =
            "Hi there,\n\nNavigating tax compliance can be complex and time-consuming, but AI-driven solutions are changing the game.";

        var document = ContentDocumentText.FromPlainText(body);
        var flat = ContentDocumentText.Flatten(document);

        Assert.Equal(body, flat);
        Assert.DoesNotContain("AI-d\nHi there", flat.Replace("\r\n", "\n"));
        Assert.Equal(string.Empty, document.Lede.Heading);
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

    [Fact]
    public void SectionHtmlRenderer_escapes_special_characters_in_meta_attribute_values()
    {
        var lede = MakeSection("h2", "Lede", "body");
        var document = new ContentDocument(lede, []);
        const string description = "Tax Compliance & Regulations \"quoted\" <tag> value";

        var html = SectionHtmlRenderer.RenderDocument(
            "Title", description, null, "article", null, null, new Dictionary<string, string?>(), document);

        Assert.Contains("content=\"Tax Compliance &amp; Regulations &quot;quoted&quot; &lt;tag&gt; value\"", html);
        Assert.DoesNotContain("content=\"Tax Compliance & Regulations", html);
    }

    [Fact]
    public void SectionHtmlRenderer_injects_gtm_head_script_and_body_noscript()
    {
        var lede = MakeSection("h2", "Lede", "body");
        var document = new ContentDocument(lede, []);

        var html = SectionHtmlRenderer.RenderDocument(
            "Title", null, null, "article", null, null, new Dictionary<string, string?>(), document,
            gtmContainerId: "GTM-K5CXSQRP");

        Assert.Contains("googletagmanager.com/gtm.js?id=", html);
        Assert.Contains("'GTM-K5CXSQRP'", html);
        Assert.Contains("src=\"https://www.googletagmanager.com/ns.html?id=GTM-K5CXSQRP\"", html);
        Assert.Contains("<noscript>", html);
        // Noscript must be the first meaningful node inside <body>.
        var bodyStart = html.IndexOf("<body>", StringComparison.Ordinal);
        var noscript = html.IndexOf("<noscript>", StringComparison.Ordinal);
        var h1 = html.IndexOf("<h1>", StringComparison.Ordinal);
        Assert.True(bodyStart >= 0 && noscript > bodyStart && noscript < h1);
    }

    [Fact]
    public void SectionHtmlRenderer_ignores_invalid_gtm_container_id()
    {
        var lede = MakeSection("h2", "Lede", "body");
        var document = new ContentDocument(lede, []);

        var html = SectionHtmlRenderer.RenderDocument(
            "Title", null, null, "article", null, null, new Dictionary<string, string?>(), document,
            gtmContainerId: "javascript:alert(1)");

        Assert.DoesNotContain("googletagmanager.com", html);
        Assert.DoesNotContain("<noscript>", html);
    }

    [Fact]
    public void AssignSectionIds_slugifies_headings_and_disambiguates_collisions()
    {
        var lede = MakeSection("h2", "Opening");
        var child = MakeSection("h3", "Overview");
        var sections = new[]
        {
            new Section("h2", "Overview", [new TextParagraph([new Run("a")])], null, [child]),
            MakeSection("h2", "Overview"),
            MakeSection("h2", "People Also Ask"),
        };
        var document = ContentDocumentText.AssignSectionIds(new ContentDocument(lede, sections));

        Assert.Equal("opening", document.Lede.Id);
        Assert.Equal("overview", document.Sections[0].Id);
        Assert.Equal("overview-2", document.Sections[0].Children[0].Id);
        Assert.Equal("overview-3", document.Sections[1].Id);
        Assert.Equal("people-also-ask", document.Sections[2].Id);
    }

    [Fact]
    public void AppendSectionToc_adds_in_article_links_after_lede_skipping_faq()
    {
        var lede = MakeSection("h2", "Opening", "Hook paragraph.");
        var sections = new[]
        {
            MakeSection("h2", "Implementation Framework", "body"),
            MakeSection("h2", "People Also Ask", "faq"),
        };
        var document = ContentDocumentText.AppendSectionToc(
            ContentDocumentText.AssignSectionIds(new ContentDocument(lede, sections)));

        Assert.Equal(3, document.Lede.Paragraphs.Count);
        var leadIn = Assert.IsType<TextParagraph>(document.Lede.Paragraphs[1]);
        Assert.Equal("In this article:", leadIn.Runs[0].Text);

        var toc = Assert.IsType<ListParagraph>(document.Lede.Paragraphs[2]);
        Assert.Single(toc.Items);
        Assert.Equal("Implementation Framework", toc.Items[0][0].Text);
        Assert.Equal("#implementation-framework", toc.Items[0][0].Href);
    }

    [Fact]
    public void SectionHtmlRenderer_emits_heading_id_attributes_and_toc_hrefs()
    {
        var lede = MakeSection("h2", "Opening", "Hook.");
        var section = MakeSection("h2", "Main Section", "Detail.");
        var document = ContentDocumentText.AppendSectionToc(
            ContentDocumentText.AssignSectionIds(new ContentDocument(lede, [section])));

        var html = SectionHtmlRenderer.RenderFragment(document);

        Assert.Contains("id=\"opening\"", html);
        Assert.Contains("id=\"main-section\"", html);
        Assert.Contains("href=\"#main-section\"", html);
        Assert.Contains("In this article:", html);
    }

    [Fact]
    public void AppendChildSectionReferences_links_h3_to_its_h4_children()
    {
        var h4 = MakeSection("h4", "AI Content Creation and Repurposing for Small Businesses", "detail");
        var h3 = new Section(
            "h3",
            "AI Marketing Workflow",
            [new TextParagraph([new Run("Intro to the workflow.")])],
            null,
            [h4]);
        var h2 = new Section("h2", "Marketing Systems", [new TextParagraph([new Run("Section intro.")])], null, [h3]);
        var document = ContentDocumentText.AppendChildSectionReferences(
            ContentDocumentText.AssignSectionIds(new ContentDocument(MakeSection("h2", "Lede", "lede"), [h2])));

        var workflow = document.Sections[0].Children[0];
        Assert.Equal("AI Marketing Workflow", workflow.Heading);
        Assert.Equal(3, workflow.Paragraphs.Count); // intro + "In this section:" + list
        var leadIn = Assert.IsType<TextParagraph>(workflow.Paragraphs[1]);
        Assert.Equal("In this section:", leadIn.Runs[0].Text);
        var list = Assert.IsType<ListParagraph>(workflow.Paragraphs[2]);
        Assert.Single(list.Items);
        Assert.Equal("AI Content Creation and Repurposing for Small Businesses", list.Items[0][0].Text);
        Assert.Equal("#ai-content-creation-and-repurposing-for-small-businesses", list.Items[0][0].Href);

        // Parent H2 also references its H3 child.
        var parentList = Assert.IsType<ListParagraph>(document.Sections[0].Paragraphs[^1]);
        Assert.Equal("AI Marketing Workflow", parentList.Items[0][0].Text);
        Assert.Equal("#ai-marketing-workflow", parentList.Items[0][0].Href);

        var html = SectionHtmlRenderer.RenderFragment(document);
        Assert.Contains("id=\"ai-marketing-workflow\"", html);
        Assert.Contains("id=\"ai-content-creation-and-repurposing-for-small-businesses\"", html);
        Assert.Contains("href=\"#ai-content-creation-and-repurposing-for-small-businesses\"", html);
    }

    [Fact]
    public void AppendChildSectionReferences_skips_faq_parents()
    {
        var question = MakeSection("h3", "What is AI marketing?", "answer");
        var faq = new Section("h2", "People Also Ask", [], null, [question]);
        var document = ContentDocumentText.AppendChildSectionReferences(
            ContentDocumentText.AssignSectionIds(new ContentDocument(MakeSection("h2", "Lede", "lede"), [faq])));

        Assert.Empty(document.Sections[0].Paragraphs);
    }
}
