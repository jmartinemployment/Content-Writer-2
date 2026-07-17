using ContentWriter.Application.Services.Publish;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Data;
using ContentWriter.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContentWriter.Application.Services.Batch;

/// <summary>
/// Drives one BatchJobItem through the full pipeline: crawl, plan, body, tools/blog/social
/// (parallel), email, image prompts, review (stub until Phase 4), publish.
///
/// Resumability: before running each step, checks the real domain data for that step's expected
/// output (row completeness — non-empty required fields), not a stored "completed" flag alone.
/// A crash mid-write leaves an incomplete row, which re-derives as "not done" and reruns rather
/// than being silently skipped. The Publish step is the one exception — its effect lives in
/// GeekBackend, not locally, so it relies on GeekBackendClient's own existing-post lookup for
/// idempotency instead.
///
/// Each parallel step (Tools/Blog/Social) gets its own DI scope and DbContext — the orchestrator
/// methods call SaveChanges internally, and a single DbContext instance is not safe for concurrent
/// use, so true concurrent DB writes on one context are avoided by construction.
/// </summary>
public sealed class BatchPipelineRunner : IBatchPipelineRunner
{
    private const int CrawlMaxPages = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BatchPipelineRunner> _logger;

    public BatchPipelineRunner(IServiceScopeFactory scopeFactory, ILogger<BatchPipelineRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunItemAsync(Guid batchJobItemId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentWriterDbContext>();

        var item = await db.BatchJobItems
            .Include(i => i.BatchJob)
            .FirstOrDefaultAsync(i => i.Id == batchJobItemId, cancellationToken)
            ?? throw new InvalidOperationException($"BatchJobItem {batchJobItemId} not found.");

        item.Status = BatchJobItemStatus.Running;
        item.StartedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var projectId = await EnsureProjectAsync(db, item, cancellationToken);

            await RunStepAsync(db, item, BatchStepName.Crawl, cancellationToken,
                () => IsCrawlCompleteAsync(db, projectId, cancellationToken),
                async ct =>
                {
                    var projectRepository = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
                    var crawler = scope.ServiceProvider.GetRequiredService<ISiteCrawlerService>();
                    var project = await projectRepository.GetByIdAsync(projectId, ct)
                        ?? throw new InvalidOperationException($"Project {projectId} not found.");

                    var result = await crawler.CrawlAsync(project.ProjectUrl, CrawlMaxPages, ct);
                    await projectRepository.SetCrawledSiteAsync(new CrawledSite
                    {
                        ProjectId = projectId,
                        SourceUrl = project.ProjectUrl,
                        SiteName = result.SiteName,
                        JsonLdBlocks = result.JsonLdBlocks,
                        Headings = result.Headings,
                        Paragraphs = result.Paragraphs,
                        DetectedTone = result.DetectedTone,
                        DetectedFocus = result.DetectedFocus,
                        PagesCrawled = result.PagesCrawled,
                    }, ct);

                    project.Status = ProjectStatus.ReadyForGeneration;
                    projectRepository.Update(project);
                    await projectRepository.SaveChangesAsync(ct);
                });

            await RunStepAsync(db, item, BatchStepName.Plan, cancellationToken,
                () => IsPlanCompleteAsync(db, projectId, cancellationToken),
                async ct =>
                {
                    var orchestrator = scope.ServiceProvider.GetRequiredService<IContentGenerationOrchestrator>();
                    await orchestrator.GeneratePillarPlanAsync(projectId, ct);
                });

            await RunStepAsync(db, item, BatchStepName.Body, cancellationToken,
                () => IsBodyCompleteAsync(db, projectId, cancellationToken),
                async ct =>
                {
                    var orchestrator = scope.ServiceProvider.GetRequiredService<IContentGenerationOrchestrator>();
                    await orchestrator.GeneratePillarBodyAsync(projectId, ct);
                });

            // Tools, Blog, Social run concurrently — each in its own scope/DbContext since the
            // orchestrator saves internally and one DbContext instance can't be used concurrently.
            await Task.WhenAll(
                RunStepInOwnScopeAsync(item.Id, BatchStepName.Tools, cancellationToken,
                    (db2, ct) => IsToolsCompleteAsync(db2, projectId, ct),
                    async (sp, ct) => await sp.GetRequiredService<IContentGenerationOrchestrator>().GenerateToolPagesAsync(projectId, ct)),
                RunStepInOwnScopeAsync(item.Id, BatchStepName.Blog, cancellationToken,
                    (db2, ct) => IsBlogCompleteAsync(db2, projectId, ct),
                    async (sp, ct) => await sp.GetRequiredService<IContentGenerationOrchestrator>().GenerateBlogAsync(projectId, ct)),
                RunStepInOwnScopeAsync(item.Id, BatchStepName.Social, cancellationToken,
                    (db2, ct) => IsSocialCompleteAsync(db2, projectId, ct),
                    async (sp, ct) => await sp.GetRequiredService<IContentGenerationOrchestrator>().GenerateSocialAsync(projectId, ct)));

            await RunStepAsync(db, item, BatchStepName.Email, cancellationToken,
                () => IsEmailCompleteAsync(db, projectId, cancellationToken),
                async ct =>
                {
                    var orchestrator = scope.ServiceProvider.GetRequiredService<IContentGenerationOrchestrator>();
                    await orchestrator.GenerateColdOutreachAsync(projectId, ct);
                });

            await RunStepAsync(db, item, BatchStepName.ImagePrompts, cancellationToken,
                () => IsImagePromptsCompleteAsync(db, projectId, cancellationToken),
                async ct =>
                {
                    var orchestrator = scope.ServiceProvider.GetRequiredService<IContentGenerationOrchestrator>();
                    await orchestrator.GenerateImagePromptsAsync(projectId, ct);
                });

            // Stub until Phase 4's EditorialReviewService lands — auto-approve so Publish can proceed.
            await RunStepAsync(db, item, BatchStepName.Review, cancellationToken,
                () => Task.FromResult(true),
                _ => Task.CompletedTask);

            await RunStepAsync(db, item, BatchStepName.Publish, cancellationToken,
                isComplete: null, // side effect lives in GeekBackend, not locally checkable — rely on GeekBackendClient's own idempotent upsert-by-slug
                async ct =>
                {
                    var publishService = scope.ServiceProvider.GetRequiredService<IGeekBlogPublishService>();
                    await publishService.PublishAsync(projectId, departmentOverride: null, ct);
                });

            item.Status = BatchJobItemStatus.Completed;
            item.CompletedAtUtc = DateTime.UtcNow;
            item.ErrorText = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchJobItem {ItemId} failed", batchJobItemId);
            item.Status = BatchJobItemStatus.Failed;
            item.ErrorText = ex.Message;
            item.CompletedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        await UpdateBatchJobCountersAsync(db, item.BatchJobId, cancellationToken);
    }

    private static async Task<Guid> EnsureProjectAsync(ContentWriterDbContext db, BatchJobItem item, CancellationToken cancellationToken)
    {
        if (item.ProjectId is not null)
            return item.ProjectId.Value;

        var project = new Project
        {
            ClientId = item.BatchJob!.ClientId,
            Name = item.TargetKeyword,
            ProjectUrl = item.ProjectUrl,
            TargetKeyword = item.TargetKeyword,
            PreferredProvider = item.PreferredProvider,
        };

        db.Projects.Add(project);
        item.ProjectId = project.Id;
        await db.SaveChangesAsync(cancellationToken);

        return project.Id;
    }

    /// <summary>Runs one step against the item's current scope, skipping it if already complete and recording attempt/timing/error state.</summary>
    private async Task RunStepAsync(
        ContentWriterDbContext db,
        BatchJobItem item,
        BatchStepName stepName,
        CancellationToken cancellationToken,
        Func<Task<bool>>? isComplete,
        Func<CancellationToken, Task> run)
    {
        var step = await db.BatchJobItemSteps
            .FirstOrDefaultAsync(s => s.BatchJobItemId == item.Id && s.StepName == stepName, cancellationToken);

        if (step is null)
        {
            step = new BatchJobItemStep { BatchJobItemId = item.Id, StepName = stepName };
            db.BatchJobItemSteps.Add(step);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (isComplete is not null && await isComplete())
        {
            step.Status = BatchStepStatus.Completed;
            step.CompletedAtUtc ??= DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        step.Status = BatchStepStatus.Running;
        step.AttemptCount += 1;
        step.StartedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await run(cancellationToken);
            step.Status = BatchStepStatus.Completed;
            step.CompletedAtUtc = DateTime.UtcNow;
            step.ErrorText = null;
        }
        catch (Exception ex)
        {
            step.Status = BatchStepStatus.Failed;
            step.ErrorText = ex.Message;
            throw;
        }
        finally
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Same as RunStepAsync but opens its own DI scope/DbContext, for steps run concurrently via Task.WhenAll.</summary>
    private async Task RunStepInOwnScopeAsync(
        Guid batchJobItemId,
        BatchStepName stepName,
        CancellationToken cancellationToken,
        Func<ContentWriterDbContext, CancellationToken, Task<bool>> isComplete,
        Func<IServiceProvider, CancellationToken, Task> run)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentWriterDbContext>();

        var item = await db.BatchJobItems.FirstAsync(i => i.Id == batchJobItemId, cancellationToken);

        await RunStepAsync(db, item, stepName, cancellationToken,
            () => isComplete(db, cancellationToken),
            ct => run(scope.ServiceProvider, ct));
    }

    private static async Task UpdateBatchJobCountersAsync(ContentWriterDbContext db, Guid batchJobId, CancellationToken cancellationToken)
    {
        var items = await db.BatchJobItems.Where(i => i.BatchJobId == batchJobId).ToListAsync(cancellationToken);
        var job = await db.BatchJobs.FirstAsync(b => b.Id == batchJobId, cancellationToken);

        job.CompletedItems = items.Count(i => i.Status == BatchJobItemStatus.Completed);
        job.FailedItems = items.Count(i => i.Status == BatchJobItemStatus.Failed);

        if (items.All(i => i.Status is BatchJobItemStatus.Completed or BatchJobItemStatus.Failed or BatchJobItemStatus.Cancelled))
        {
            job.Status = job.FailedItems > 0 && job.CompletedItems == 0 ? BatchJobStatus.Failed : BatchJobStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // --- Step completeness checks: re-derived from real domain data, not a trusted status flag ---

    private static async Task<bool> IsCrawlCompleteAsync(ContentWriterDbContext db, Guid projectId, CancellationToken ct) =>
        await db.CrawledSites.AnyAsync(c => c.ProjectId == projectId && c.PagesCrawled > 0, ct);

    private static async Task<bool> IsPlanCompleteAsync(ContentWriterDbContext db, Guid projectId, CancellationToken ct) =>
        await db.GeneratedContents.AnyAsync(c =>
            c.ProjectId == projectId && c.ContentType == GeneratedContentType.TechnicalArticle
            && !string.IsNullOrEmpty(c.Title), ct);

    private static async Task<bool> IsBodyCompleteAsync(ContentWriterDbContext db, Guid projectId, CancellationToken ct) =>
        await db.GeneratedContents.AnyAsync(c =>
            c.ProjectId == projectId && c.ContentType == GeneratedContentType.TechnicalArticle
            && c.WordCount > 0 && c.BodyHtml != string.Empty, ct);

    private static async Task<bool> IsToolsCompleteAsync(ContentWriterDbContext db, Guid projectId, CancellationToken ct) =>
        await db.GeneratedContents.AnyAsync(c =>
            c.ProjectId == projectId && c.ContentType == GeneratedContentType.ToolPost
            && c.WordCount > 0, ct);

    private static async Task<bool> IsBlogCompleteAsync(ContentWriterDbContext db, Guid projectId, CancellationToken ct) =>
        await db.GeneratedContents.AnyAsync(c =>
            c.ProjectId == projectId && c.ContentType == GeneratedContentType.BlogPost
            && c.WordCount > 0 && c.BodyHtml != string.Empty, ct);

    private static async Task<bool> IsSocialCompleteAsync(ContentWriterDbContext db, Guid projectId, CancellationToken ct)
    {
        var hasFacebook = await db.GeneratedContents.AnyAsync(c =>
            c.ProjectId == projectId && c.ContentType == GeneratedContentType.SocialFacebook && c.BodyHtml != string.Empty, ct);
        var hasLinkedIn = await db.GeneratedContents.AnyAsync(c =>
            c.ProjectId == projectId && c.ContentType == GeneratedContentType.SocialLinkedIn && c.BodyHtml != string.Empty, ct);
        return hasFacebook && hasLinkedIn;
    }

    private static async Task<bool> IsEmailCompleteAsync(ContentWriterDbContext db, Guid projectId, CancellationToken ct) =>
        await db.GeneratedContents.AnyAsync(c =>
            c.ProjectId == projectId && c.ContentType == GeneratedContentType.EmailColdOutreach
            && c.BodyHtml != string.Empty, ct);

    private static async Task<bool> IsImagePromptsCompleteAsync(ContentWriterDbContext db, Guid projectId, CancellationToken ct) =>
        await db.GeneratedContents.AnyAsync(c =>
            c.ProjectId == projectId && (
                c.ContentType == GeneratedContentType.ImagePromptPillarFigure
                || c.ContentType == GeneratedContentType.ImagePromptSection), ct);
}
