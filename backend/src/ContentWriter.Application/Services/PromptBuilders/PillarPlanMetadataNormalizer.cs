using ContentWriter.Application.DTOs;

namespace ContentWriter.Application.Services.PromptBuilders;

internal static class PillarPlanMetadataNormalizer
{
    public static ArticleMetadataDraft Normalize(ArticleMetadataDraft metadata, string targetKeyword)
    {
        var title = metadata.Title.Trim();
        if (title.EndsWith('?') || title.StartsWith("How ", StringComparison.OrdinalIgnoreCase))
        {
            title = ToDefinitiveTitle(targetKeyword);
        }

        return metadata with { Title = title };
    }

    private static string ToDefinitiveTitle(string targetKeyword) =>
        $"{char.ToUpper(targetKeyword[0])}{targetKeyword[1..]}: Implementation Guide";
}
