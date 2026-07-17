using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.SchemaBuilders;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Application.Services;

public interface IToolPageGenerator
{
    Task<ToolGenerationResult> GenerateToolPagesAsync(
        Project project,
        GeneratedContent articleRow,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        CancellationToken cancellationToken = default);
}

public sealed record ToolGenerationResult(
    ToolGenerationOutcome Outcome,
    IReadOnlyList<GeneratedContent> ToolPosts);

public sealed class ToolPageGenerator : IToolPageGenerator
{
    private const int MaxTools = 5;
    private readonly ISoftwareApplicationSchemaBuilder _softwareApplicationSchemaBuilder;
    private readonly IContentPromptBuilder _promptBuilder;

    public ToolPageGenerator(
        ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder,
        IContentPromptBuilder promptBuilder)
    {
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
        _promptBuilder = promptBuilder;
    }

    public async Task<ToolGenerationResult> GenerateToolPagesAsync(
        Project project,
        GeneratedContent articleRow,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        CancellationToken cancellationToken = default)
    {
        var extraction = ToolsSectionHtmlParser.DiagnoseExtraction(articleRow.BodyHtml, metadata.SectionOutline);
        if (extraction.Outcome != ToolGenerationOutcome.Success)
        {
            return new ToolGenerationResult(extraction.Outcome, []);
        }

        var applications = extraction.Applications.Take(MaxTools).ToList();
        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slotted = applications
            .Select((app, index) => (
                App: app,
                Slug: SlugHelper.EnsureUniqueSlug(SlugHelper.Slugify(app.Name), usedSlugs),
                Order: index + 1))
            .ToList();

        var rows = (await Task.WhenAll(slotted.Select(slot => GenerateOneToolAsync(
                project, metadata, context, provider, pillarArticleUrl,
                slot.App, slot.Slug, slot.Order, cancellationToken))))
            .ToList();

        articleRow.BodyHtml = ToolsSectionHtmlParser.InjectToolLinks(
            articleRow.BodyHtml,
            metadata.SectionOutline,
            context.ToolBaseUrl,
            rows.Select(r => (r.SourceAppName!, r.Slug)).ToList());

        return new ToolGenerationResult(ToolGenerationOutcome.Success, rows);
    }

    private async Task<GeneratedContent> GenerateOneToolAsync(
        Project project,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        SoftwareApplicationDescriptor app,
        string slug,
        int order,
        CancellationToken cancellationToken)
    {
        var toolUrl = $"{context.ToolBaseUrl.TrimEnd('/')}/{slug}";

        var bodyHtml = await GenerateToolBodyWithValidationAsync(
            provider, context, metadata, app, slug, cancellationToken);

        var toolMetadata = await GenerateToolMetadataAsync(
            provider, context, metadata, app, bodyHtml, cancellationToken);

        var wordCount = HtmlWordCounter.Count(bodyHtml);
        var displayTitle = app.Name.Trim();
        var now = DateTime.UtcNow;
        var schemaMeta = new ContentMetadata(
            displayTitle,
            toolMetadata.MetaDescription,
            context.AuthorName,
            context.PublisherName,
            context.PublisherLogoUrl,
            toolUrl,
            context.PublisherLogoUrl,
            now,
            now,
            metadata.Keywords,
            wordCount);

        var jsonLd = _softwareApplicationSchemaBuilder.BuildToolPage(schemaMeta, pillarArticleUrl, app);

        return new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.ToolPost,
            Title = displayTitle,
            DisplayTitle = displayTitle,
            Slug = slug,
            Summary = toolMetadata.Summary,
            MainSummary = toolMetadata.MainSummary,
            HeroSummary = toolMetadata.HeroSummary,
            HomeSummary = toolMetadata.HomeSummary,
            BlogSummary = toolMetadata.BlogSummary,
            DepartmentListExcerpt = toolMetadata.DepartmentListExcerpt,
            ToolPageExcerpt = toolMetadata.ToolPageExcerpt,
            AdvertisingSummary = toolMetadata.AdvertisingSummary,
            MetaDescription = toolMetadata.MetaDescription.Length > 160
                ? toolMetadata.MetaDescription[..160]
                : toolMetadata.MetaDescription,
            BodyHtml = bodyHtml,
            JsonLdSchema = string.IsNullOrWhiteSpace(jsonLd) ? "{}" : jsonLd,
            RelatedArticleUrl = pillarArticleUrl,
            SourceAppName = app.Name,
            SourceAppOrder = order,
            WordCount = wordCount,
            GeneratedByProvider = provider.ProviderType,
            GeneratedByModel = provider.ProviderType.ToString(),
        };
    }

    private async Task<ToolMetadataDraft> GenerateToolMetadataAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SoftwareApplicationDescriptor app,
        string bodyHtml,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await provider.CompleteAsync(
                _promptBuilder.BuildToolMetadataPrompt(context, pillarMetadata, app, bodyHtml),
                cancellationToken);
            try
            {
                return LlmResponseJsonParser.Parse<ToolMetadataDraft>(result.Content, "tool metadata");
            }
            catch (ContentGenerationException ex) when (attempt < maxAttempts)
            {
                _ = ex;
            }
        }

        throw new ContentGenerationException(
            $"Model did not return valid JSON for tool metadata for '{app.Name}' after {maxAttempts} attempts.");
    }

    private async Task<string> GenerateToolBodyWithValidationAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SoftwareApplicationDescriptor app,
        string toolSlug,
        CancellationToken cancellationToken)
    {
        var result = await provider.CompleteAsync(
            _promptBuilder.BuildToolBodyPrompt(context, pillarMetadata, app, toolSlug),
            cancellationToken);
        var bodyHtml = LlmResponseJsonParser.ParseHtmlBody(result.Content, $"tool page '{app.Name}'");
        var wordCount = HtmlWordCounter.Count(bodyHtml);

        const int maxExpansionPasses = 3;
        for (var pass = 0; wordCount < ContentLengthTargets.ToolMinWords && pass < maxExpansionPasses; pass++)
        {
            var expansion = await provider.CompleteAsync(
                _promptBuilder.BuildToolWordCountExpansionPrompt(context, app, bodyHtml, wordCount),
                cancellationToken);
            var expanded = LlmResponseJsonParser.ParseHtmlBody(expansion.Content, $"tool page expansion '{app.Name}'");
            var expandedCount = HtmlWordCounter.Count(expanded);
            if (expandedCount > wordCount)
            {
                bodyHtml = expanded;
                wordCount = expandedCount;
            }
        }

        if (wordCount > ContentLengthTargets.ToolHardMaxWords)
        {
            var trim = await provider.CompleteAsync(
                _promptBuilder.BuildToolWordCountTrimPrompt(context, app, bodyHtml, wordCount),
                cancellationToken);
            var trimmed = LlmResponseJsonParser.ParseHtmlBody(trim.Content, $"tool page trim '{app.Name}'");
            var trimmedCount = HtmlWordCounter.Count(trimmed);
            if (trimmedCount < wordCount)
            {
                bodyHtml = trimmed;
                wordCount = trimmedCount;
            }
        }

        if (wordCount < ContentLengthTargets.ToolMinWords || wordCount > ContentLengthTargets.ToolHardMaxWords)
        {
            throw new ContentGenerationException(
                $"Tool page for '{app.Name}' is {wordCount:N0} words after retry; required range is " +
                $"{ContentLengthTargets.ToolMinWords:N0}-{ContentLengthTargets.ToolHardMaxWords:N0}.");
        }

        return HtmlBodyNormalizer.Normalize(bodyHtml);
    }
}
