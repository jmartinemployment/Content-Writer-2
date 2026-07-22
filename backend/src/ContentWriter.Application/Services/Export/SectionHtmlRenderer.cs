using ContentWriter.Domain.Entities;
using HtmlAgilityPack;

namespace ContentWriter.Application.Services.Export;

/// <summary>
/// The only place tag characters are produced in the whole pipeline. Builds an HtmlAgilityPack DOM
/// node-by-node from a <see cref="ContentDocument"/> — never string/StringBuilder concatenation of
/// markup — so tags are always balanced and correctly nested by construction, and inserted text is
/// HTML-encoded automatically.
/// </summary>
public static class SectionHtmlRenderer
{
    /// <summary>Builds a full standalone HTML document: doctype, head metadata (canonical, Open Graph,
    /// Twitter card, JSON+LD, robots, viewport), H1 title, lede, sections.</summary>
    public static string RenderDocument(
        string title,
        string? description,
        string? canonicalUrl,
        string ogType,
        string? ogImage,
        string? jsonLdSchema,
        IReadOnlyDictionary<string, string?> additionalMeta,
        ContentDocument body)
    {
        var doc = new HtmlDocument();
        var html = doc.CreateElement("html");
        html.SetAttributeValue("lang", "en");
        doc.DocumentNode.AppendChild(html);

        var head = doc.CreateElement("head");
        html.AppendChild(head);
        AppendMeta(doc, head, "charset", null, "utf-8");
        AppendMeta(doc, head, null, "viewport", "width=device-width, initial-scale=1");
        AppendMeta(doc, head, null, "robots", "index, follow");

        var titleNode = doc.CreateElement("title");
        titleNode.AppendChild(CreateEncodedTextNode(doc, title));
        head.AppendChild(titleNode);
        if (!string.IsNullOrWhiteSpace(description))
        {
            AppendMeta(doc, head, null, "description", description);
        }
        if (!string.IsNullOrWhiteSpace(canonicalUrl))
        {
            var link = doc.CreateElement("link");
            link.SetAttributeValue("rel", "canonical");
            link.SetAttributeValue("href", canonicalUrl);
            head.AppendChild(link);
        }

        AppendOpenGraphAndTwitter(doc, head, title, description, canonicalUrl, ogType, ogImage);

        if (!string.IsNullOrWhiteSpace(jsonLdSchema) && jsonLdSchema.Trim() is not ("{}" or "[]"))
        {
            var script = doc.CreateElement("script");
            script.SetAttributeValue("type", "application/ld+json");
            // JSON, not HTML — must not be entity-encoded, but a literal "</script>" inside a string
            // value would still break out of the tag, so guard against that specifically.
            script.InnerHtml = jsonLdSchema.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
            head.AppendChild(script);
        }

        foreach (var (name, value) in additionalMeta)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                AppendMeta(doc, head, null, name, value!);
            }
        }

        var body_ = doc.CreateElement("body");
        html.AppendChild(body_);

        var h1 = doc.CreateElement("h1");
        h1.AppendChild(CreateEncodedTextNode(doc, title));
        body_.AppendChild(h1);

        AppendSection(doc, body_, body.Lede);
        foreach (var section in body.Sections)
        {
            AppendSection(doc, body_, section);
        }

        return "<!doctype html>\n" + doc.DocumentNode.OuterHtml;
    }

    private static void AppendOpenGraphAndTwitter(
        HtmlDocument doc, HtmlNode head, string title, string? description, string? canonicalUrl, string ogType, string? ogImage)
    {
        AppendMetaProperty(doc, head, "og:type", ogType);
        AppendMetaProperty(doc, head, "og:title", title);
        if (!string.IsNullOrWhiteSpace(description))
        {
            AppendMetaProperty(doc, head, "og:description", description);
        }
        if (!string.IsNullOrWhiteSpace(canonicalUrl))
        {
            AppendMetaProperty(doc, head, "og:url", canonicalUrl);
        }
        if (!string.IsNullOrWhiteSpace(ogImage))
        {
            AppendMetaProperty(doc, head, "og:image", ogImage);
        }

        AppendMeta(doc, head, null, "twitter:card", string.IsNullOrWhiteSpace(ogImage) ? "summary" : "summary_large_image");
        AppendMeta(doc, head, null, "twitter:title", title);
        if (!string.IsNullOrWhiteSpace(description))
        {
            AppendMeta(doc, head, null, "twitter:description", description);
        }
        if (!string.IsNullOrWhiteSpace(ogImage))
        {
            AppendMeta(doc, head, null, "twitter:image", ogImage);
        }
    }

    private static void AppendMetaProperty(HtmlDocument doc, HtmlNode head, string property, string content)
    {
        var meta = doc.CreateElement("meta");
        meta.SetAttributeValue("property", property);
        meta.SetAttributeValue("content", content);
        head.AppendChild(meta);
    }

    /// <summary>Renders just the body fragment (lede + sections) — used by the preview UI.</summary>
    public static string RenderFragment(ContentDocument body)
    {
        var doc = new HtmlDocument();
        var container = doc.CreateElement("div");
        doc.DocumentNode.AppendChild(container);

        AppendSection(doc, container, body.Lede);
        foreach (var section in body.Sections)
        {
            AppendSection(doc, container, section);
        }

        return container.InnerHtml;
    }

    private static void AppendMeta(HtmlDocument doc, HtmlNode head, string? charset, string? name, string content)
    {
        var meta = doc.CreateElement("meta");
        if (charset is not null)
        {
            meta.SetAttributeValue("charset", content);
        }
        else
        {
            meta.SetAttributeValue("name", name);
            meta.SetAttributeValue("content", content);
        }
        head.AppendChild(meta);
    }

    private static void AppendSection(HtmlDocument doc, HtmlNode parent, Section section)
    {
        var headingTag = doc.CreateElement(section.Tag);
        if (!string.IsNullOrWhiteSpace(section.Href))
        {
            var anchor = doc.CreateElement("a");
            anchor.SetAttributeValue("href", section.Href);
            anchor.AppendChild(CreateEncodedTextNode(doc, section.Heading));
            headingTag.AppendChild(anchor);
        }
        else
        {
            headingTag.AppendChild(CreateEncodedTextNode(doc, section.Heading));
        }
        parent.AppendChild(headingTag);

        foreach (var paragraph in section.Paragraphs)
        {
            AppendParagraph(doc, parent, paragraph);
        }

        foreach (var child in section.Children)
        {
            AppendSection(doc, parent, child);
        }
    }

    private static void AppendParagraph(HtmlDocument doc, HtmlNode parent, Paragraph paragraph)
    {
        switch (paragraph)
        {
            case TextParagraph text:
                var p = doc.CreateElement("p");
                AppendRuns(doc, p, text.Runs);
                parent.AppendChild(p);
                break;

            case ListParagraph list:
                var listNode = doc.CreateElement(list.Ordered ? "ol" : "ul");
                foreach (var item in list.Items)
                {
                    var li = doc.CreateElement("li");
                    AppendRuns(doc, li, item);
                    listNode.AppendChild(li);
                }
                parent.AppendChild(listNode);
                break;
        }
    }

    private static void AppendRuns(HtmlDocument doc, HtmlNode parent, IReadOnlyList<Run> runs)
    {
        foreach (var run in runs)
        {
            HtmlNode textHost = parent;

            if (!string.IsNullOrWhiteSpace(run.Href))
            {
                var anchor = doc.CreateElement("a");
                anchor.SetAttributeValue("href", run.Href);
                parent.AppendChild(anchor);
                textHost = anchor;
            }

            if (run.Bold)
            {
                var strong = doc.CreateElement("strong");
                textHost.AppendChild(strong);
                textHost = strong;
            }

            if (run.Italic)
            {
                var em = doc.CreateElement("em");
                textHost.AppendChild(em);
                textHost = em;
            }

            textHost.AppendChild(CreateEncodedTextNode(doc, run.Text));
        }
    }

    /// <summary>HtmlAgilityPack's CreateTextNode does not HTML-encode on its own — without this,
    /// a stray "&lt;script&gt;" that slipped past content-hygiene validation would render as live
    /// markup instead of literal text. Encoding here is the actual injection guard.</summary>
    private static HtmlNode CreateEncodedTextNode(HtmlDocument doc, string text) =>
        doc.CreateTextNode(System.Net.WebUtility.HtmlEncode(text));
}
