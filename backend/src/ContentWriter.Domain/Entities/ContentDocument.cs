using System.Text.Json.Serialization;

namespace ContentWriter.Domain.Entities;

public enum LedeType
{
    Creative,
    Summary,
}

public sealed record Run(string Text, bool Bold = false, bool Italic = false, string? Href = null);

public abstract record Paragraph;

public sealed record TextParagraph(IReadOnlyList<Run> Runs) : Paragraph;

public sealed record ListParagraph(bool Ordered, IReadOnlyList<IReadOnlyList<Run>> Items) : Paragraph;

public sealed record Section(
    string Tag,
    string Heading,
    IReadOnlyList<Paragraph> Paragraphs,
    string? Href,
    IReadOnlyList<Section> Children,
    string? ImagePrompt = null,
    /// <summary>In-page anchor id for jump links / TOC. Assigned in code after generation —
    /// ignored by the LLM section JSON schema so the model never invents or omits it.</summary>
    [property: JsonIgnore]
    string? Id = null);

/// <summary>
/// A generated body: a lede section (opening hook, always tag "h2") followed by the body's
/// section tree. Per-element addressing (doc.Sections[1].Children[0]) is plain object indexing —
/// no text blob is ever parsed to recover this structure.
/// </summary>
public sealed record ContentDocument(Section Lede, IReadOnlyList<Section> Sections);
