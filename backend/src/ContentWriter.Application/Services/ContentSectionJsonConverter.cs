using System.Text.Json;
using System.Text.Json.Serialization;
using ContentWriter.Domain.Entities;

namespace ContentWriter.Application.Services;

/// <summary>
/// (De)serializes the <see cref="Paragraph"/> discriminated union to/from the wire shape
/// { "type": "text" | "list", ... } that the JSON-schema-constrained provider response uses.
/// </summary>
public sealed class ParagraphJsonConverter : JsonConverter<Paragraph>
{
    public override Paragraph? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        return type switch
        {
            "list" => new ListParagraph(
                root.TryGetProperty("ordered", out var o) && o.GetBoolean(),
                root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                    ? items.EnumerateArray()
                        .Select(item => (IReadOnlyList<Run>)(item.ValueKind == JsonValueKind.Array
                            ? item.EnumerateArray().Select(r => r.Deserialize<Run>(options) ?? new Run(string.Empty)).ToList()
                            : []))
                        .ToList()
                    : []),
            _ => new TextParagraph(
                root.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array
                    ? runs.EnumerateArray().Select(r => r.Deserialize<Run>(options) ?? new Run(string.Empty)).ToList()
                    : []),
        };
    }

    public override void Write(Utf8JsonWriter writer, Paragraph value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case TextParagraph text:
                writer.WriteString("type", "text");
                writer.WritePropertyName("runs");
                JsonSerializer.Serialize(writer, text.Runs, options);
                break;
            case ListParagraph list:
                writer.WriteString("type", "list");
                writer.WriteBoolean("ordered", list.Ordered);
                writer.WritePropertyName("items");
                JsonSerializer.Serialize(writer, list.Items, options);
                break;
        }
        writer.WriteEndObject();
    }
}
