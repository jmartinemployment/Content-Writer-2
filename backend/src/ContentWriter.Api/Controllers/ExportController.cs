using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.Publish;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}")]
public class ExportController : ControllerBase
{
    private readonly IGeekBlogPublishService _publishService;
    private readonly ILogger<ExportController> _logger;

    public ExportController(IGeekBlogPublishService publishService, ILogger<ExportController> logger)
    {
        _publishService = publishService;
        _logger = logger;
    }

    [HttpPost("publish")]
    public async Task<IActionResult> Publish(
        Guid projectId,
        [FromBody] PublishRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _publishService.PublishAsync(
                projectId,
                request?.Department,
                cancellationToken);

            return Ok(new PublishResponse(
                result.CategorySlug,
                result.Posts.Select(p => new PublishedPostResponse(
                    p.ContentType,
                    p.PostId,
                    p.Slug,
                    p.LanguageCode,
                    p.SectionCount,
                    p.WasUpdated)).ToList()));
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "Publish to GeekBackend failed for project {ProjectId}", projectId);
            return Problem(ex.Message, statusCode: 400, title: "Publish failed");
        }
        catch (GeekBackendPublishException ex)
        {
            _logger.LogError(ex, "GeekBackend rejected publish for project {ProjectId}", projectId);
            return Problem(ex.Message, statusCode: 502, title: "GeekBackend publish failed");
        }
    }
}

public sealed record PublishRequest(string? Department);

public sealed record PublishResponse(string CategorySlug, IReadOnlyList<PublishedPostResponse> Posts);

public sealed record PublishedPostResponse(
    string ContentType,
    int PostId,
    string Slug,
    string LanguageCode,
    int SectionCount,
    bool WasUpdated);
