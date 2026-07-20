using System.Text.Json;
using System.Text.Json.Serialization;
using ContentWriter.Application.DTOs;
using ContentWriter.Domain.Entities;

namespace ContentWriter.Application.Services;

public static class ImagePromptMetadata
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ImagePromptSectionDraft item) =>
        JsonSerializer.Serialize(new StoredImagePromptSettings(
            item.Width,
            item.Height,
            item.ImageModel,
            ResolveModelId(item.ImageModel),
            item.StylePreset,
            item.Alchemy,
            item.PhotoReal,
            item.Notes,
            item.SourceType,
            item.Heading,
            item.Order), JsonOptions);

    public static ImagePromptSectionContent ToSectionContent(GeneratedContent row)
    {
        var settings = Deserialize(row.MetaDescription);
        return new ImagePromptSectionContent(
            settings.SourceType ?? InferSourceType(row),
            settings.Heading ?? row.Title,
            settings.Order,
            row.BodyHtml,
            settings.Width,
            settings.Height,
            settings.ImageModel,
            settings.ImageModelId,
            settings.StylePreset,
            settings.Alchemy,
            settings.PhotoReal,
            settings.Notes);
    }

    private static string InferSourceType(GeneratedContent row) =>
        row.Slug.Contains("-blog-h2-", StringComparison.OrdinalIgnoreCase) ? "blog" : "pillar";

    private static StoredImagePromptSettings Deserialize(string? metaJson)
    {
        if (string.IsNullOrWhiteSpace(metaJson))
        {
            return new StoredImagePromptSettings(
                ImagePromptDefaults.PillarWidth,
                ImagePromptDefaults.PillarHeight,
                ImagePromptDefaults.DefaultImageModel,
                ImagePromptDefaults.DefaultImageModelId,
                ImagePromptDefaults.PillarStylePreset,
                Alchemy: true,
                PhotoReal: false,
                Notes: null,
                SourceType: null,
                Heading: null,
                Order: 0);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<StoredImagePromptSettings>(metaJson, JsonOptions);
            if (parsed is null)
                throw new JsonException("Null settings.");

            return parsed with
            {
                ImageModelId = string.IsNullOrWhiteSpace(parsed.ImageModelId)
                    ? ResolveModelId(parsed.ImageModel)
                    : parsed.ImageModelId,
            };
        }
        catch (JsonException)
        {
            return new StoredImagePromptSettings(
                ImagePromptDefaults.PillarWidth,
                ImagePromptDefaults.PillarHeight,
                ImagePromptDefaults.DefaultImageModel,
                ImagePromptDefaults.DefaultImageModelId,
                ImagePromptDefaults.PillarStylePreset,
                Alchemy: true,
                PhotoReal: false,
                Notes: metaJson,
                SourceType: null,
                Heading: null,
                Order: 0);
        }
    }

    private static string ResolveModelId(string modelName) => ImagePromptDefaults.DefaultImageModelId;

    private sealed record StoredImagePromptSettings(
        int Width,
        int Height,
        string ImageModel,
        string ImageModelId,
        string StylePreset,
        bool Alchemy,
        bool PhotoReal,
        string? Notes,
        string? SourceType,
        string? Heading,
        int Order);
}
