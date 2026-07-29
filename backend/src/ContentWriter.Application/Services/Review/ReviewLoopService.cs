using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.InMemory;

namespace ContentWriter.Application.Services.Review;

public interface IReviewLoopService
{
    /// <summary>
    /// Single-pass review over a project's publishable rows (pillar, blog, and at most one tool
    /// post). Does not regenerate or re-review automatically — when the verdict is Revise, the
    /// user triggers rewrite via <see cref="RewriteFromLatestVerdictAsync"/>.
    /// When tools are included, <paramref name="toolSlugToTest"/> selects which tool; if omitted,
    /// the first tool by order is reviewed.
    /// </summary>
    Task<List<ReviewVerdict>> RunForProjectAsync(
        Guid projectId, IReadOnlySet<GeneratedContentType>? contentTypes = null, string? toolSlugToTest = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// User-initiated regenerate for one row using its most recent revise feedback (Revise or
    /// legacy Exhausted). Passes extracted reviewer suggestions into the writer prompt. For a
    /// ToolPost row this regenerates only that one tool, not the whole tool-post batch.
    /// </summary>
    Task<GeneratedContentSet> RewriteFromLatestVerdictAsync(
        Guid projectId, Guid generatedContentId, CancellationToken cancellationToken = default);
}

public sealed class ReviewLoopService : IReviewLoopService
{
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

        var context = _orchestrator.BuildContext(project);

        if (requestedTypes.Contains(GeneratedContentType.TechnicalArticle)
            && project.GeneratedContents.Any(c => c.ContentType == GeneratedContentType.TechnicalArticle))
        {
            verdicts.Add(await ReviewSingleRowAsync(project, GeneratedContentType.TechnicalArticle, context, cancellationToken));
        }

        if (requestedTypes.Contains(GeneratedContentType.BlogPost)
            && project.GeneratedContents.Any(c => c.ContentType == GeneratedContentType.BlogPost))
        {
            verdicts.Add(await ReviewSingleRowAsync(project, GeneratedContentType.BlogPost, context, cancellationToken));
        }

        if (requestedTypes.Contains(GeneratedContentType.ToolPost)
            && project.GeneratedContents.Any(c => c.ContentType == GeneratedContentType.ToolPost))
        {
            // Always review exactly one tool page — never the whole batch (rate limits + focused rewrite).
            var toolRow = project.GeneratedContents
                .Where(c => c.ContentType == GeneratedContentType.ToolPost)
                .OrderBy(c => c.SourceAppOrder)
                .ThenBy(c => c.Title)
                .FirstOrDefault(c =>
                    string.IsNullOrWhiteSpace(toolSlugToTest)
                    || string.Equals(c.Slug, toolSlugToTest, StringComparison.OrdinalIgnoreCase));

            if (toolRow is null && !string.IsNullOrWhiteSpace(toolSlugToTest))
            {
                throw new ContentGenerationException(
                    $"No tool post with slug '{toolSlugToTest}' was found to review.");
            }

            if (toolRow is not null)
            {
                verdicts.Add(await ReviewAndRecordAsync(toolRow, context, cancellationToken));
            }
        }

        // Persist the project after recording verdicts
        await _projectStore.SaveAsync(project, cancellationToken);

        return verdicts;
    }

    public async Task<GeneratedContentSet> RewriteFromLatestVerdictAsync(
        Guid projectId, Guid generatedContentId, CancellationToken cancellationToken = default)
    {
        var project = await _projectStore.GetAsync(projectId, cancellationToken)
            ?? throw new ContentGenerationException($"Project {projectId} was not found.");

        var row = project.GeneratedContents.FirstOrDefault(c => c.Id == generatedContentId)
            ?? throw new ContentGenerationException($"Generated content {generatedContentId} was not found.");

        // Legacy Exhausted verdicts (from the old auto-retry attempt cap) still carry rewrite feedback.
        var latestFeedback = row.ReviewVerdicts
            .Where(v => v.Status is ReviewVerdictStatus.Revise or ReviewVerdictStatus.Exhausted)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefault()
            ?? throw new ContentGenerationException("No outstanding revise feedback for this content.");

        var revisionNotes = ExtractRevisionNotes(latestFeedback.NotesJson);
        if (string.IsNullOrWhiteSpace(revisionNotes))
        {
            throw new ContentGenerationException("Latest review verdict has no suggestions to pass to the writer.");
        }

        // Tool regenerations share generic H2 names — tag notes with this row's slug so
        // BuildRevisionNotesBlock can scope them when Tool: markers are present.
        if (row.ContentType == GeneratedContentType.ToolPost
            && !revisionNotes.Contains("Tool:", StringComparison.Ordinal))
        {
            revisionNotes = $"Tool: {row.Slug}\n{revisionNotes}";
        }

        return row.ContentType switch
        {
            GeneratedContentType.TechnicalArticle =>
                await _orchestrator.GeneratePillarBodyAsync(projectId, revisionNotes, cancellationToken: cancellationToken),
            GeneratedContentType.BlogPost =>
                await _orchestrator.GenerateBlogAsync(projectId, revisionNotes, cancellationToken),
            GeneratedContentType.ToolPost =>
                await _orchestrator.GenerateToolPagesAsync(
                    projectId, revisionNotes, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.Slug }, cancellationToken),
            _ => throw new ContentGenerationException("Rewrite is not supported for this content type."),
        };
    }

    /// <summary>Pulls the human-readable suggestions out of a reviewer response. Accepts either the
    /// raw JSON <c>{"verdict","notes"}</c> blob stored on the verdict, or plain notes text.</summary>
    public static string ExtractRevisionNotes(string notesJson)
    {
        if (string.IsNullOrWhiteSpace(notesJson))
        {
            return string.Empty;
        }

        var trimmed = notesJson.Trim();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("notes", out var notesProp)
                    && notesProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return notesProp.GetString()?.Trim() ?? string.Empty;
                }

                // Reviewer JSON without a notes field (e.g. approved-only) — nothing to pass.
                if (doc.RootElement.TryGetProperty("verdict", out _))
                {
                    return string.Empty;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON — treat the whole string as the feedback.
        }

        return trimmed;
    }

    private async Task<ReviewVerdict> ReviewSingleRowAsync(
        Project project, GeneratedContentType contentType, ProjectGenerationContext context, CancellationToken cancellationToken)
    {
        var row = project.GeneratedContents.First(c => c.ContentType == contentType);
        return await ReviewAndRecordAsync(row, context, cancellationToken);
    }

    private async Task<ReviewVerdict> ReviewAndRecordAsync(
        GeneratedContent row, ProjectGenerationContext context, CancellationToken cancellationToken)
    {
        var outcome = await _reviewService.ReviewAsync(row, context, cancellationToken);

        var verdict = new ReviewVerdict
        {
            GeneratedContentId = row.Id,
            GeneratedContent = row,
            Status = outcome.Status,
            NotesJson = outcome.NotesJson,
            ReviewerProvider = outcome.ReviewerProvider,
            ReviewerModel = outcome.ReviewerModel,
            AttemptCount = 1,
            RetryCount = outcome.RetryCount,
            RetryReason = outcome.RetryReason,
        };
        row.ReviewVerdicts.Add(verdict);

        return verdict;
    }
}
