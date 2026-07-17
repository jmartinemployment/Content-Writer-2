using ContentWriter.Api.Contracts;
using ContentWriter.Application.Services;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/crawl")]
public class CrawlController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly ISiteCrawlerService _crawlerService;
    private readonly ILogger<CrawlController> _logger;

    public CrawlController(IProjectRepository projectRepository, ISiteCrawlerService crawlerService, ILogger<CrawlController> logger)
    {
        _projectRepository = projectRepository;
        _crawlerService = crawlerService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<CrawlSummaryResponse>> CrawlProject(Guid projectId, [FromQuery] int maxPages = 50, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        project.Status = ProjectStatus.Crawling;
        _projectRepository.Update(project);
        await _projectRepository.SaveChangesAsync(cancellationToken);

        SiteCrawlResult result;
        try
        {
            result = await _crawlerService.CrawlAsync(project.ProjectUrl, maxPages, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Crawl failed for project {ProjectId}", projectId);
            project.Status = ProjectStatus.Failed;
            _projectRepository.Update(project);
            await _projectRepository.SaveChangesAsync(cancellationToken);
            return Problem($"Crawl failed: {ex.Message}", statusCode: 502);
        }

        var crawledSite = new CrawledSite
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

        await _projectRepository.SetCrawledSiteAsync(crawledSite, cancellationToken);

        project.Status = ProjectStatus.ReadyForGeneration;
        project.UpdatedAtUtc = DateTime.UtcNow;
        _projectRepository.Update(project);
        await _projectRepository.SaveChangesAsync(cancellationToken);

        return Ok(new CrawlSummaryResponse(
            result.SiteName, result.PagesCrawled, result.DetectedTone, result.DetectedFocus,
            result.Headings.Count, result.Paragraphs.Count, result.JsonLdBlocks.Count));
    }
}
