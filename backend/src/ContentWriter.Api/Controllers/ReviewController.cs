using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.Review;
using ContentWriter.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/review")]
public class ReviewController : ControllerBase
{
    private readonly IReviewLoopService _reviewLoop;
    private readonly ILogger<ReviewController> _logger;

    public ReviewController(IReviewLoopService reviewLoop, ILogger<ReviewController> logger)
    {
        _reviewLoop = reviewLoop;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<List<ReviewVerdictResponse>>> Run(
        Guid projectId, [FromBody] RunReviewRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            var contentTypes = request?.ContentTypes is { Count: > 0 }
                ? new HashSet<GeneratedContentType>(request.ContentTypes)
                : null;

            var verdicts = await _reviewLoop.RunForProjectAsync(projectId, contentTypes, request?.ToolSlugToTest, cancellationToken);
            return Ok(verdicts.Select(v => new ReviewVerdictResponse(
                v.Id,
                v.GeneratedContentId,
                v.GeneratedContent!.ContentType,
                v.GeneratedContent.Title,
                v.GeneratedContent.Slug,
                v.Status,
                v.AttemptCount,
                v.ReviewerProvider,
                v.ReviewerModel,
                v.NotesJson,
                v.RetryCount,
                v.RetryReason,
                v.CreatedAtUtc)).ToList());
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "Review loop failed for project {ProjectId}", projectId);
            return Problem(ex.Message, statusCode: 400, title: "Review failed");
        }
    }

    [HttpPost("rewrite")]
    public async Task<ActionResult<Application.DTOs.GeneratedContentSet>> Rewrite(
        Guid projectId, [FromBody] RewriteRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _reviewLoop.RewriteFromLatestVerdictAsync(projectId, request.GeneratedContentId, cancellationToken);
            return Ok(result);
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "Rewrite failed for project {ProjectId}, content {GeneratedContentId}", projectId, request.GeneratedContentId);
            return Problem(ex.Message, statusCode: 400, title: "Rewrite failed");
        }
    }
}

public sealed record RunReviewRequest(List<GeneratedContentType>? ContentTypes, string? ToolSlugToTest = null);

public sealed record RewriteRequest(Guid GeneratedContentId);

public sealed record ReviewVerdictResponse(
    Guid Id,
    Guid GeneratedContentId,
    GeneratedContentType ContentType,
    string Title,
    string Slug,
    Domain.Enums.ReviewVerdictStatus Status,
    int AttemptCount,
    Domain.Enums.LlmProviderType ReviewerProvider,
    string ReviewerModel,
    string NotesJson,
    int RetryCount,
    string? RetryReason,
    DateTime CreatedAtUtc);
