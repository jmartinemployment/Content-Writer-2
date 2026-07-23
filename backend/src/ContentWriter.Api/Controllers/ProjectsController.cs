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

        var crawl = project.CrawledSite is null ? null : new CrawlSummaryResponse(
            project.CrawledSite.SiteName, project.CrawledSite.PagesCrawled,
            project.CrawledSite.DetectedTone, project.CrawledSite.DetectedFocus,
            project.CrawledSite.Headings.Count, project.CrawledSite.Paragraphs.Count, project.CrawledSite.JsonLdBlocks.Count);

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

    private static ProjectSummaryResponse ToSummary(Project project) => new(
        project.Id, project.ClientId, project.Name, project.ProjectUrl, project.TargetKeyword, project.Department,
        project.Status, project.PreferredProvider, project.UseExactKeywordAsTitle, project.CreatedAtUtc);
}
