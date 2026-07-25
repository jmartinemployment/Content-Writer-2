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
        string? revisionNotes = null,
        IReadOnlySet<string>? toolSlugsToRegenerate = null,
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
        string? revisionNotes = null,
        IReadOnlySet<string>? toolSlugsToRegenerate = null,
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

        // A full run (no filter) regenerates every tool, same as before. A targeted rewrite only
        // regenerates the requested slug(s) — the rest of the current tool-post set is left
        // untouched by the caller (see ContentGenerationOrchestrator.GenerateToolPagesAsync).
        var slotsToGenerate = toolSlugsToRegenerate is null or { Count: 0 }
            ? slotted
            : slotted.Where(s => toolSlugsToRegenerate.Contains(s.Slug)).ToList();
        if (slotsToGenerate.Count == 0)
        {
            throw new ContentGenerationException(
                "None of the requested tool slugs match the pillar's current Tools section.");
        }

        var rows = (await Task.WhenAll(slotsToGenerate.Select(slot => GenerateOneToolAsync(
                project, metadata, context, provider, pillarArticleUrl,
                slot.App, slot.Slug, slot.Order, revisionNotes, cancellationToken))))
            .ToList();

        if (articleRow.Body is not null)
        {
            // Always reinject links for the full current tool set, not just the ones regenerated
            // this call — a targeted rewrite must not drop links for tools left untouched.
            articleRow.Body = ToolSectionExtractor.InjectToolLinks(
                articleRow.Body,
                metadata.SectionOutline,
                $"{context.ToolBaseUrl.TrimEnd('/')}/{context.Department}",
                slotted.Select(s => (s.App.Name, s.Slug)).ToList());
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
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        var toolUrl = $"{context.ToolBaseUrl.TrimEnd('/')}/{context.Department}/{slug}";

        var document = await GenerateToolBodyWithValidationAsync(
            provider, context, metadata, app, slug, revisionNotes, cancellationToken);

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
        var result = await provider.CompleteAsync(
            _promptBuilder.BuildToolMetadataPrompt(context, pillarMetadata, app, document),
            cancellationToken);

        return LlmResponseJsonParser.Parse<ToolMetadataDraft>(result.Content, "tool metadata");
    }

    /// <summary>Generates the tool page as a sections array; the first section (always "Overview")
    /// becomes the document's lede, the rest become its top-level sections.</summary>
    private async Task<ContentDocument> GenerateToolBodyWithValidationAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SoftwareApplicationDescriptor app,
        string toolSlug,
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        var result = await provider.CompleteAsync(
            _promptBuilder.BuildToolBodyPrompt(context, pillarMetadata, app, toolSlug, revisionNotes),
            cancellationToken);
        var sections = LlmResponseJsonParser.ParseSections(result.Content, $"tool page '{app.Name}'");
        var wordCount = ContentDocumentText.CountWords(sections);

        if (wordCount < ContentLengthTargets.ToolMinWords || wordCount > ContentLengthTargets.ToolHardMaxWords)
        {
            throw new ContentGenerationException(
                $"Tool page for '{app.Name}' is {wordCount:N0} words; required range is " +
                $"{ContentLengthTargets.ToolMinWords:N0}-{ContentLengthTargets.ToolHardMaxWords:N0}.");
        }

        var lede = sections[0] with { Tag = "h2" };
        return new ContentDocument(lede, sections.Skip(1).ToList());
    }
}
