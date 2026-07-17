using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/generate")]
public class GenerateController : ControllerBase
{
    private readonly IContentGenerationOrchestrator _orchestrator;
    private readonly ILogger<GenerateController> _logger;

    public GenerateController(IContentGenerationOrchestrator orchestrator, ILogger<GenerateController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [HttpPost("pillar/plan")]
    public Task<IActionResult> GeneratePillarPlan(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GeneratePillarPlanAsync(projectId, cancellationToken), "pillar-plan", cancellationToken);

    [HttpPost("pillar/body")]
    public Task<IActionResult> GeneratePillarBody(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GeneratePillarBodyAsync(projectId, cancellationToken), "pillar-body", cancellationToken);

    [HttpPost("pillar")]
    public Task<IActionResult> GeneratePillar(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GeneratePillarAsync(projectId, cancellationToken), "pillar", cancellationToken);

    [HttpPost("tools")]
    public Task<IActionResult> GenerateToolPages(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GenerateToolPagesAsync(projectId, cancellationToken), "tools", cancellationToken);

    [HttpPost("blog")]
    public Task<IActionResult> GenerateBlog(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GenerateBlogAsync(projectId, cancellationToken), "blog", cancellationToken);

    [HttpPost("social")]
    public Task<IActionResult> GenerateSocial(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GenerateSocialAsync(projectId, cancellationToken), "social", cancellationToken);

    [HttpPost("email-cold-outreach")]
    public Task<IActionResult> GenerateColdOutreach(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GenerateColdOutreachAsync(projectId, cancellationToken), "email-cold-outreach", cancellationToken);

    [HttpPost("image-prompts")]
    public Task<IActionResult> GenerateImagePrompts(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GenerateImagePromptsAsync(projectId, cancellationToken), "image-prompts", cancellationToken);

    [HttpPost]
    public Task<IActionResult> GenerateAll(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GenerateAllAsync(projectId, cancellationToken), "all", cancellationToken);

    private async Task<IActionResult> RunStep(
        Guid projectId, Task<GeneratedContentSet> action, string step, CancellationToken cancellationToken)
    {
        try
        {
            var result = await action;
            return Ok(result);
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "Content generation step {Step} failed for project {ProjectId}", step, projectId);
            var title = ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                ? "Generation timed out"
                : "Content generation failed";
            return Problem(ex.Message, statusCode: 502, title: title);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Content generation step {Step} timed out for project {ProjectId}", step, projectId);
            return Problem(
                "The LLM provider did not respond in time. Try again, or split the step into smaller pieces.",
                statusCode: 504,
                title: "Generation timed out");
        }
    }
}
