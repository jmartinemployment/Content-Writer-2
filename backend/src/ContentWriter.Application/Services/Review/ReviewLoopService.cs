using ContentWriter.Application.Providers;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.InMemory;

namespace ContentWriter.Application.Services.Review;

public interface IReviewLoopService
{
    /// <summary>
    /// Runs the automated revise loop over a project's publishable rows (pillar, blog, the tool
    /// post set) — review, and on Revise regenerate + re-review, up to <see cref="MaxAttempts"/>
    /// attempts. No human gate, no held-for-manual-review state. A row that hits the cap without
    /// Approved becomes Exhausted — visible via the live AttemptCount, not silently retried forever.
    /// </summary>
    Task<List<ReviewVerdict>> RunForProjectAsync(
        Guid projectId, IReadOnlySet<GeneratedContentType>? contentTypes = null, string? toolSlugToTest = null, CancellationToken cancellationToken = default);
}

public sealed class ReviewLoopService : IReviewLoopService
{
    // Capped to 1 to limit review-call cost — no revise-and-retry loop, just a single review pass per row.
    private const int MaxAttempts = 1;

    private readonly IProjectStore _projectStore;
    private readonly IEditorialReviewService _reviewService;
    private readonly IContentGenerationOrchestrator _orchestrator;

    public ReviewLoopService(
        IProjectStore projectStore, IEditorialReviewService reviewService, IContentGenerationOrchestrator orchestrator)
    {
        _projectStore = projectStore;
        _reviewService = reviewService;
        _orchestrator = orchestrator;
    }

    private static readonly IReadOnlySet<GeneratedContentType> AllReviewableTypes = new HashSet<GeneratedContentType>
    {
        GeneratedContentType.TechnicalArticle,
        GeneratedContentType.BlogPost,
        GeneratedContentType.ToolPost,
    };

    public async Task<List<ReviewVerdict>> RunForProjectAsync(
        Guid projectId, IReadOnlySet<GeneratedContentType>? contentTypes = null, string? toolSlugToTest = null, CancellationToken cancellationToken = default)
    {
        var project = await _projectStore.GetAsync(projectId, cancellationToken)
            ?? throw new ContentGenerationException($"Project {projectId} was not found.");

        var requestedTypes = contentTypes is null or { Count: 0 } ? AllReviewableTypes : contentTypes;
        var verdicts = new List<ReviewVerdict>();

        if (requestedTypes.Contains(GeneratedContentType.TechnicalArticle)
            && project.GeneratedContents.Any(c => c.ContentType == GeneratedContentType.TechnicalArticle))
        {
            verdicts.Add(await RunSingleRowLoopAsync(
                project, GeneratedContentType.TechnicalArticle, project.TargetKeyword,
                () => _orchestrator.GeneratePillarBodyAsync(projectId, cancellationToken), cancellationToken));
        }

        if (requestedTypes.Contains(GeneratedContentType.BlogPost)
            && project.GeneratedContents.Any(c => c.ContentType == GeneratedContentType.BlogPost))
        {
            verdicts.Add(await RunSingleRowLoopAsync(
                project, GeneratedContentType.BlogPost, project.TargetKeyword,
                () => _orchestrator.GenerateBlogAsync(projectId, cancellationToken), cancellationToken));
        }

        if (requestedTypes.Contains(GeneratedContentType.ToolPost)
            && project.GeneratedContents.Any(c => c.ContentType == GeneratedContentType.ToolPost))
        {
            verdicts.AddRange(await RunToolPostBatchLoopAsync(project, projectId, toolSlugToTest, cancellationToken));
        }

        return verdicts;
    }

    /// <summary>For content types with exactly one row (pillar, blog) — regeneration replaces the row, so it's relooked-up by (project, type) each attempt rather than tracked by a stale Id.</summary>
    private async Task<ReviewVerdict> RunSingleRowLoopAsync(
        Project project, GeneratedContentType contentType, string targetKeyword,
        Func<Task> regenerate, CancellationToken cancellationToken)
    {
        ReviewVerdict verdict = null!;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var row = project.GeneratedContents.First(c => c.ContentType == contentType);

            verdict = await ReviewAndRecordAsync(row, targetKeyword, attempt, cancellationToken);

            if (verdict.Status == ReviewVerdictStatus.Approved || attempt == MaxAttempts)
            {
                if (verdict.Status == ReviewVerdictStatus.Revise && attempt == MaxAttempts)
                {
                    verdict.Status = ReviewVerdictStatus.Exhausted;
                }
                return verdict;
            }

            await regenerate();
        }

        return verdict;
    }

    /// <summary>Tool posts reviewed as a batch (or single tool if toolSlugToTest specified). Any Revise triggers regenerating the entire set, up to MaxAttempts whole-set attempts.</summary>
    private async Task<List<ReviewVerdict>> RunToolPostBatchLoopAsync(
        Project project, Guid projectId, string? toolSlugToTest, CancellationToken cancellationToken)
    {
        List<ReviewVerdict> verdicts = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var rows = project.GeneratedContents
                .Where(c => c.ContentType == GeneratedContentType.ToolPost)
                .Where(c => string.IsNullOrWhiteSpace(toolSlugToTest) || c.Slug == toolSlugToTest)
                .ToList();

            verdicts = [];
            foreach (var row in rows)
            {
                verdicts.Add(await ReviewAndRecordAsync(row, project.TargetKeyword, attempt, cancellationToken));
            }

            var allApproved = verdicts.Count > 0 && verdicts.All(v => v.Status == ReviewVerdictStatus.Approved);
            if (allApproved || attempt == MaxAttempts)
            {
                if (!allApproved)
                {
                    foreach (var v in verdicts.Where(v => v.Status == ReviewVerdictStatus.Revise))
                        v.Status = ReviewVerdictStatus.Exhausted;
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
            RetryCount = outcome.RetryCount,
            RetryReason = outcome.RetryReason,
        };
        row.ReviewVerdicts.Add(verdict);

        return verdict;
    }
}
