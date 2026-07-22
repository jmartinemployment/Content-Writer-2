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
        var extraction = ToolSectionExtractor.DiagnoseExtraction(articleRow.Body, metadata.SectionOutline);
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

        if (articleRow.Body is not null)
        {
            articleRow.Body = ToolSectionExtractor.InjectToolLinks(
                articleRow.Body,
                metadata.SectionOutline,
                $"{context.ToolBaseUrl.TrimEnd('/')}/{context.Department}",
                rows.Select(r => (r.SourceAppName!, r.Slug)).ToList());
        }

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
        var toolUrl = $"{context.ToolBaseUrl.TrimEnd('/')}/{context.Department}/{slug}";

        var document = await GenerateToolBodyWithValidationAsync(
            provider, context, metadata, app, slug, cancellationToken);

        var toolMetadata = await GenerateToolMetadataAsync(
            provider, context, metadata, app, document, cancellationToken);

        var wordCount = ContentDocumentText.CountWords(document);
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
            Body = document,
            LedeType = Domain.Entities.LedeType.Summary,
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
        ContentDocument document,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await provider.CompleteAsync(
                _promptBuilder.BuildToolMetadataPrompt(context, pillarMetadata, app, document),
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

    /// <summary>Generates the tool page as a sections array; the first section (always "Overview")
    /// becomes the document's lede, the rest become its top-level sections.</summary>
    private async Task<ContentDocument> GenerateToolBodyWithValidationAsync(
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
        var sections = LlmResponseJsonParser.ParseSections(result.Content, $"tool page '{app.Name}'");
        var wordCount = ContentDocumentText.CountWords(sections);

        const int maxExpansionPasses = 3;
        for (var pass = 0; wordCount < ContentLengthTargets.ToolMinWords && pass < maxExpansionPasses; pass++)
        {
            var expansion = await provider.CompleteAsync(
                _promptBuilder.BuildToolWordCountExpansionPrompt(context, app, sections, wordCount),
                cancellationToken);
            var expanded = LlmResponseJsonParser.ParseSections(expansion.Content, $"tool page expansion '{app.Name}'");
            var expandedCount = ContentDocumentText.CountWords(expanded);
            if (expandedCount > wordCount)
            {
                sections = expanded;
                wordCount = expandedCount;
            }
        }

        if (wordCount > ContentLengthTargets.ToolHardMaxWords)
        {
            var trim = await provider.CompleteAsync(
                _promptBuilder.BuildToolWordCountTrimPrompt(context, app, sections, wordCount),
                cancellationToken);
            var trimmed = LlmResponseJsonParser.ParseSections(trim.Content, $"tool page trim '{app.Name}'");
            var trimmedCount = ContentDocumentText.CountWords(trimmed);
            if (trimmedCount < wordCount)
            {
                sections = trimmed;
                wordCount = trimmedCount;
            }
        }

        if (wordCount < ContentLengthTargets.ToolMinWords || wordCount > ContentLengthTargets.ToolHardMaxWords)
        {
            throw new ContentGenerationException(
                $"Tool page for '{app.Name}' is {wordCount:N0} words after retry; required range is " +
                $"{ContentLengthTargets.ToolMinWords:N0}-{ContentLengthTargets.ToolHardMaxWords:N0}.");
        }

        var lede = sections[0] with { Tag = "h2" };
        return new ContentDocument(lede, sections.Skip(1).ToList());
    }
}
