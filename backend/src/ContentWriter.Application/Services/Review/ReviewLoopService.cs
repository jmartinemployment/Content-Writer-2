using ContentWriter.Application.Providers;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentWriter.Application.Services.Review;

public interface IReviewLoopService
{
    /// <summary>
    /// Runs the automated revise loop over a project's publishable rows (pillar, blog, the tool
    /// post set) — review, and on Revise regenerate + re-review, up to <see cref="MaxAttempts"/>
    /// attempts. No human gate, no held-for-manual-review state. A row that hits the cap without
    /// Approved becomes Exhausted — visible via the live AttemptCount, not silently retried forever.
    /// </summary>
    Task<List<ReviewVerdict>> RunForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public sealed class ReviewLoopService : IReviewLoopService
{
    // Capped to 1 to limit review-call cost — no revise-and-retry loop, just a single review pass per row.
    private const int MaxAttempts = 1;

    private readonly ContentWriterDbContext _db;
    private readonly IEditorialReviewService _reviewService;
    private readonly IContentGenerationOrchestrator _orchestrator;

    public ReviewLoopService(
        ContentWriterDbContext db, IEditorialReviewService reviewService, IContentGenerationOrchestrator orchestrator)
    {
        _db = db;
        _reviewService = reviewService;
        _orchestrator = orchestrator;
    }

    public async Task<List<ReviewVerdict>> RunForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new ContentGenerationException($"Project {projectId} was not found.");

        var verdicts = new List<ReviewVerdict>();

        if (await _db.GeneratedContents.AnyAsync(c => c.ProjectId == projectId && c.ContentType == GeneratedContentType.TechnicalArticle, cancellationToken))
        {
            verdicts.Add(await RunSingleRowLoopAsync(
                projectId, GeneratedContentType.TechnicalArticle, project.TargetKeyword,
                () => _orchestrator.GeneratePillarBodyAsync(projectId, cancellationToken), cancellationToken));
        }

        if (await _db.GeneratedContents.AnyAsync(c => c.ProjectId == projectId && c.ContentType == GeneratedContentType.BlogPost, cancellationToken))
        {
            verdicts.Add(await RunSingleRowLoopAsync(
                projectId, GeneratedContentType.BlogPost, project.TargetKeyword,
                () => _orchestrator.GenerateBlogAsync(projectId, cancellationToken), cancellationToken));
        }

        if (await _db.GeneratedContents.AnyAsync(c => c.ProjectId == projectId && c.ContentType == GeneratedContentType.ToolPost, cancellationToken))
        {
            verdicts.AddRange(await RunToolPostBatchLoopAsync(projectId, project.TargetKeyword, cancellationToken));
        }

        return verdicts;
    }

    /// <summary>For content types with exactly one row (pillar, blog) — regeneration replaces the row, so it's refetched by (project, type) each attempt rather than tracked by a stale Id.</summary>
    private async Task<ReviewVerdict> RunSingleRowLoopAsync(
        Guid projectId, GeneratedContentType contentType, string targetKeyword,
        Func<Task> regenerate, CancellationToken cancellationToken)
    {
        ReviewVerdict verdict = null!;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var row = await _db.GeneratedContents.FirstAsync(
                c => c.ProjectId == projectId && c.ContentType == contentType, cancellationToken);

            verdict = await ReviewAndRecordAsync(row, targetKeyword, attempt, cancellationToken);

            if (verdict.Status == ReviewVerdictStatus.Approved || attempt == MaxAttempts)
            {
                if (verdict.Status == ReviewVerdictStatus.Revise && attempt == MaxAttempts)
                {
                    verdict.Status = ReviewVerdictStatus.Exhausted;
                    await _db.SaveChangesAsync(cancellationToken);
                }
                return verdict;
            }

            await regenerate();
        }

        return verdict;
    }

    /// <summary>Tool posts regenerate as a whole set (GenerateToolPagesAsync has no single-row mode) — reviewed as a batch: any Revise triggers regenerating the entire set, up to MaxAttempts whole-set attempts.</summary>
    private async Task<List<ReviewVerdict>> RunToolPostBatchLoopAsync(
        Guid projectId, string targetKeyword, CancellationToken cancellationToken)
    {
        List<ReviewVerdict> verdicts = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var rows = await _db.GeneratedContents
                .Where(c => c.ProjectId == projectId && c.ContentType == GeneratedContentType.ToolPost)
                .ToListAsync(cancellationToken);

            verdicts = [];
            foreach (var row in rows)
            {
                verdicts.Add(await ReviewAndRecordAsync(row, targetKeyword, attempt, cancellationToken));
            }

            var allApproved = verdicts.Count > 0 && verdicts.All(v => v.Status == ReviewVerdictStatus.Approved);
            if (allApproved || attempt == MaxAttempts)
            {
                if (!allApproved)
                {
                    foreach (var v in verdicts.Where(v => v.Status == ReviewVerdictStatus.Revise))
                        v.Status = ReviewVerdictStatus.Exhausted;
                    await _db.SaveChangesAsync(cancellationToken);
                }
                return verdicts;
            }

            await _orchestrator.GenerateToolPagesAsync(projectId, cancellationToken);
        }

        return verdicts;
    }

    private async Task<ReviewVerdict> ReviewAndRecordAsync(
        GeneratedContent row, string targetKeyword, int attempt, CancellationToken cancellationToken)
    {
        var outcome = await _reviewService.ReviewAsync(row, targetKeyword, cancellationToken);

        var verdict = new ReviewVerdict
        {
            GeneratedContentId = row.Id,
            Status = outcome.Status,
            NotesJson = outcome.NotesJson,
            ReviewerProvider = outcome.ReviewerProvider,
            ReviewerModel = outcome.ReviewerModel,
            AttemptCount = attempt,
        };
        _db.ReviewVerdicts.Add(verdict);
        await _db.SaveChangesAsync(cancellationToken);

        return verdict;
    }
}
