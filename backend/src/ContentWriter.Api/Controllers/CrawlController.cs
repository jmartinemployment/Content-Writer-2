using ContentWriter.Api.Contracts;
using ContentWriter.Application.Services;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.InMemory;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/crawl")]
public class CrawlController : ControllerBase
{
    private readonly IProjectStore _projectStore;
    private readonly ISiteCrawlerService _crawlerService;
    private readonly ILogger<CrawlController> _logger;

    public CrawlController(IProjectStore projectStore, ISiteCrawlerService crawlerService, ILogger<CrawlController> logger)
    {
        _projectStore = projectStore;
        _crawlerService = crawlerService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<CrawlSummaryResponse>> CrawlProject(Guid projectId, [FromQuery] int maxPages = 50, CancellationToken cancellationToken = default)
    {
        var project = await _projectStore.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        project.Status = ProjectStatus.Crawling;

        SiteCrawlResult result;
        try
        {
            result = await _crawlerService.CrawlAsync(project.ProjectUrl, maxPages, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Crawl failed for project {ProjectId}", projectId);
            project.Status = ProjectStatus.Failed;
            return Problem($"Crawl failed: {ex.Message}", statusCode: 502);
        }

        project.CrawledSite = new CrawledSite
        {
            ProjectId = project.Id,
            SourceUrl = project.ProjectUrl,
            SiteName = result.SiteName,
            JsonLdBlocks = result.JsonLdBlocks,
            Headings = result.Headings,
            Paragraphs = result.Paragraphs,
            DetectedTone = result.DetectedTone,
            DetectedFocus = result.DetectedFocus,
            PagesCrawled = result.PagesCrawled
        };

        project.Status = ProjectStatus.ReadyForGeneration;
        project.UpdatedAtUtc = DateTime.UtcNow;

        return Ok(new CrawlSummaryResponse(
            result.SiteName, result.PagesCrawled, result.DetectedTone, result.DetectedFocus,
            result.Headings.Count, result.Paragraphs.Count, result.JsonLdBlocks.Count));
    }
}
