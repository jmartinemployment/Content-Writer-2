using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using ContentWriter.Domain.Entities;

namespace ContentWriter.Application.Services;

/// <summary>
/// Generates the provider-facing JSON schema via .NET's built-in <see cref="JsonSchemaExporter"/>,
/// fed the exact same <see cref="LlmResponseJsonParser.SectionJsonOptions"/> used for real
/// deserialization — the schema is derived from the live serializer contract (camelCase naming,
/// the registered <see cref="ParagraphJsonConverter"/>), not hand-maintained separately, so it
/// cannot silently drift from what <see cref="LlmResponseJsonParser"/> actually parses. The one
/// thing the exporter cannot infer on its own is <see cref="Paragraph"/>'s custom-converter wire
/// shape (no generic tool can guess a hand-written converter's contract) — that's supplied via a
/// small <see cref="JsonSchemaExporterOptions.TransformSchemaNode"/> callback below, the same
/// callback also enforces OpenAI/Groq strict-mode's <c>additionalProperties: false</c> +
/// full-<c>required</c> requirements across every object node, and swaps <c>oneOf</c> for
/// <c>anyOf</c> (Groq's structured outputs don't support <c>oneOf</c>/<c>const</c>).
/// </summary>
public static class ContentSectionJsonSchema
{
    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TransformSchemaNode = TransformNode,
    };

    /// <summary>Schema for a single <see cref="Section"/> object — used by
    /// <c>BuildArticleSectionPrompt</c>/<c>BuildArticleFaqSectionPrompt</c>/<c>BuildBlogSectionPrompt</c>.</summary>
    public static string SectionSchema { get; } =
        JsonSchemaExporter.GetJsonSchemaAsNode(LlmResponseJsonParser.SectionJsonOptions, typeof(Section), ExporterOptions)
            .ToJsonString();

    /// <summary>Schema for a top-level <c>{"sections": [...]}</c> response — used by prompts that
    /// generate/expand/trim a whole body's worth of sections at once (tool body, blog body,
    /// depth-expansion, word-count expansion/trim).</summary>
    public static string SectionsArraySchema { get; } =
        JsonSchemaExporter.GetJsonSchemaAsNode(LlmResponseJsonParser.SectionJsonOptions, typeof(SectionsArrayEnvelope), ExporterOptions)
            .ToJsonString();

    /// <summary>Wrapper type purely so the exporter generates a <c>{"sections": [...]}</c>-shaped
    /// schema — never actually (de)serialized as this type, only used to derive the schema shape.</summary>
    private sealed record SectionsArrayEnvelope(IReadOnlyList<Section> Sections);

    private static JsonNode TransformNode(JsonSchemaExporterContext context, JsonNode node)
    {
        // Paragraph is abstract with a hand-written converter (ParagraphJsonConverter) — the
        // exporter never generates a schema for it at all (silently emits a bare "array" with no
        // "items" for IReadOnlyList<Paragraph>, since it can't reflect properties on an abstract
        // type with a custom converter). No generic tool can infer that converter's
        // {"type":"text","runs":[...]} / {"type":"list","ordered":bool,"items":[[...]]} wire shape
        // from the type alone, so it's supplied explicitly here, injected as the array's "items".
        var t = context.TypeInfo.Type;
        var isParagraphList = t.IsGenericType
            && t.GetGenericArguments() is [var elemType]
            && elemType == typeof(Paragraph);

        if (isParagraphList && node is JsonObject arrayNode)
        {
            arrayNode["items"] = BuildParagraphUnionSchema();
            return arrayNode;
        }

        // The root node (and any nested object) defaults to a nullable ["object","null"] union —
        // the response is never actually null, so drop the "null" branch. Only affects nodes whose
        // "type" is the two-element ["object","null"] array (leaves other nullable unions, e.g.
        // string fields that are genuinely optional, untouched).
        if (node is JsonObject obj0
            && obj0.TryGetPropertyValue("type", out var typeArrNode)
            && typeArrNode is JsonArray { Count: 2 } typeArr
            && typeArr[0]?.ToString() == "object"
            && typeArr[1]?.ToString() == "null")
        {
            obj0["type"] = "object";
        }

        // OpenAI/Groq strict-mode structured outputs require every object to be "closed"
        // (additionalProperties: false) with every property listed in `required` (optional fields
        // are expressed as nullable unions, not omission) — apply this uniformly across the tree.
        if (node is JsonObject obj
            && obj.TryGetPropertyValue("type", out var typeNode)
            && typeNode is JsonValue typeValue
            && typeValue.GetValue<string>() == "object"
            && obj.TryGetPropertyValue("properties", out var propsNode)
            && propsNode is JsonObject props)
        {
            obj["additionalProperties"] = false;
            obj["required"] = new JsonArray(props.Select(p => JsonValue.Create(p.Key) as JsonNode).ToArray());
        }

        // Strict mode rejects "default" — every value must be explicitly produced by the model,
        // never implied by a schema default.
        if (node is JsonObject withDefault)
        {
            withDefault.Remove("default");
        }

        return node;
    }

    private static JsonNode BuildParagraphUnionSchema() => new JsonObject
    {
        ["anyOf"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray { "type", "runs" },
                ["properties"] = new JsonObject
                {
                    ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "text" } },
                    ["runs"] = new JsonObject { ["type"] = "array", ["items"] = BuildRunSchema() },
                },
            },
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray { "type", "ordered", "items" },
                ["properties"] = new JsonObject
                {
                    ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "list" } },
                    ["ordered"] = new JsonObject { ["type"] = "boolean" },
                    ["items"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "array", ["items"] = BuildRunSchema() },
                    },
                },
            },
        },
    };

    private static JsonObject BuildRunSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray { "text", "bold", "italic", "href" },
        ["properties"] = new JsonObject
        {
            ["text"] = new JsonObject { ["type"] = "string" },
            ["bold"] = new JsonObject { ["type"] = "boolean" },
            ["italic"] = new JsonObject { ["type"] = "boolean" },
            ["href"] = new JsonObject { ["anyOf"] = new JsonArray { new JsonObject { ["type"] = "string" }, new JsonObject { ["type"] = "null" } } },
        },
    };
}
