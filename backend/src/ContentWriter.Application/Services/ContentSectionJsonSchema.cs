using System.Text.Json.Nodes;
using System.Text.Json.Schema;
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
///
/// The exporter's default recursion for <see cref="Section.Children"/> emits a bare
/// <c>{"$ref":"#"}</c> (root self-reference). That's rejected by OpenAI strict mode as soon as the
/// schema is nested inside a wrapper (e.g. <see cref="SectionsArraySchema"/>'s <c>{"sections":[...]}</c>
/// envelope) — OpenAI requires every <c>$ref</c> to point at a genuine top-level <c>$defs</c> entry,
/// not the document root or an arbitrary internal JSON pointer. <see cref="RewriteRootSelfReferences"/>
/// fixes this once, after the exporter (and its <c>TransformNode</c> callback) have fully finished —
/// it is a separate, later pass, not interleaved with <c>TransformNode</c>.
/// </summary>
public static class ContentSectionJsonSchema
{
    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TransformSchemaNode = TransformNode,
    };

    /// <summary>Schema for a single <see cref="Section"/> object — used by
    /// <c>BuildArticleSectionPrompt</c>/<c>BuildArticleFaqSectionPrompt</c>.</summary>
    public static string SectionSchema { get; } = BuildSectionSchema();

    /// <summary>Schema for a top-level <c>{"sections": [...]}</c> response — used by prompts that
    /// generate/expand/trim a whole body's worth of sections at once (tool body, blog body).</summary>
    public static string SectionsArraySchema { get; } = BuildSectionsArraySchema();

    private static string BuildSectionSchema()
    {
        var sectionNode = ExportSectionNode();

        var root = new JsonObject
        {
            ["$ref"] = "#/$defs/section",
            ["$defs"] = new JsonObject { ["section"] = sectionNode },
        };

        return root.ToJsonString();
    }

    private static string BuildSectionsArraySchema()
    {
        // A fresh clone of the already-transformed, already-rewritten node — cloning happens after
        // both the exporter/TransformNode pass and the $ref rewrite, so the clone needs no
        // independent re-processing. A JsonNode can only have one parent, so the same instance
        // from BuildSectionSchema can't be reused directly here.
        var sectionNode = ExportSectionNode();
        var clonedForArray = JsonNode.Parse(sectionNode.ToJsonString())!;

        var root = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray { "sections" },
            ["properties"] = new JsonObject
            {
                ["sections"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["$ref"] = "#/$defs/section" },
                },
            },
            ["$defs"] = new JsonObject { ["section"] = clonedForArray },
        };

        return root.ToJsonString();
    }

    /// <summary>Runs the exporter (which internally invokes <see cref="TransformNode"/> once per
    /// node) to completion, then rewrites the resulting tree's self-references. The rewrite is a
    /// distinct, later pass — <c>TransformNode</c> has already finished by the time it runs, so
    /// there's no interleaving and no risk of it re-touching (or undoing) the rewritten refs.</summary>
    private static JsonNode ExportSectionNode()
    {
        var sectionNode = JsonSchemaExporter.GetJsonSchemaAsNode(
            LlmResponseJsonParser.SectionJsonOptions, typeof(Section), ExporterOptions);
        RewriteRootSelfReferences(sectionNode);
        return sectionNode;
    }

    /// <summary>Recursively finds any <c>{"$ref":"#"}</c> node (the exporter's root-recursion
    /// marker for <see cref="Section.Children"/>) and rewrites it in place to
    /// <c>{"$ref":"#/$defs/section"}</c> — the only self-reference the exporter produces.</summary>
    private static void RewriteRootSelfReferences(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("$ref", out var refNode)
                    && refNode is JsonValue refValue
                    && refValue.GetValue<string>() == "#")
                {
                    obj["$ref"] = "#/$defs/section";
                }

                foreach (var property in obj.ToList())
                {
                    RewriteRootSelfReferences(property.Value);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    RewriteRootSelfReferences(item);
                }

                break;
        }
    }

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
