using ContentWriter.Api.Contracts;
using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.InMemory;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/crawl")]
public class CrawlController : ControllerBase
{
    private const int MaxFocusAttempts = 2;

    private readonly IProjectStore _projectStore;
    private readonly ISiteCrawlerService _crawlerService;
    private readonly IContentProviderFactory _providerFactory;
    private readonly IContentPromptBuilder _promptBuilder;
    private readonly ILogger<CrawlController> _logger;

    public CrawlController(
        IProjectStore projectStore,
        ISiteCrawlerService crawlerService,
        IContentProviderFactory providerFactory,
        IContentPromptBuilder promptBuilder,
        ILogger<CrawlController> logger)
    {
        _projectStore = projectStore;
        _crawlerService = crawlerService;
        _providerFactory = providerFactory;
        _promptBuilder = promptBuilder;
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

        // Word-frequency heuristic (result.DetectedFocus) is noisy on thin/marketing-heavy sites —
        // prefer LLM-extracted topic phrases, falling back to the heuristic if the model call fails.
        var detectedFocus = await TryExtractFocusWithLlmAsync(project, result, cancellationToken) ?? result.DetectedFocus;

        project.CrawledSite = new CrawledSite
        {
            ProjectId = project.Id,
            SourceUrl = project.ProjectUrl,
            SiteName = result.SiteName,
            JsonLdBlocks = result.JsonLdBlocks,
            Headings = result.Headings,
            Paragraphs = result.Paragraphs,
            DetectedTone = result.DetectedTone,
            DetectedFocus = detectedFocus,
            PagesCrawled = result.PagesCrawled
        };

        project.Status = ProjectStatus.ReadyForGeneration;
        project.UpdatedAtUtc = DateTime.UtcNow;

        return Ok(new CrawlSummaryResponse(
            result.SiteName, result.PagesCrawled, result.DetectedTone, detectedFocus,
            result.Headings.Count, result.Paragraphs.Count, result.JsonLdBlocks.Count));
    }

    private async Task<string?> TryExtractFocusWithLlmAsync(Project project, SiteCrawlResult result, CancellationToken cancellationToken)
    {
        if (result.Headings.Count == 0 && result.Paragraphs.Count == 0)
        {
            return null;
        }

        var provider = _providerFactory.Get(project.PreferredProvider);
        var prompt = _promptBuilder.BuildTopicFocusPrompt(result.SiteName, result.Headings, result.Paragraphs);

        for (var attempt = 1; attempt <= MaxFocusAttempts; attempt++)
        {
            try
            {
                var completion = await provider.CompleteAsync(prompt, cancellationToken);
                var parsed = LlmResponseJsonParser.Parse<TopicFocusResponse>(completion.Content, "topic focus");
                var phrases = (parsed.Focus ?? [])
                    .Select(p => p?.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList();

                if (phrases.Count > 0)
                {
                    return string.Join(", ", phrases);
                }

                _logger.LogWarning("LLM returned empty topic focus for project {ProjectId} (attempt {Attempt})", project.Id, attempt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Topic focus extraction failed for project {ProjectId} (attempt {Attempt})", project.Id, attempt);
            }
        }

        _logger.LogWarning("Falling back to heuristic DetectFocus for project {ProjectId} after {Attempts} failed attempts", project.Id, MaxFocusAttempts);
        return null;
    }
}
