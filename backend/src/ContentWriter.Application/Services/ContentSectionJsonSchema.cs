using Json.Schema;
using Json.Schema.Generation;
using ContentWriter.Domain.Entities;

namespace ContentWriter.Application.Services;

/// <summary>
/// Generates the provider-facing JSON schema directly from the <see cref="Section"/> C# record —
/// the record is the single source of truth for the contract; this schema can never drift from the
/// deserialization type because it's derived from it, not hand-maintained alongside it.
/// </summary>
public static class ContentSectionJsonSchema
{
    private static readonly Lazy<string?> SectionSchemaJson = new(() =>
    {
        try
        {
            return new JsonSchemaBuilder().FromType<Section>().Build().ToString();
        }
        catch
        {
            // Schema generation for the polymorphic Paragraph union may not be supported on every
            // library version — degrade to prompt-only generation (the Groq/LM Studio fallback path)
            // rather than fail the request; the two-tier validation in LlmResponseJsonParser still
            // guarantees correctness without provider-native enforcement.
            return null;
        }
    });

    /// <summary>Null when schema generation isn't available — callers should omit JsonSchema on the
    /// request in that case, which is the same fallback path Groq/LM Studio already use.</summary>
    public static string? SectionSchema => SectionSchemaJson.Value;
}
