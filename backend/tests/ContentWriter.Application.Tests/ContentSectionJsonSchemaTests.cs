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

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());

        var required = root.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        // camelCase, matching the real wire contract LlmResponseJsonParser deserializes.
        Assert.Contains("tag", required);
        Assert.Contains("heading", required);
        Assert.Contains("paragraphs", required);
        Assert.Contains("children", required);
        Assert.Contains("imagePrompt", required);
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
