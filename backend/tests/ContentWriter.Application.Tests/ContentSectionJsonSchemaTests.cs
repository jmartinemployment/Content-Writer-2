using System.Text.Json;
using ContentWriter.Application.Services;
using Xunit;

namespace ContentWriter.Application.Tests;

public class ContentSectionJsonSchemaTests
{
    [Fact]
    public void SectionSchema_IsValidJson_AndMeetsStrictModeRequirements()
    {
        var doc = JsonDocument.Parse(ContentSectionJsonSchema.SectionSchema);
        var root = doc.RootElement;

        // Root is a $ref into $defs.section (not the inlined object itself) — required so the
        // recursive Section.Children reference points at a genuine top-level definition, which is
        // what OpenAI/Groq strict mode requires; a bare root self-reference ("$ref":"#") is only
        // accepted at the top level and gets rejected as soon as this schema is nested (e.g. inside
        // SectionsArraySchema's wrapper), so both schemas route recursion through $defs uniformly.
        Assert.Equal("#/$defs/section", root.GetProperty("$ref").GetString());

        var section = root.GetProperty("$defs").GetProperty("section");
        Assert.Equal("object", section.GetProperty("type").GetString());
        Assert.False(section.GetProperty("additionalProperties").GetBoolean());

        var required = section.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        // camelCase, matching the real wire contract LlmResponseJsonParser deserializes.
        Assert.Contains("tag", required);
        Assert.Contains("heading", required);
        Assert.Contains("paragraphs", required);
        Assert.Contains("children", required);
        Assert.Contains("imagePrompt", required);
        // Section.Id is code-assigned ([JsonIgnore]) — must not appear in the LLM schema.
        Assert.DoesNotContain("id", required);
        Assert.False(section.GetProperty("properties").TryGetProperty("id", out _));
    }

    [Fact]
    public void SectionSchema_ChildrenSelfReference_PointsAtDefsSection_NotBareRoot()
    {
        var doc = JsonDocument.Parse(ContentSectionJsonSchema.SectionSchema);
        var childrenRef = doc.RootElement.GetProperty("$defs").GetProperty("section")
            .GetProperty("properties").GetProperty("children").GetProperty("items").GetProperty("$ref").GetString();

        Assert.Equal("#/$defs/section", childrenRef);

        // No bare root self-reference should survive anywhere in the document.
        Assert.DoesNotContain("\"$ref\":\"#\"", ContentSectionJsonSchema.SectionSchema.Replace(" ", ""));
    }

    [Fact]
    public void SectionsArraySchema_SectionsItems_And_ChildrenSelfReference_PointAtDefsSection()
    {
        var doc = JsonDocument.Parse(ContentSectionJsonSchema.SectionsArraySchema);
        var root = doc.RootElement;

        var itemsRef = root.GetProperty("properties").GetProperty("sections").GetProperty("items")
            .GetProperty("$ref").GetString();
        Assert.Equal("#/$defs/section", itemsRef);

        var childrenRef = root.GetProperty("$defs").GetProperty("section")
            .GetProperty("properties").GetProperty("children").GetProperty("items").GetProperty("$ref").GetString();
        Assert.Equal("#/$defs/section", childrenRef);

        Assert.DoesNotContain("\"$ref\":\"#\"", ContentSectionJsonSchema.SectionsArraySchema.Replace(" ", ""));
        Assert.DoesNotContain("properties/sections/items", ContentSectionJsonSchema.SectionsArraySchema);
    }

    [Fact]
    public void SectionSchema_ParagraphUnion_UsesAnyOfNotOneOf_AndEnumNotConst()
    {
        // Groq's structured outputs don't support oneOf/const — anyOf + single-value enum must be
        // used instead so the same schema works on both OpenAI and Groq.
        var schema = ContentSectionJsonSchema.SectionSchema;

        Assert.DoesNotContain("\"oneOf\"", schema);
        Assert.DoesNotContain("\"const\"", schema);
        Assert.Contains("\"anyOf\"", schema);
        Assert.Contains("\"enum\":[\"text\"]", schema.Replace(" ", ""));
        Assert.Contains("\"enum\":[\"list\"]", schema.Replace(" ", ""));
    }

    [Fact]
    public void SectionSchema_HasNoDefaultKeyword()
    {
        // Strict mode rejects "default" — every value must be explicitly produced by the model.
        Assert.DoesNotContain("\"default\"", ContentSectionJsonSchema.SectionSchema);
    }

    [Fact]
    public void SectionsArraySchema_WrapsSectionsUnderTopLevelKey()
    {
        var doc = JsonDocument.Parse(ContentSectionJsonSchema.SectionsArraySchema);
        var root = doc.RootElement;

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("sections", root.GetProperty("required").EnumerateArray().Single().GetString());
        Assert.Equal("array", root.GetProperty("properties").GetProperty("sections").GetProperty("type").GetString());
    }
}
