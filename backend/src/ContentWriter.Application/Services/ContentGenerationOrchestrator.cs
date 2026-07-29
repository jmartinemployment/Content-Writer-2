using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.JsonLd;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.SchemaBuilders;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.InMemory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentWriter.Application.Services;

public class ContentGenerationOrchestrator : IContentGenerationOrchestrator
{
    private const int MaxPeopleAlsoAskQuestions = 12;

    private readonly IProjectStore _projectStore;
    private readonly IContentProviderFactory _providerFactory;
    private readonly IContentPromptBuilder _promptBuilder;
    private readonly IJsonLdParserService _jsonLdParser;
    private readonly ITechnicalArticleSchemaBuilder _articleSchemaBuilder;
    private readonly IBlogPostingSchemaBuilder _blogSchemaBuilder;
    private readonly IToolPageGenerator _toolPageGenerator;
    private readonly CompanyProfileOptions _companyProfile;
    private readonly ILogger<ContentGenerationOrchestrator> _logger;

    public ContentGenerationOrchestrator(
        IProjectStore projectStore,
        IContentProviderFactory providerFactory,
        IContentPromptBuilder promptBuilder,
        IJsonLdParserService jsonLdParser,
        ITechnicalArticleSchemaBuilder articleSchemaBuilder,
        IBlogPostingSchemaBuilder blogSchemaBuilder,
        IToolPageGenerator toolPageGenerator,
        IOptions<CompanyProfileOptions> companyProfile,
        ILogger<ContentGenerationOrchestrator> logger)
    {
        _projectStore = projectStore;
        _providerFactory = providerFactory;
        _promptBuilder = promptBuilder;
        _jsonLdParser = jsonLdParser;
        _articleSchemaBuilder = articleSchemaBuilder;
        _blogSchemaBuilder = blogSchemaBuilder;
        _toolPageGenerator = toolPageGenerator;
        _companyProfile = companyProfile.Value;
        _logger = logger;
    }

    public async Task<GeneratedContentSet> GeneratePillarPlanAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectForGenerationAsync(projectId, cancellationToken);
        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);

        _logger.LogInformation("Generating pillar plan for project {ProjectId} via {Provider}", projectId, provider.ProviderType);

        RemoveGeneratedContents(project,
            GeneratedContentType.TechnicalArticle,
            GeneratedContentType.ToolPost,
            GeneratedContentType.BlogPost,
            GeneratedContentType.SocialFacebook,
            GeneratedContentType.SocialLinkedIn,
            GeneratedContentType.EmailColdOutreach,
            GeneratedContentType.ImagePromptPillarFigure,
            GeneratedContentType.ImagePromptSocialFacebook,
            GeneratedContentType.ImagePromptSocialLinkedIn,
            GeneratedContentType.ImagePromptSection);

        var metadata = await GenerateArticleMetadataAsync(provider, context, cancellationToken);
        var articleSlug = SlugHelper.Slugify(metadata.Title);

        await AddContentAsync(project, provider.ProviderType, new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.TechnicalArticle,
            Title = metadata.Title,
            Slug = articleSlug,
            MetaDescription = metadata.MetaDescription,
            Keywords = metadata.Keywords,
            SectionOutline = metadata.SectionOutline,
            WordCount = 0,
            GeneratedByProvider = provider.ProviderType,
            GeneratedByModel = ResolveModelName(project.PreferredProvider)
        }, cancellationToken);

        await SaveProjectAsync(project, ProjectStatus.ReadyForGeneration, cancellationToken);
        return Assemble(project);
    }

    public async Task<GeneratedContentSet> GeneratePillarBodyAsync(Guid projectId, string? revisionNotes = null, CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectForGenerationAsync(projectId, cancellationToken);
        var articleRow = RequireGeneratedContent(project, GeneratedContentType.TechnicalArticle,
            "Generate the pillar plan (Step 1) before writing the article body.");

        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);
        var metadata = ToMetadataDraft(articleRow);
        var (bodyMetadata, faqQuestions) = PrepareBodyInput(metadata, context.PeopleAlsoAskQuestions, context.TargetKeyword, context.DesiredHeadings);
        if (!articleRow.SectionOutline.SequenceEqual(bodyMetadata.SectionOutline))
        {
            articleRow.SectionOutline = bodyMetadata.SectionOutline;
        }

        var isRegeneration = articleRow.Body is not null && articleRow.WordCount > 0;

        _logger.LogInformation(
            "Generating pillar body for project {ProjectId} via {Provider} (regeneration={IsRegeneration}, faqCount={FaqCount})",
            projectId, provider.ProviderType, isRegeneration, faqQuestions.Count);

        if (!string.IsNullOrWhiteSpace(revisionNotes))
        {
            bodyMetadata = await ApplyMetaRevisionNotesAsync(
                provider, context, articleRow, bodyMetadata, revisionNotes, cancellationToken);
        }

        var existingLedeHeading = articleRow.Body?.Lede.Heading;
        var (document, ledeType) = await GenerateArticleBodyAsync(
            provider, context, bodyMetadata, faqQuestions, isRegeneration, revisionNotes, existingLedeHeading, cancellationToken);
        var wordCount = ContentDocumentText.CountWords(document);

        if (wordCount < ContentLengthTargets.PillarMinWords)
        {
            _logger.LogWarning(
                "Pillar body for project keyword \"{Keyword}\" is {Count} words (minimum {Minimum}) — no expansion pass, single attempt only; saving anyway.",
                context.TargetKeyword,
                wordCount,
                ContentLengthTargets.PillarMinWords);
        }
        else if (wordCount > ContentLengthTargets.PillarTargetMaxWords)
        {
            _logger.LogWarning(
                "Pillar body for project keyword \"{Keyword}\" is {Count} words (target max {Maximum}) — no trim pass, single attempt only; saving anyway.",
                context.TargetKeyword,
                wordCount,
                ContentLengthTargets.PillarTargetMaxWords);
        }

        document = ContentDocumentText.AssignSectionIds(document);
        document = ContentDocumentText.AppendChildSectionReferences(document);
        document = ContentDocumentText.AppendSectionToc(document);
        wordCount = ContentDocumentText.CountWords(document);

        var articleUrl = CombineUrl(context.ArticleBaseUrl, context.Department, articleRow.Slug);
        var placeholderBlogUrl = CombineUrl(context.BlogBaseUrl, context.Department, $"{articleRow.Slug}-blog");

        var now = DateTime.UtcNow;
        var articleMetadata = new ContentMetadata(
            bodyMetadata.Title, bodyMetadata.MetaDescription, context.AuthorName, context.PublisherName,
            context.PublisherLogoUrl, articleUrl, context.PublisherLogoUrl, now, now, bodyMetadata.Keywords, wordCount);
        var softwareApplications = ToolSectionExtractor.ExtractApplications(document, bodyMetadata.SectionOutline);
        articleRow.Body = document;
        articleRow.LedeType = ledeType;
        articleRow.WordCount = wordCount;
        articleRow.JsonLdSchema = _articleSchemaBuilder.Build(articleMetadata, placeholderBlogUrl, softwareApplications);
        articleRow.RelatedArticleUrl = placeholderBlogUrl;
        articleRow.GeneratedByProvider = provider.ProviderType;
        articleRow.GeneratedByModel = ResolveModelName(project.PreferredProvider);

        var summaryVariants = await GenerateSummaryVariantsAsync(
            provider, context, bodyMetadata.Title, document, bodyMetadata.MetaDescription, "pillar", cancellationToken);
        articleRow.Summary = summaryVariants.Summary;
        articleRow.MainSummary = summaryVariants.MainSummary;
        articleRow.HeroSummary = summaryVariants.HeroSummary;
        articleRow.HomeSummary = summaryVariants.HomeSummary;
        articleRow.BlogSummary = summaryVariants.BlogSummary;
        articleRow.AdvertisingSummary = summaryVariants.AdvertisingSummary;

        await SaveProjectAsync(project, ProjectStatus.ReadyForGeneration, cancellationToken);
        return Assemble(project);
    }

    public async Task<GeneratedContentSet> GeneratePillarAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await GeneratePillarPlanAsync(projectId, cancellationToken);
        await GeneratePillarBodyAsync(projectId, revisionNotes: null, cancellationToken);
        return await GenerateToolPagesAsync(projectId, revisionNotes: null, cancellationToken: cancellationToken);
    }

    public async Task<GeneratedContentSet> GenerateToolPagesAsync(
        Guid projectId, string? revisionNotes = null, IReadOnlySet<string>? toolSlugsToRegenerate = null, CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectForGenerationAsync(projectId, cancellationToken);
        var articleRow = RequireCompletePillar(project);
        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);
        var metadata = ToMetadataDraft(articleRow);
        var articleUrl = CombineUrl(context.ArticleBaseUrl, context.Department, articleRow.Slug);

        _logger.LogInformation("Generating tool pages for project {ProjectId} via {Provider}", projectId, provider.ProviderType);

        var generation = await _toolPageGenerator.GenerateToolPagesAsync(
            project,
            articleRow,
            metadata,
            context,
            provider,
            articleUrl,
            revisionNotes,
            toolSlugsToRegenerate,
            cancellationToken);

        if (generation.Outcome != ToolGenerationOutcome.Success)
        {
            _logger.LogWarning(
                "Tool page generation for project {ProjectId} produced no tools: {Outcome}",
                projectId, generation.Outcome);
        }
        else
        {
            // Only remove the rows we're actually replacing, and only now that generation has
            // succeeded — a failed/retried-out run must never destroy tool pages that were already
            // generated successfully in a prior run. A full run (no slug filter) still replaces the
            // entire existing ToolPost set, same as before; a targeted rewrite only replaces the
            // slug(s) it regenerated.
            var newSlugs = generation.ToolPosts.Select(r => r.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var toRemove = project.GeneratedContents
                .Where(c => c.ContentType == GeneratedContentType.ToolPost && newSlugs.Contains(c.Slug))
                .ToList();
            foreach (var row in toRemove)
            {
                project.GeneratedContents.Remove(row);
            }

            foreach (var toolRow in generation.ToolPosts)
            {
                await AddContentAsync(project, provider.ProviderType, toolRow, cancellationToken);
            }

            // Tool pages (and their real slugs/URLs) didn't exist when the pillar's JSON+LD was first
            // built in GeneratePillarBodyAsync, so its SoftwareApplication entries had no "url". Now
            // that ToolPageGenerator has injected real hrefs into articleRow.Body's Tools section,
            // rebuild it so the embedded schema doesn't silently stay stale if the blog step never runs.
            var now = DateTime.UtcNow;
            var articleMetadata = new ContentMetadata(
                metadata.Title, metadata.MetaDescription, context.AuthorName, context.PublisherName,
                context.PublisherLogoUrl, articleUrl, context.PublisherLogoUrl, now, now, metadata.Keywords, articleRow.WordCount);
            var softwareApplications = ToolSectionExtractor.ExtractApplications(articleRow.Body, metadata.SectionOutline);
            articleRow.JsonLdSchema = _articleSchemaBuilder.Build(
                articleMetadata, articleRow.RelatedArticleUrl ?? string.Empty, softwareApplications);
        }

        await SaveProjectAsync(project, ProjectStatus.ReadyForGeneration, cancellationToken);
        return Assemble(project);
    }

    public async Task<GeneratedContentSet> GenerateBlogAsync(Guid projectId, string? revisionNotes = null, CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectForGenerationAsync(projectId, cancellationToken);
        var articleRow = RequireCompletePillar(project);

        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);
        var article = GeneratedContentSetAssembler.ToArticleDraft(articleRow);
        var articleUrl = CombineUrl(context.ArticleBaseUrl, context.Department, articleRow.Slug);

        _logger.LogInformation("Generating blog content for project {ProjectId} via {Provider}", projectId, provider.ProviderType);

        var (blogDraft, ledeType) = await GenerateBlogDraftAsync(provider, context, article, revisionNotes, cancellationToken);
        var blogSlug = SlugHelper.Slugify(blogDraft.Title);
        var blogUrl = CombineUrl(context.BlogBaseUrl, context.Department, blogSlug);

        // The model was explicitly told not to write this link — only we know the real pillar URL,
        // so it's assigned here as a field write on already-parsed data, never a guessed-at href.
        var blog = blogDraft with
        {
            Body = ContentDocumentText.AppendClosingLink(
                blogDraft.Body, "Read the full technical guide for implementation depth", articleUrl),
        };

        var now = DateTime.UtcNow;
        var blogMetadata = new ContentMetadata(
            blog.Title, blog.MetaDescription, context.AuthorName, context.PublisherName,
            context.PublisherLogoUrl, blogUrl, context.PublisherLogoUrl, now, now, blog.Keywords, blog.WordCount);
        var blogJsonLd = _blogSchemaBuilder.Build(blogMetadata, articleUrl);

        var articleMetadata = new ContentMetadata(
            article.Title, article.MetaDescription, context.AuthorName, context.PublisherName,
            context.PublisherLogoUrl, articleUrl, context.PublisherLogoUrl, now, now, article.Keywords, article.WordCount);
        var softwareApplications = ToolSectionExtractor.ExtractApplications(articleRow.Body, article.SectionOutline);
        articleRow.JsonLdSchema = _articleSchemaBuilder.Build(articleMetadata, blogUrl, softwareApplications);
        articleRow.RelatedArticleUrl = blogUrl;

        var summaryVariants = await GenerateSummaryVariantsAsync(
            provider, context, blog.Title, blog.Body, blog.MetaDescription, "blog", cancellationToken);

        // Only remove the existing BlogPost row now that generation has fully succeeded — a
        // failed/retried-out run must never destroy a blog that was already generated successfully
        // in a prior run (see GenerateImagePromptsAsync's identical fix for the same bug class).
        RemoveGeneratedContents(project, GeneratedContentType.BlogPost);

        await AddContentAsync(project, provider.ProviderType, new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.BlogPost,
            Title = blog.Title,
            Slug = blogSlug,
            MetaDescription = blog.MetaDescription,
            Keywords = blog.Keywords,
            WordCount = blog.WordCount,
            SectionOutline = blog.SectionOutline,
            Body = blog.Body,
            LedeType = ledeType,
            JsonLdSchema = blogJsonLd,
            RelatedArticleUrl = articleUrl,
            Summary = summaryVariants.Summary,
            MainSummary = summaryVariants.MainSummary,
            HeroSummary = summaryVariants.HeroSummary,
            HomeSummary = summaryVariants.HomeSummary,
            BlogSummary = summaryVariants.BlogSummary,
            AdvertisingSummary = summaryVariants.AdvertisingSummary,
            GeneratedByProvider = provider.ProviderType,
            GeneratedByModel = ResolveModelName(project.PreferredProvider)
        }, cancellationToken);

        await SaveProjectAsync(project, ProjectStatus.ReadyForGeneration, cancellationToken);
        return Assemble(project);
    }

    public async Task<GeneratedContentSet> GenerateSocialAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectForGenerationAsync(projectId, cancellationToken);
        var articleRow = RequireCompletePillar(project);

        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);
        var article = GeneratedContentSetAssembler.ToArticleDraft(articleRow);
        var articleUrl = CombineUrl(context.ArticleBaseUrl, context.Department, articleRow.Slug);

        _logger.LogInformation("Generating social content for project {ProjectId} via {Provider}", projectId, provider.ProviderType);

        RemoveGeneratedContents(project, GeneratedContentType.SocialFacebook, GeneratedContentType.SocialLinkedIn);

        var facebook = await GenerateSocialPostAsync(provider, context, article, articleUrl, "Facebook", cancellationToken);
        var linkedIn = await GenerateSocialPostAsync(provider, context, article, articleUrl, "LinkedIn", cancellationToken);

        await AddContentAsync(project, provider.ProviderType, new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.SocialFacebook,
            Title = $"{article.Title} (Facebook)",
            Slug = $"{articleRow.Slug}-facebook",
            Body = ContentDocumentText.FromPlainText(facebook.Text),
            RelatedArticleUrl = articleUrl,
            GeneratedByProvider = provider.ProviderType,
            GeneratedByModel = ResolveModelName(project.PreferredProvider)
        }, cancellationToken);

        await AddContentAsync(project, provider.ProviderType, new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.SocialLinkedIn,
            Title = $"{article.Title} (LinkedIn)",
            Slug = $"{articleRow.Slug}-linkedin",
            Body = ContentDocumentText.FromPlainText(linkedIn.Text),
            RelatedArticleUrl = articleUrl,
            GeneratedByProvider = provider.ProviderType,
            GeneratedByModel = ResolveModelName(project.PreferredProvider)
        }, cancellationToken);

        await SaveProjectAsync(project, ProjectStatus.Completed, cancellationToken);
        return Assemble(project);
    }

    public async Task<GeneratedContentSet> GenerateColdOutreachAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectForGenerationAsync(projectId, cancellationToken);
        var articleRow = RequireCompletePillar(project);

        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);
        var article = GeneratedContentSetAssembler.ToArticleDraft(articleRow);
        var articleUrl = CombineUrl(context.ArticleBaseUrl, context.Department, articleRow.Slug);

        _logger.LogInformation("Generating cold outreach email for project {ProjectId} via {Provider}", projectId, provider.ProviderType);

        RemoveGeneratedContents(project, GeneratedContentType.EmailColdOutreach);

        var result = await provider.CompleteAsync(
            _promptBuilder.BuildColdOutreachPrompt(context, article, articleUrl),
            cancellationToken);
        var draft = LlmResponseJsonParser.ParseColdOutreach(result.Content, "cold outreach email");
        var wordCount = draft.BodyText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        if (wordCount < ContentLengthTargets.EmailColdOutreachMinWords
            || wordCount > ContentLengthTargets.EmailColdOutreachMaxWords)
        {
            _logger.LogWarning(
                "Cold outreach body for project {ProjectId} is {Count} words (target {Minimum}-{Maximum}) — saving anyway.",
                projectId,
                wordCount,
                ContentLengthTargets.EmailColdOutreachMinWords,
                ContentLengthTargets.EmailColdOutreachMaxWords);
        }

        await AddContentAsync(project, provider.ProviderType, new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.EmailColdOutreach,
            Title = draft.Subject,
            Slug = $"{articleRow.Slug}-cold-outreach",
            Body = ContentDocumentText.FromPlainText(draft.BodyText),
            MetaDescription = draft.CtaLabel,
            RelatedArticleUrl = articleUrl,
            WordCount = wordCount,
            GeneratedByProvider = provider.ProviderType,
            GeneratedByModel = ResolveModelName(project.PreferredProvider)
        }, cancellationToken);

        await SaveProjectAsync(project, ProjectStatus.Completed, cancellationToken);
        return Assemble(project);
    }

    public async Task<GeneratedContentSet> GenerateImagePromptsAsync(
        Guid projectId, IReadOnlySet<string>? sectionHeadingsToTest = null, CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectForGenerationAsync(projectId, cancellationToken);
        var articleRow = RequireCompletePillar(project);
        var blogRow = RequireCompleteBlog(project);

        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);
        var article = GeneratedContentSetAssembler.ToArticleDraft(articleRow);
        var blog = GeneratedContentSetAssembler.ToBlogDraft(blogRow);
        var articleUrl = CombineUrl(context.ArticleBaseUrl, context.Department, articleRow.Slug);
        var blogUrl = CombineUrl(context.BlogBaseUrl, context.Department, blogRow.Slug);

        var toolTitles = project.GeneratedContents
            .Where(c => c.ContentType == GeneratedContentType.ToolPost)
            .OrderBy(c => c.SourceAppOrder ?? int.MaxValue)
            .Select(c => string.IsNullOrWhiteSpace(c.DisplayTitle) ? c.Title : c.DisplayTitle!)
            .ToList();

        var allSections = ContentDocumentText.BuildSectionTargets(
            articleRow.DisplayTitle ?? articleRow.Title,
            articleRow.Body,
            blogRow.DisplayTitle ?? blogRow.Title,
            blogRow.Body,
            toolTitles);
        if (allSections.Count == 0)
        {
            throw new ContentGenerationException(
                "Pillar and blog must each include at least one top-level section before generating image prompts.");
        }

        // Scoped to a subset (e.g. only the sections a prior run failed to produce) when
        // sectionHeadingsToTest is supplied — mirrors ReviewLoopService's toolSlugToTest pattern.
        // A full re-run (no filter) still regenerates every section, same as before.
        var sections = sectionHeadingsToTest is null or { Count: 0 }
            ? allSections
            : allSections.Where(s => sectionHeadingsToTest.Contains(s.Heading, StringComparer.OrdinalIgnoreCase)).ToList();
        if (sections.Count == 0)
        {
            throw new ContentGenerationException(
                "None of the requested section headings match the pillar/blog/tool outline.");
        }

        _logger.LogInformation(
            "Generating {SectionCount} section image prompts for project {ProjectId} via {Provider}",
            sections.Count,
            projectId,
            provider.ProviderType);

        var result = await provider.CompleteAsync(
            _promptBuilder.BuildSectionImagePromptsPrompt(
                context, article, blog, articleUrl, blogUrl, sections),
            cancellationToken);
        var draft = LlmResponseJsonParser.ParseSectionImagePrompts(result.Content, sections, "image prompts");

        // Only remove existing rows for the sections we're about to replace, and only now that
        // generation has actually succeeded — a failed/retried-out run must never destroy image
        // prompts that were already generated successfully in a prior run.
        RemoveImagePromptRowsForSections(project, articleRow.Slug, sections);

        foreach (var section in draft.Sections)
        {
            await AddSectionImagePromptRowAsync(
                project,
                provider.ProviderType,
                articleRow.Slug,
                articleUrl,
                section,
                cancellationToken);
        }

        await SaveProjectAsync(project, ProjectStatus.Completed, cancellationToken);
        return Assemble(project);
    }

    public async Task<GeneratedContentSet> GenerateAllAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await GeneratePillarPlanAsync(projectId, cancellationToken);
        await GeneratePillarBodyAsync(projectId, revisionNotes: null, cancellationToken);
        await GenerateToolPagesAsync(projectId, revisionNotes: null, cancellationToken: cancellationToken);
        await GenerateBlogAsync(projectId, revisionNotes: null, cancellationToken);
        await GenerateSocialAsync(projectId, cancellationToken);
        await GenerateColdOutreachAsync(projectId, cancellationToken);
        return await GenerateImagePromptsAsync(projectId, sectionHeadingsToTest: null, cancellationToken);
    }

    private async Task<Project> LoadProjectForGenerationAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await _projectStore.GetAsync(projectId, cancellationToken)
            ?? throw new ContentGenerationException($"Project {projectId} was not found.");

        if (project.CrawledSite is null)
        {
            throw new ContentGenerationException("Project has not been crawled yet. Run the crawl step before generating content.");
        }

        if (project.KeywordSources.Count == 0)
        {
            throw new ContentGenerationException("Upload at least one research input before generating content.");
        }

        return project;
    }

    private GeneratedContent RequireGeneratedContent(Project project, GeneratedContentType type, string message) =>
        project.GeneratedContents.FirstOrDefault(c => c.ContentType == type)
        ?? throw new ContentGenerationException(message);

    private GeneratedContent RequireCompletePillar(Project project)
    {
        var row = RequireGeneratedContent(project, GeneratedContentType.TechnicalArticle,
            "Generate the pillar plan and body (Steps 1–2) before continuing.");

        if (row.Body is null || row.WordCount < 200)
        {
            throw new ContentGenerationException("Generate the pillar body (Step 2) before continuing.");
        }

        return row;
    }

    private static ArticleMetadataDraft ToMetadataDraft(GeneratedContent row) => new(
        row.Title,
        row.MetaDescription ?? string.Empty,
        row.Keywords,
        row.SectionOutline);

    private void RemoveGeneratedContents(Project project, params GeneratedContentType[] types)
    {
        var toRemove = project.GeneratedContents.Where(c => types.Contains(c.ContentType)).ToList();
        if (toRemove.Count == 0)
        {
            return;
        }

        foreach (var row in toRemove)
        {
            project.GeneratedContents.Remove(row);
        }
    }

    private Task AddContentAsync(
        Project project,
        LlmProviderType providerType,
        GeneratedContent row,
        CancellationToken cancellationToken)
    {
        project.GeneratedContents.Add(row);
        return Task.CompletedTask;
    }

    private GeneratedContent RequireCompleteBlog(Project project)
    {
        var row = RequireGeneratedContent(project, GeneratedContentType.BlogPost,
            "Generate the blog (Step 3) before image prompts.");

        if (row.Body is null || row.WordCount < 200)
        {
            throw new ContentGenerationException("Generate the blog (Step 3) before image prompts.");
        }

        return row;
    }

    private async Task AddSectionImagePromptRowAsync(
        Project project,
        LlmProviderType providerType,
        string articleSlug,
        string articleUrl,
        ImagePromptSectionDraft item,
        CancellationToken cancellationToken)
    {
        var (contentType, slug) = ImagePromptSectionIdentity(articleSlug, item.SourceType, item.Heading);
        var wordCount = item.Prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        await AddContentAsync(project, providerType, new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = contentType,
            Title = item.Heading,
            Slug = slug,
            Body = ContentDocumentText.FromPlainText(item.Prompt),
            MetaDescription = ImagePromptMetadata.Serialize(item),
            RelatedArticleUrl = articleUrl,
            WordCount = wordCount,
            GeneratedByProvider = providerType,
            GeneratedByModel = ResolveModelName(project.PreferredProvider),
        }, cancellationToken);
    }

    /// <summary>Same (contentType, slug) derivation used both when writing a new image-prompt row
    /// and when identifying which existing row a regenerated target should replace.</summary>
    private static (GeneratedContentType ContentType, string Slug) ImagePromptSectionIdentity(
        string articleSlug, string sourceType, string heading)
    {
        var isPillarHero = sourceType.Equals("pillar-hero", StringComparison.OrdinalIgnoreCase);
        var isBlogHero = sourceType.Equals("blog-hero", StringComparison.OrdinalIgnoreCase);
        var headingSlug = SlugHelper.Slugify(heading);

        var contentType = isPillarHero ? GeneratedContentType.ImagePromptPillarFigure
            : isBlogHero ? GeneratedContentType.ImagePromptBlogFigure
            : GeneratedContentType.ImagePromptSection;
        var slug = isPillarHero || isBlogHero
            ? $"{articleSlug}-{sourceType}"
            : $"{articleSlug}-{sourceType}-h2-{headingSlug}";

        return (contentType, slug);
    }

    /// <summary>Removes only the existing image-prompt rows matching the given targets, leaving
    /// every other image-prompt row (from sections not currently being regenerated) untouched.</summary>
    private void RemoveImagePromptRowsForSections(Project project, string articleSlug, IReadOnlyList<ImagePromptSectionTarget> sections)
    {
        var slugsToReplace = sections
            .Select(s => ImagePromptSectionIdentity(articleSlug, s.SourceType, s.Heading).Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toRemove = project.GeneratedContents
            .Where(c => c.ContentType is GeneratedContentType.ImagePromptPillarFigure
                or GeneratedContentType.ImagePromptBlogFigure
                or GeneratedContentType.ImagePromptSection)
            .Where(c => slugsToReplace.Contains(c.Slug))
            .ToList();

        foreach (var row in toRemove)
        {
            project.GeneratedContents.Remove(row);
        }
    }

    private async Task SaveProjectAsync(Project project, ProjectStatus status, CancellationToken cancellationToken)
    {
        project.Status = status;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await _projectStore.SaveAsync(project, cancellationToken);
    }

    private GeneratedContentSet Assemble(Project project) =>
        GeneratedContentSetAssembler.Assemble(
            project, project.Department, _companyProfile.ArticleBaseUrl, _companyProfile.BlogBaseUrl, _companyProfile.ToolBaseUrl);

    public ProjectGenerationContext BuildContext(Project project)
    {
        var crawl = project.CrawledSite!;
        var keywordSummaries = project.KeywordSources
            .Where(k => k.Category != KeywordSourceCategory.PeopleAlsoAsk)
            .Select(k => new KeywordSourceSummary(
                k.Category,
                k.ExtractedTitle,
                k.OriginalFileName,
                k.ExtractedHeadings,
                k.ExtractedParagraphs))
            .ToList();

        var paaQuestions = project.KeywordSources
            .Where(k => k.Category == KeywordSourceCategory.PeopleAlsoAsk)
            .SelectMany(k => k.ExtractedQuestions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPeopleAlsoAskQuestions)
            .ToList();

        if (paaQuestions.Count == MaxPeopleAlsoAskQuestions)
        {
            var totalPaa = project.KeywordSources
                .Where(k => k.Category == KeywordSourceCategory.PeopleAlsoAsk)
                .SelectMany(k => k.ExtractedQuestions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (totalPaa > MaxPeopleAlsoAskQuestions)
            {
                _logger.LogWarning(
                    "Project {ProjectId} has {Total} PAA questions; using first {Cap} for generation.",
                    project.Id, totalPaa, MaxPeopleAlsoAskQuestions);
            }
        }

        var jsonLdSummary = JsonLdSummaryFormatter.Format(_jsonLdParser.Summarize(crawl.JsonLdBlocks));
        if (!string.IsNullOrWhiteSpace(jsonLdSummary))
        {
            _logger.LogInformation(
                "Including parsed JSON+LD structured summary for project {ProjectId} ({BlockCount} raw blocks)",
                project.Id,
                crawl.JsonLdBlocks.Count);
        }

        return new ProjectGenerationContext(
            ProjectName: project.Name,
            ProjectUrl: project.ProjectUrl,
            TargetKeyword: project.TargetKeyword,
            Department: project.Department,
            SiteName: crawl.SiteName,
            DetectedTone: crawl.DetectedTone,
            DetectedFocus: crawl.DetectedFocus,
            CrawledHeadings: crawl.Headings,
            CrawledParagraphs: crawl.Paragraphs,
            JsonLdStructuredSummary: string.IsNullOrWhiteSpace(jsonLdSummary) ? null : jsonLdSummary,
            KeywordSources: keywordSummaries,
            PeopleAlsoAskQuestions: paaQuestions,
            PublisherName: _companyProfile.PublisherName,
            PublisherLogoUrl: _companyProfile.PublisherLogoUrl,
            AuthorName: _companyProfile.AuthorName,
            ArticleBaseUrl: _companyProfile.ArticleBaseUrl,
            BlogBaseUrl: _companyProfile.BlogBaseUrl,
            ToolBaseUrl: _companyProfile.ToolBaseUrl,
            ImplementerPositioning: _companyProfile.ImplementerPositioning,
            Provider: project.PreferredProvider,
            UseExactKeywordAsTitle: project.UseExactKeywordAsTitle,
            DesiredHeadings: project.Notes
                ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList() ?? []);
    }

    private static string CombineUrl(string baseUrl, string department, string slug) =>
        $"{baseUrl.TrimEnd('/')}/{department}/{slug}";

    private static string ResolveModelName(LlmProviderType provider) => provider switch
    {
        LlmProviderType.LmStudio => "lm-studio-local",
        LlmProviderType.OpenAi => "openai",
        LlmProviderType.Anthropic => "anthropic",
        _ => "unknown"
    };

    private async Task<ArticleMetadataDraft> GenerateArticleMetadataAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        CancellationToken cancellationToken)
    {
        var metadataResult = await provider.CompleteAsync(
            _promptBuilder.BuildArticleMetadataPrompt(context),
            cancellationToken);
        var metadata = NormalizeMetadata(ParseJson<ArticleMetadataDraft>(metadataResult.Content, "TechnicalArticle metadata"));
        metadata = SanitizePlanMetadata(metadata, context.PeopleAlsoAskQuestions, context.TargetKeyword, context.DesiredHeadings);
        metadata = PillarPlanMetadataNormalizer.Normalize(metadata, context.TargetKeyword);
        return context.UseExactKeywordAsTitle ? metadata with { Title = context.TargetKeyword } : metadata;
    }

    private async Task<(ContentDocument Document, LedeType LedeType)> GenerateArticleBodyAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions,
        bool isRegeneration,
        string? revisionNotes,
        string? existingLedeHeading,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating pillar lede");
        var ledeResult = await provider.CompleteAsync(
            _promptBuilder.BuildArticleLedePrompt(context, metadata, revisionNotes, existingLedeHeading),
            cancellationToken);
        var (lede, ledeType) = LlmResponseJsonParser.ParseLede(ledeResult.Content, "TechnicalArticle lede");

        var mainSections = metadata.SectionOutline
            .Where(s => !PillarOutlineNormalizer.IsFaqSectionTitle(s))
            .ToList();

        var sections = new List<Section>();

        for (var i = 0; i < mainSections.Count; i++)
        {
            var heading = mainSections[i];
            _logger.LogInformation(
                "Generating pillar section {Index}/{Total}: {Heading}",
                i + 1, mainSections.Count, heading);

            Section section;
            if (PillarOutlineNormalizer.IsToolsSection(heading))
            {
                section = await GenerateToolsSectionAsync(
                    provider, context, metadata, heading, isRegeneration, revisionNotes, cancellationToken);
            }
            else
            {
                var sectionResult = await provider.CompleteAsync(
                    _promptBuilder.BuildArticleSectionPrompt(
                        context, metadata, heading, i, mainSections.Count, metadata.SectionOutline, isRegeneration, revisionNotes),
                    cancellationToken);
                section = LlmResponseJsonParser.ParseSection(sectionResult.Content, "h2", $"TechnicalArticle section '{heading}'");
            }

            sections.Add(section);

            var sectionMin = PillarOutlineNormalizer.IsToolsSection(heading)
                ? (int)(ContentLengthTargets.PillarToolsSectionMinWords * 0.85)
                : (int)(ContentLengthTargets.PillarSectionMinWords * 0.85);
            var sectionWords = ContentDocumentText.CountWords(section);
            if (sectionWords < sectionMin)
            {
                _logger.LogWarning(
                    "Pillar section \"{Heading}\" is {Count} words (soft minimum {Minimum}) — no retry, single attempt only.",
                    heading,
                    sectionWords,
                    sectionMin);
            }
        }

        if (faqQuestions.Count > 0)
        {
            _logger.LogInformation("Generating pillar FAQ section ({Count} questions)", faqQuestions.Count);

            var faqResult = await provider.CompleteAsync(
                _promptBuilder.BuildArticleFaqSectionPrompt(context, metadata, faqQuestions, isRegeneration, revisionNotes),
                cancellationToken);

            sections.Add(LlmResponseJsonParser.ParseSection(faqResult.Content, "h2", "TechnicalArticle FAQ section"));
        }

        return (new ContentDocument(lede, sections), ledeType);
    }

    private async Task<Section> GenerateToolsSectionAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        bool isRegeneration,
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating Tools platform list for \"{Heading}\"", toolsSectionHeading);
        var listResult = await provider.CompleteAsync(
            _promptBuilder.BuildToolsPlatformListPrompt(
                context, metadata, toolsSectionHeading, isRegeneration, revisionNotes),
            cancellationToken);
        var platforms = NormalizeToolsPlatformList(
            ParseJson<ToolsPlatformListDraft>(listResult.Content, "TechnicalArticle Tools platform list").Platforms,
            toolsSectionHeading);

        var children = new List<Section>();
        for (var i = 0; i < platforms.Count; i++)
        {
            var platformName = platforms[i];
            _logger.LogInformation(
                "Generating Tools platform {Index}/{Total}: {Platform}",
                i + 1, platforms.Count, platformName);

            var platformResult = await provider.CompleteAsync(
                _promptBuilder.BuildToolsPlatformChildPrompt(
                    context, metadata, toolsSectionHeading, platformName, platforms, i, platforms.Count,
                    isRegeneration, revisionNotes),
                cancellationToken);

            var child = LlmResponseJsonParser.ParseSection(
                platformResult.Content, "h3", $"TechnicalArticle Tools platform '{platformName}'");
            // Keep extractor/slug matching stable even if the model paraphrases the heading.
            children.Add(child with { Heading = platformName });
        }

        return new Section(
            Tag: "h2",
            Heading: toolsSectionHeading,
            Paragraphs: [],
            Href: null,
            Children: children);
    }

    private static List<string> NormalizeToolsPlatformList(IReadOnlyList<string>? raw, string toolsSectionHeading)
    {
        var platforms = (raw ?? [])
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (platforms.Count < 4)
        {
            throw new ContentGenerationException(
                $"Tools section '{toolsSectionHeading}' platform list returned {platforms.Count} platforms; need 4–5 real products.");
        }

        return platforms;
    }

    private static ArticleMetadataDraft SanitizePlanMetadata(
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> paaQuestions,
        string targetKeyword,
        IReadOnlyList<string>? desiredHeadings = null)
    {
        var (mainOutline, _) = PillarOutlineNormalizer.Sanitize(metadata.SectionOutline, paaQuestions, targetKeyword, desiredHeadings);
        return metadata with { SectionOutline = mainOutline };
    }

    private static (ArticleMetadataDraft Metadata, List<string> FaqQuestions) PrepareBodyInput(
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> paaQuestions,
        string targetKeyword,
        IReadOnlyList<string>? desiredHeadings = null)
    {
        var (mainOutline, faqQuestions) = PillarOutlineNormalizer.Sanitize(metadata.SectionOutline, paaQuestions, targetKeyword, desiredHeadings);
        return (metadata with { SectionOutline = mainOutline }, faqQuestions);
    }

    private static ArticleMetadataDraft NormalizeMetadata(ArticleMetadataDraft metadata) => metadata with
    {
        Keywords = metadata.Keywords ?? new List<string>(),
        SectionOutline = metadata.SectionOutline ?? new List<string>()
    };

    private async Task<(BlogDraft Draft, LedeType LedeType)> GenerateBlogDraftAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleDraft article,
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        var metadataResult = await provider.CompleteAsync(
            _promptBuilder.BuildBlogMetadataPrompt(context, article),
            cancellationToken);
        var metadata = NormalizeBlogMetadata(ParseJson<BlogMetadataDraft>(metadataResult.Content, "BlogPosting metadata"));
        metadata = EnsureBlogSectionOutline(metadata);

        _logger.LogInformation("Generating blog lede");
        var ledeResult = await provider.CompleteAsync(
            _promptBuilder.BuildBlogLedePrompt(context, article, metadata),
            cancellationToken);
        var (lede, ledeType) = LlmResponseJsonParser.ParseLede(ledeResult.Content, "BlogPosting lede");

        var sections = await GenerateBlogBodyAsync(provider, context, article, metadata, revisionNotes, cancellationToken);
        var wordCount = ContentDocumentText.CountWords(sections);

        if (wordCount < ContentLengthTargets.BlogMinWords)
        {
            _logger.LogWarning(
                "Blog draft for project keyword \"{Keyword}\" is {Count} words (minimum {Minimum}) — no expansion pass, single attempt only.",
                context.TargetKeyword,
                wordCount,
                ContentLengthTargets.BlogMinWords);
        }
        else if (wordCount > ContentLengthTargets.BlogTargetMaxWords)
        {
            _logger.LogWarning(
                "Blog draft for project keyword \"{Keyword}\" is {Count} words (target max {Maximum}) — no trim pass, single attempt only; saving anyway.",
                context.TargetKeyword,
                wordCount,
                ContentLengthTargets.BlogTargetMaxWords);
        }

        var draft = new BlogDraft(
            metadata.Title,
            metadata.MetaDescription,
            new ContentDocument(lede, sections),
            metadata.Keywords,
            wordCount,
            metadata.SectionOutline);

        return (draft, ledeType);
    }

    private async Task<List<Section>> GenerateBlogBodyAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleDraft article,
        BlogMetadataDraft metadata,
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        var sections = metadata.SectionOutline.Count > 0
            ? metadata.SectionOutline
            :
            [
                "Why this matters now",
                "What the data shows",
                "Key takeaways from the pillar",
                "Practical steps you can take today",
                "Common mistakes to avoid",
                "What to read next"
            ];

        var parts = new List<Section>();

        for (var i = 0; i < sections.Count; i++)
        {
            var heading = sections[i];
            _logger.LogInformation(
                "Generating blog section {Index}/{Total}: {Heading}",
                i + 1,
                sections.Count,
                heading);

            var section = await GenerateBlogSectionAsync(
                provider, context, article, metadata, heading, i, sections.Count, revisionNotes, cancellationToken);
            parts.Add(section);
        }

        return parts;
    }

    private async Task<Section> GenerateBlogSectionAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleDraft article,
        BlogMetadataDraft metadata,
        string heading,
        int sectionIndex,
        int totalSections,
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        var sectionResult = await provider.CompleteAsync(
            _promptBuilder.BuildBlogSectionPrompt(context, article, metadata, heading, sectionIndex, totalSections, revisionNotes),
            cancellationToken);

        var section = LlmResponseJsonParser.ParseSection(sectionResult.Content, "h2", $"BlogPosting section '{heading}'");

        var sectionMin = (int)(ContentLengthTargets.BlogSectionMinWords * 0.85);
        if (ContentDocumentText.CountWords(section) < sectionMin)
        {
            _logger.LogWarning(
                "Blog section \"{Heading}\" is under {Minimum} words — no retry, single attempt only.",
                heading,
                sectionMin);
        }

        return section;
    }

    private static BlogMetadataDraft EnsureBlogSectionOutline(BlogMetadataDraft metadata)
    {
        var outline = metadata.SectionOutline?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];

        while (outline.Count < ContentLengthTargets.BlogSectionCountMin)
        {
            outline.Add(outline.Count switch
            {
                0 => "Why this matters now",
                1 => "What the data shows",
                2 => "Key takeaways you can use",
                3 => "Practical steps to implement",
                _ => "What to do next"
            });
        }

        return metadata with { SectionOutline = outline };
    }

    private static BlogMetadataDraft NormalizeBlogMetadata(BlogMetadataDraft metadata) => metadata with
    {
        Keywords = metadata.Keywords ?? new List<string>(),
        SectionOutline = metadata.SectionOutline ?? new List<string>()
    };

    private async Task<SocialPostDraft> GenerateSocialPostAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleDraft article,
        string articleUrl,
        string platform,
        CancellationToken cancellationToken)
    {
        var result = await provider.CompleteAsync(
            _promptBuilder.BuildSocialPrompt(context, article, platform, articleUrl),
            cancellationToken);

        var text = LlmResponseJsonParser.ParseSocialText(result.Content, articleUrl, $"{platform} post");
        return new SocialPostDraft(platform, text);
    }

    private async Task<ArticleMetadataDraft> ApplyMetaRevisionNotesAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        GeneratedContent articleRow,
        ArticleMetadataDraft metadata,
        string revisionNotes,
        CancellationToken cancellationToken)
    {
        var request = _promptBuilder.BuildArticleMetaRevisionPrompt(
            context, metadata.Title, metadata.MetaDescription, revisionNotes);
        if (request is null)
        {
            return metadata;
        }

        _logger.LogInformation("Applying meta-description revision notes for project keyword \"{Keyword}\"", context.TargetKeyword);
        var result = await provider.CompleteAsync(request, cancellationToken);
        var revised = ParseJson<MetaRevisionDraft>(result.Content, "TechnicalArticle meta revision");

        var title = string.IsNullOrWhiteSpace(revised.Title) ? metadata.Title : revised.Title.Trim();
        var metaDescription = string.IsNullOrWhiteSpace(revised.MetaDescription)
            ? metadata.MetaDescription
            : revised.MetaDescription.Trim();

        articleRow.Title = title;
        articleRow.MetaDescription = metaDescription;

        return metadata with { Title = title, MetaDescription = metaDescription };
    }

    private async Task<SummaryVariantsDraft> GenerateSummaryVariantsAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        string title,
        ContentDocument body,
        string? metaDescription,
        string contentTypeLabel,
        CancellationToken cancellationToken)
    {
        var result = await provider.CompleteAsync(
            _promptBuilder.BuildSummaryVariantsPrompt(context, title, body, metaDescription, contentTypeLabel),
            cancellationToken);

        return LlmResponseJsonParser.Parse<SummaryVariantsDraft>(result.Content, "summary variants");
    }

    private T ParseJson<T>(string rawContent, string label)
    {
        try
        {
            return LlmResponseJsonParser.Parse<T>(rawContent, label);
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogError(ex, "Failed to parse {Label} JSON. Raw content: {Raw}", label, rawContent);
            throw;
        }
    }

    private sealed record MetaRevisionDraft(string? Title, string? MetaDescription);

    private sealed record ToolsPlatformListDraft(List<string>? Platforms);
}

public class CompanyProfileOptions
{
    public const string SectionName = "CompanyProfile";

    public string PublisherName { get; set; } = "Geek At Your Spot";
    public string PublisherLogoUrl { get; set; } = "https://www.geekatyourspot.com/images/GeekAtYourSpot.svg";
    public string AuthorName { get; set; } = "Geek At Your Spot Editorial Team";
    public string ArticleBaseUrl { get; set; } = "https://www.geekatyourspot.com/use-cases";
    public string BlogBaseUrl { get; set; } = "https://www.geekatyourspot.com/blog";
    public string ToolBaseUrl { get; set; } = "https://www.geekatyourspot.com/tools";

    /// <summary>Google Tag Manager container id (e.g. GTM-K5CXSQRP) injected into exported HTML
    /// head + body noscript. Empty/invalid values skip GTM entirely.</summary>
    public string GtmContainerId { get; set; } = "GTM-K5CXSQRP";

    /// <summary>How the publisher positions AI implementation services in pillar Tools sections.</summary>
    public string ImplementerPositioning { get; set; } =
        "Geek At Your Spot is an AI implementation consultancy for B2B organizations. " +
        "In every pillar Tools section, for each major platform covered, explain which client problems an AI implementer solves " +
        "(accelerated deployment, data model design, workflow configuration, custom code, autonomous agents, integration, and change management).";
}
