using ContentWriter.Api.Contracts;
using ContentWriter.Application.Services;
using ContentWriter.Application.Services.Export;
using ContentWriter.Domain.Entities;
using ContentWriter.Infrastructure.InMemory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectStore _projectStore;
    private readonly CompanyProfileOptions _companyProfile;

    public ProjectsController(IProjectStore projectStore, IOptions<CompanyProfileOptions> companyProfile)
    {
        _projectStore = projectStore;
        _companyProfile = companyProfile.Value;
    }

    [HttpPost]
    public async Task<ActionResult<ProjectSummaryResponse>> Create([FromBody] CreateProjectRequest request, CancellationToken cancellationToken)
    {
        if (request.ClientId == Guid.Empty)
        {
            return BadRequest("ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ProjectUrl) || !Uri.IsWellFormedUriString(request.ProjectUrl, UriKind.Absolute))
        {
            return BadRequest("ProjectUrl must be a valid absolute URL.");
        }

        if (!Departments.IsValid(request.Department))
        {
            return BadRequest($"Department must be one of: {string.Join(", ", Departments.Slugs)}.");
        }

        var existing = (await _projectStore.ListAsync(
            p => p.TargetKeyword == request.TargetKeyword && p.ProjectUrl == request.ProjectUrl,
            cancellationToken)).FirstOrDefault();
        if (existing is not null)
        {
            return Ok(ToSummary(existing));
        }

        var project = new Project
        {
            ClientId = request.ClientId,
            Name = request.Name,
            ProjectUrl = request.ProjectUrl,
            TargetKeyword = request.TargetKeyword,
            Department = request.Department,
            PreferredProvider = request.PreferredProvider,
            UseExactKeywordAsTitle = request.UseExactKeywordAsTitle
        };

        await _projectStore.AddAsync(project, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = project.Id }, ToSummary(project));
    }

    private static readonly TimeSpan StaleProjectMaxAge = TimeSpan.FromHours(24);

    [HttpGet]
    public async Task<ActionResult<List<ProjectSummaryResponse>>> GetRecent(CancellationToken cancellationToken)
    {
        await _projectStore.PurgeStaleAsync(StaleProjectMaxAge, cancellationToken);
        var projects = await _projectStore.GetRecentAsync(cancellationToken: cancellationToken);
        return Ok(projects.Select(ToSummary).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDetailResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var project = await _projectStore.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var crawl = project.CrawledSite is null ? null : ToCrawlSummary(project.CrawledSite);

        var keywordSources = project.KeywordSources.Select(k => new KeywordSourceResponse(
            k.Id, k.Category, k.OriginalFileName, k.ExtractedTitle,
            k.ExtractedHeadings.Count, k.ExtractedParagraphs.Count, k.ExtractedQuestions.Count)).ToList();

        var generatedContent = project.GeneratedContents.Select(g => new GeneratedContentResponse(
            g.Id, g.ContentType, g.Title, g.Slug, g.MetaDescription, g.Keywords, g.WordCount,
            g.Body is null ? string.Empty : SectionHtmlRenderer.RenderFragment(g.Body),
            g.JsonLdSchema, g.RelatedArticleUrl, g.CreatedAtUtc)).ToList();

        var contentSet = project.GeneratedContents.Count == 0
            ? null
            : GeneratedContentSetAssembler.Assemble(
                project, project.Department, _companyProfile.ArticleBaseUrl, _companyProfile.BlogBaseUrl, _companyProfile.ToolBaseUrl);

        return Ok(new ProjectDetailResponse(
            project.Id, project.ClientId, project.Name, project.ProjectUrl, project.TargetKeyword, project.Department, project.Status,
            project.PreferredProvider, project.UseExactKeywordAsTitle, crawl, keywordSources, generatedContent, contentSet));
    }

    [HttpPatch("{id:guid}/tone")]
    public async Task<ActionResult<CrawlSummaryResponse>> UpdateTone(
        Guid id, [FromBody] UpdateProjectToneRequest request, CancellationToken cancellationToken)
    {
        var project = await _projectStore.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (project.CrawledSite is null)
        {
            return BadRequest("Crawl the project site before setting tone.");
        }

        if (!BrandTones.IsValid(request.ToneId))
        {
            return BadRequest($"ToneId must be one of: {string.Join(", ", BrandTones.All.Select(t => t.Id))}.");
        }

        project.CrawledSite.DetectedTone = request.ToneId.Trim().ToLowerInvariant();
        project.UpdatedAtUtc = DateTime.UtcNow;

        return Ok(ToCrawlSummary(project.CrawledSite));
    }

    private static CrawlSummaryResponse ToCrawlSummary(CrawledSite crawl) => new(
        crawl.SiteName, crawl.PagesCrawled,
        BrandTones.MapFromDetected(crawl.DetectedTone), crawl.DetectedFocus,
        crawl.Headings.Count, crawl.Paragraphs.Count, crawl.JsonLdBlocks.Count);

    private static ProjectSummaryResponse ToSummary(Project project) => new(
        project.Id, project.ClientId, project.Name, project.ProjectUrl, project.TargetKeyword, project.Department,
        project.Status, project.PreferredProvider, project.UseExactKeywordAsTitle, project.CreatedAtUtc);
}
