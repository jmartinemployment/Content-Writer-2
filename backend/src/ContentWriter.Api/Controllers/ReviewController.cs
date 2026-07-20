using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.Review;
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
    public async Task<ActionResult<List<ReviewVerdictResponse>>> Run(Guid projectId, CancellationToken cancellationToken)
    {
        try
        {
            var verdicts = await _reviewLoop.RunForProjectAsync(projectId, cancellationToken);
            return Ok(verdicts.Select(v => new ReviewVerdictResponse(
                v.Id, v.GeneratedContentId, v.Status, v.AttemptCount, v.ReviewerProvider, v.ReviewerModel,
                v.NotesJson, v.RetryCount, v.RetryReason, v.CreatedAtUtc)).ToList());
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "Review loop failed for project {ProjectId}", projectId);
            return Problem(ex.Message, statusCode: 400, title: "Review failed");
        }
    }
}

public sealed record ReviewVerdictResponse(
    Guid Id, Guid GeneratedContentId, Domain.Enums.ReviewVerdictStatus Status, int AttemptCount,
    Domain.Enums.LlmProviderType ReviewerProvider, string ReviewerModel, string NotesJson,
    int RetryCount, string? RetryReason, DateTime CreatedAtUtc);
