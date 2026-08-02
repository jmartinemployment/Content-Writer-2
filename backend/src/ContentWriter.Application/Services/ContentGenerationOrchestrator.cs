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

        articleRow.NoResearchWarning = HasNoResearchInput(context) ? BuildNoResearchWarning(context.TargetKeyword) : null;
        if (articleRow.NoResearchWarning is not null)
        {
            _logger.LogWarning(
                "Project {ProjectId} has no crawled site content, no uploaded keyword sources, and no matched Home-page Use Case — generating from keyword \"{Keyword}\" alone.",
                projectId, context.TargetKeyword);
        }

        var metadata = ToMetadataDraft(articleRow);
        var (bodyMetadata, faqQuestions) = PrepareBodyInput(metadata, context.PeopleAlsoAskQuestions, context.TargetKeyword);
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
        articleRow.Gaps = FindGaps(document, context);

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
        var pillar = TryGetCompletePillar(project);
        if (pillar is null)
        {
            return await GenerateStandaloneBlogAsync(project, revisionNotes, cancellationToken);
        }

        var articleRow = pillar;
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

    /// <summary>
    /// Content Creator path: blog without a pillar — research brief + keyword via standalone prompts.
    /// </summary>
    private async Task<GeneratedContentSet> GenerateStandaloneBlogAsync(
        Project project,
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);

        _logger.LogInformation(
            "Generating standalone blog (no pillar) for project {ProjectId} via {Provider}",
            project.Id,
            provider.ProviderType);

        var (blogDraft, ledeType) = await GenerateStandaloneBlogDraftAsync(provider, context, revisionNotes, cancellationToken);
        var blogSlug = SlugHelper.Slugify(blogDraft.Title);
        var blogUrl = CombineUrl(context.BlogBaseUrl, context.Department, blogSlug);

        var now = DateTime.UtcNow;
        var blogMetadata = new ContentMetadata(
            blogDraft.Title, blogDraft.MetaDescription, context.AuthorName, context.PublisherName,
            context.PublisherLogoUrl, blogUrl, context.PublisherLogoUrl, now, now, blogDraft.Keywords, blogDraft.WordCount);
        var blogJsonLd = _blogSchemaBuilder.Build(blogMetadata, relatedArticleUrl: string.Empty);

        var summaryVariants = await GenerateSummaryVariantsAsync(
            provider, context, blogDraft.Title, blogDraft.Body, blogDraft.MetaDescription, "blog", cancellationToken);

        RemoveGeneratedContents(project, GeneratedContentType.BlogPost);

        await AddContentAsync(project, provider.ProviderType, new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.BlogPost,
            Title = blogDraft.Title,
            Slug = blogSlug,
            MetaDescription = blogDraft.MetaDescription,
            Keywords = blogDraft.Keywords,
            WordCount = blogDraft.WordCount,
            SectionOutline = blogDraft.SectionOutline,
            Body = blogDraft.Body,
            LedeType = ledeType,
            JsonLdSchema = blogJsonLd,
            RelatedArticleUrl = null,
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

    private async Task<(BlogDraft Draft, LedeType LedeType)> GenerateStandaloneBlogDraftAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        var metadataResult = await provider.CompleteAsync(
            _promptBuilder.BuildStandaloneBlogMetadataPrompt(context),
            cancellationToken);
        var metadata = NormalizeBlogMetadata(ParseJson<BlogMetadataDraft>(metadataResult.Content, "BlogPosting metadata"));
        metadata = EnsureBlogSectionOutline(metadata);

        _logger.LogInformation("Generating standalone blog lede");
        var ledeResult = await provider.CompleteAsync(
            _promptBuilder.BuildStandaloneBlogLedePrompt(context, metadata),
            cancellationToken);
        var (lede, ledeType) = LlmResponseJsonParser.ParseLede(ledeResult.Content, "BlogPosting lede");

        _logger.LogInformation("Generating standalone blog body");
        var bodyResult = await provider.CompleteAsync(
            _promptBuilder.BuildStandaloneBlogBodyPrompt(context, metadata, revisionNotes),
            cancellationToken);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "BlogPosting body");
        var wordCount = ContentDocumentText.CountWords(sections);

        metadata = metadata with { SectionOutline = sections.Select(s => s.Heading).ToList() };

        var draft = new BlogDraft(
            metadata.Title,
            metadata.MetaDescription,
            new ContentDocument(lede, sections.ToList()),
            metadata.Keywords,
            wordCount,
            metadata.SectionOutline);

        return (draft, ledeType);
    }

    public async Task<GeneratedContentSet> GenerateSocialAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectForGenerationAsync(projectId, cancellationToken);
        var (source, sourceUrl, slugBase) = ResolveRepurposeSource(project);

        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);

        _logger.LogInformation("Generating social content for project {ProjectId} via {Provider}", projectId, provider.ProviderType);

        RemoveGeneratedContents(project, GeneratedContentType.SocialFacebook, GeneratedContentType.SocialLinkedIn);

        var facebook = await GenerateSocialPostAsync(provider, context, source, sourceUrl, "Facebook", cancellationToken);
        var linkedIn = await GenerateSocialPostAsync(provider, context, source, sourceUrl, "LinkedIn", cancellationToken);

        await AddContentAsync(project, provider.ProviderType, new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.SocialFacebook,
            Title = $"{source.Title} (Facebook)",
            Slug = $"{slugBase}-facebook",
            Body = ContentDocumentText.FromPlainText(facebook.Text),
            RelatedArticleUrl = sourceUrl,
            GeneratedByProvider = provider.ProviderType,
            GeneratedByModel = ResolveModelName(project.PreferredProvider)
        }, cancellationToken);

        await AddContentAsync(project, provider.ProviderType, new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.SocialLinkedIn,
            Title = $"{source.Title} (LinkedIn)",
            Slug = $"{slugBase}-linkedin",
            Body = ContentDocumentText.FromPlainText(linkedIn.Text),
            RelatedArticleUrl = sourceUrl,
            GeneratedByProvider = provider.ProviderType,
            GeneratedByModel = ResolveModelName(project.PreferredProvider)
        }, cancellationToken);

        await SaveProjectAsync(project, ProjectStatus.Completed, cancellationToken);
        return Assemble(project);
    }

    public async Task<GeneratedContentSet> GenerateColdOutreachAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectForGenerationAsync(projectId, cancellationToken);
        var (source, sourceUrl, _) = ResolveRepurposeSource(project);

        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);

        _logger.LogInformation("Generating cold outreach email for project {ProjectId} via {Provider}", projectId, provider.ProviderType);

        RemoveGeneratedContents(project, GeneratedContentType.EmailColdOutreach);

        var result = await provider.CompleteAsync(
            _promptBuilder.BuildColdOutreachPrompt(context, source, sourceUrl),
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
            Slug = $"{SlugHelper.Slugify(source.Title)}-cold-outreach",
            Body = ContentDocumentText.FromPlainText(draft.BodyText),
            MetaDescription = draft.CtaLabel,
            RelatedArticleUrl = sourceUrl,
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
        var articleRow = TryGetCompletePillar(project);
        var blogRow = RequireCompleteBlog(project);

        var context = BuildContext(project);
        var provider = _providerFactory.Get(project.PreferredProvider);
        var article = articleRow is null
            ? new ArticleDraft(
                Title: blogRow.Title,
                MetaDescription: blogRow.MetaDescription ?? string.Empty,
                Body: blogRow.Body ?? new ContentDocument(
                    new Section("h2", "Opening", Array.Empty<Paragraph>(), null, Array.Empty<Section>()),
                    Array.Empty<Section>()),
                Keywords: blogRow.Keywords,
                WordCount: blogRow.WordCount,
                SectionOutline: blogRow.SectionOutline)
            : GeneratedContentSetAssembler.ToArticleDraft(articleRow);
        var blog = GeneratedContentSetAssembler.ToBlogDraft(blogRow);
        var articleUrl = articleRow is null
            ? string.Empty
            : CombineUrl(context.ArticleBaseUrl, context.Department, articleRow.Slug);
        var blogUrl = CombineUrl(context.BlogBaseUrl, context.Department, blogRow.Slug);
        var slugForRows = articleRow?.Slug ?? blogRow.Slug;

        var toolTitles = project.GeneratedContents
            .Where(c => c.ContentType == GeneratedContentType.ToolPost)
            .OrderBy(c => c.SourceAppOrder ?? int.MaxValue)
            .Select(c => string.IsNullOrWhiteSpace(c.DisplayTitle) ? c.Title : c.DisplayTitle!)
            .ToList();

        var allSections = ContentDocumentText.BuildSectionTargets(
            articleRow is null ? null : (articleRow.DisplayTitle ?? articleRow.Title),
            articleRow?.Body,
            blogRow.DisplayTitle ?? blogRow.Title,
            blogRow.Body,
            toolTitles);
        if (allSections.Count == 0)
        {
            throw new ContentGenerationException(
                "Blog must include at least one top-level section before generating image prompts.");
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
        RemoveImagePromptRowsForSections(project, slugForRows, sections);

        foreach (var section in draft.Sections)
        {
            await AddSectionImagePromptRowAsync(
                project,
                provider.ProviderType,
                slugForRows,
                articleUrl.Length > 0 ? articleUrl : blogUrl,
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

    private static GeneratedContent? TryGetCompletePillar(Project project)
    {
        var row = project.GeneratedContents.FirstOrDefault(c => c.ContentType == GeneratedContentType.TechnicalArticle);
        if (row is null || row.Body is null || row.WordCount < 200)
            return null;
        return row;
    }

    /// <summary>
    /// Content Creator: social/cold outreach can source from pillar or standalone blog.
    /// </summary>
    private (ArticleDraft Source, string SourceUrl, string SlugBase) ResolveRepurposeSource(Project project)
    {
        var context = BuildContext(project);
        var pillar = TryGetCompletePillar(project);
        if (pillar is not null)
        {
            return (
                GeneratedContentSetAssembler.ToArticleDraft(pillar),
                CombineUrl(context.ArticleBaseUrl, context.Department, pillar.Slug),
                pillar.Slug);
        }

        var blog = project.GeneratedContents.FirstOrDefault(c => c.ContentType == GeneratedContentType.BlogPost)
            ?? throw new ContentGenerationException(
                "Generate a pillar body or a blog before social / cold outreach.");
        if (blog.Body is null || blog.WordCount < 100)
            throw new ContentGenerationException("Generate a complete blog (or pillar) before social / cold outreach.");

        var blogDraft = GeneratedContentSetAssembler.ToBlogDraft(blog);
        var asArticle = new ArticleDraft(
            blogDraft.Title,
            blogDraft.MetaDescription,
            blogDraft.Body,
            blogDraft.Keywords,
            blogDraft.WordCount,
            blogDraft.SectionOutline);
        return (
            asArticle,
            CombineUrl(context.BlogBaseUrl, context.Department, blog.Slug),
            blog.Slug);
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
                k.Category == KeywordSourceCategory.KeywordResult ? ClusterHeadings(k.ExtractedHeadings) : k.ExtractedHeadings,
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
                .ToList() ?? [],
            MatchedUseCase: MatchUseCase(crawl.UseCases, project.TargetKeyword));
    }

    /// <summary>Matches a project's TargetKeyword against a Home page use-case item by name — forgiving
    /// match since the keyword is typically typed directly from that item's listed name. Exact match
    /// first, then either-direction substring, so close but not identical wording still connects.</summary>
    private static UseCaseItem? MatchUseCase(List<UseCaseItem> useCases, string targetKeyword)
    {
        if (useCases.Count == 0 || string.IsNullOrWhiteSpace(targetKeyword))
        {
            return null;
        }

        var keyword = targetKeyword.Trim();

        var exact = useCases.FirstOrDefault(u => string.Equals(u.Name, keyword, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return useCases.FirstOrDefault(u =>
            u.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains(u.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Dedupes near-duplicate headings within a single keyword SERP upload by clustering
    /// them (TopicClusteringService.ClusterKeywordList) and keeping one representative heading per
    /// cluster, so overlapping phrasing of the same topic doesn't get fed into prompts as if each
    /// were distinct.</summary>
    private static List<string> ClusterHeadings(List<string> headings)
    {
        if (headings.Count <= 1)
        {
            return headings;
        }

        return TopicClusteringService.ClusterKeywordList(headings)
            .Select(c => c.PillarKeyword)
            .ToList();
    }

    /// <summary>Checks each required topic (Notes-requested subtopics, plus the matched Home-page
    /// Use Case name when present) against every heading actually in the generated document —
    /// a required topic that didn't make it in anywhere is a "gap", reported rather than silently
    /// accepted. Substring match (either direction) since the model may word a heading slightly
    /// differently than the requested topic.</summary>
    private static List<string> FindGaps(ContentDocument document, ProjectGenerationContext context)
    {
        var required = (context.DesiredHeadings ?? []).ToList();
        if (context.MatchedUseCase is { } matched)
        {
            required.Add(matched.Name);
        }

        if (required.Count == 0)
        {
            return [];
        }

        var headings = ContentDocumentText.AllHeadings(document);
        return required
            .Where(topic => !headings.Any(h =>
                h.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
                topic.Contains(h, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>True when generation is about to run from nothing but the bare keyword — no
    /// crawled site content, no uploaded keyword sources, no matched Home-page Use Case.</summary>
    private static bool HasNoResearchInput(ProjectGenerationContext context) =>
        context.CrawledHeadings.Count == 0
        && context.CrawledParagraphs.Count == 0
        && context.KeywordSources.Count == 0
        && context.MatchedUseCase is null;

    private static string BuildNoResearchWarning(string targetKeyword) =>
        $"Generated from the keyword \"{targetKeyword}\" alone — no crawled site content, uploaded keyword sources, or matched Home-page Use Case were available.";

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
        metadata = SanitizePlanMetadata(metadata, context.PeopleAlsoAskQuestions, context.TargetKeyword);
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
        var mainSections = metadata.SectionOutline
            .Where(s => !PillarOutlineNormalizer.IsFaqSectionTitle(s))
            .ToList();

        var introductionHeading = mainSections.FirstOrDefault(PillarSectionClassifier.IsIntroductionSection);

        Section lede;
        LedeType ledeType;
        // Keyed by heading, not appended in call-completion order — calls now complete out of
        // outline order (batch call covers several non-adjacent headings at once), so the document
        // is reassembled in mainSections' original order at the end instead of relying on append order.
        var sectionsByHeading = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);

        if (introductionHeading is not null)
        {
            _logger.LogInformation("Generating pillar lede + introduction (combined call)");
            var introIndex = mainSections.IndexOf(introductionHeading);
            var ledeIntroResult = await provider.CompleteAsync(
                _promptBuilder.BuildArticleLedeAndIntroductionPrompt(
                    context, metadata, introductionHeading, introIndex, mainSections.Count, metadata.SectionOutline,
                    isRegeneration, revisionNotes, existingLedeHeading),
                cancellationToken);
            (lede, ledeType, var introSection) = LlmResponseJsonParser.ParseLedeAndIntroduction(
                ledeIntroResult.Content, "TechnicalArticle lede+introduction");
            sectionsByHeading[introductionHeading] = introSection;
        }
        else
        {
            // Fallback: no heading in this outline matches the Introduction markers — generate the
            // lede on its own rather than silently dropping it.
            _logger.LogInformation("Generating pillar lede (no Introduction heading found to combine with)");
            var ledeResult = await provider.CompleteAsync(
                _promptBuilder.BuildArticleLedePrompt(context, metadata, revisionNotes, existingLedeHeading),
                cancellationToken);
            (lede, ledeType) = LlmResponseJsonParser.ParseLede(ledeResult.Content, "TechnicalArticle lede");
        }

        // Batch every remaining main section that isn't Tools/Implementation into one call — those
        // two stay their own calls (Tools per the documented truncation-avoidance rule; Implementation
        // kept separate per the requested grouping).
        var batchHeadings = mainSections
            .Where(h => !sectionsByHeading.ContainsKey(h)
                && !PillarOutlineNormalizer.IsToolsSection(h)
                && !PillarSectionClassifier.IsImplementationSection(h))
            .ToList();

        if (batchHeadings.Count > 0)
        {
            _logger.LogInformation("Generating {Count} pillar sections in one batched call: {Headings}",
                batchHeadings.Count, string.Join(", ", batchHeadings));
            var batchResult = await provider.CompleteAsync(
                _promptBuilder.BuildArticleSectionBatchPrompt(
                    context, metadata, batchHeadings, metadata.SectionOutline, isRegeneration, revisionNotes),
                cancellationToken);
            var batchSections = LlmResponseJsonParser.ParseSections(batchResult.Content, "TechnicalArticle section batch");

            // Match returned sections back to requested headings by position — the model is asked to
            // return them in the same order it was given, but never trust that blindly for a keyed
            // lookup; fall back to the model's own heading text if the count doesn't line up.
            for (var b = 0; b < batchHeadings.Count; b++)
            {
                var section = b < batchSections.Count ? batchSections[b] : null;
                if (section is not null)
                {
                    sectionsByHeading[batchHeadings[b]] = section;
                }
            }

            var batchWordFloor = (int)(ContentLengthTargets.PillarSectionMinWords * 0.85);
            for (var b = 0; b < batchHeadings.Count; b++)
            {
                if (!sectionsByHeading.TryGetValue(batchHeadings[b], out var section))
                {
                    _logger.LogWarning("Batched pillar section \"{Heading}\" was not returned by the model.", batchHeadings[b]);
                    continue;
                }

                var words = ContentDocumentText.CountWords(section);
                if (words < batchWordFloor)
                {
                    _logger.LogWarning(
                        "Pillar section \"{Heading}\" is {Count} words (soft minimum {Minimum}) — no retry, single attempt only.",
                        batchHeadings[b], words, batchWordFloor);
                }
            }
        }

        for (var i = 0; i < mainSections.Count; i++)
        {
            var heading = mainSections[i];
            if (sectionsByHeading.ContainsKey(heading))
            {
                continue;
            }

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

            sectionsByHeading[heading] = section;

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

        var sections = mainSections
            .Where(h => sectionsByHeading.ContainsKey(h))
            .Select(h => sectionsByHeading[h])
            .ToList();

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
        string targetKeyword)
    {
        var (mainOutline, _) = PillarOutlineNormalizer.Sanitize(metadata.SectionOutline, paaQuestions, targetKeyword);
        return metadata with { SectionOutline = mainOutline };
    }

    private static (ArticleMetadataDraft Metadata, List<string> FaqQuestions) PrepareBodyInput(
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> paaQuestions,
        string targetKeyword)
    {
        var (mainOutline, faqQuestions) = PillarOutlineNormalizer.Sanitize(metadata.SectionOutline, paaQuestions, targetKeyword);
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

        _logger.LogInformation("Generating blog body (single call, repurposed from pillar text)");
        var bodyResult = await provider.CompleteAsync(
            _promptBuilder.BuildBlogBodyPrompt(context, article, metadata, revisionNotes),
            cancellationToken);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "BlogPosting body");
        var wordCount = ContentDocumentText.CountWords(sections);

        // The model chooses its own headings in the single-call body prompt rather than following
        // the metadata call's advisory outline — keep the stored outline truthful to what was
        // actually written.
        metadata = metadata with { SectionOutline = sections.Select(s => s.Heading).ToList() };

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
            new ContentDocument(lede, sections.ToList()),
            metadata.Keywords,
            wordCount,
            metadata.SectionOutline);

        return (draft, ledeType);
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

    public string FaviconUrl { get; set; } = "/favicon.ico";

    /// <summary>Real search-console verification tokens (Google/Yandex/Yahoo). Empty by default —
    /// a placeholder token in a verification meta tag is worse than no tag at all, so these are only
    /// emitted when actually configured to a real value.</summary>
    public string? GoogleSiteVerification { get; set; }
    public string? YandexVerification { get; set; }
    public string? YahooVerification { get; set; }

    /// <summary>How the publisher positions AI implementation services in pillar Tools sections.</summary>
    public string ImplementerPositioning { get; set; } =
        "Geek At Your Spot is an AI implementation consultancy for B2B organizations. " +
        "In every pillar Tools section, for each major platform covered, explain which client problems an AI implementer solves " +
        "(accelerated deployment, data model design, workflow configuration, custom code, autonomous agents, integration, and change management).";
}
