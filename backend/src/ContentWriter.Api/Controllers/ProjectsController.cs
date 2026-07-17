using ContentWriter.Api.Contracts;
using ContentWriter.Application.Services;
using ContentWriter.Domain.Entities;
using ContentWriter.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly CompanyProfileOptions _companyProfile;

    public ProjectsController(IProjectRepository projectRepository, IOptions<CompanyProfileOptions> companyProfile)
    {
        _projectRepository = projectRepository;
        _companyProfile = companyProfile.Value;
    }

    [HttpPost]
    public async Task<ActionResult<ProjectSummaryResponse>> Create([FromBody] CreateProjectRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectUrl) || !Uri.IsWellFormedUriString(request.ProjectUrl, UriKind.Absolute))
        {
            return BadRequest("ProjectUrl must be a valid absolute URL.");
        }

        var existing = (await _projectRepository.ListAsync(
            p => p.TargetKeyword == request.TargetKeyword && p.ProjectUrl == request.ProjectUrl,
            cancellationToken)).FirstOrDefault();
        if (existing is not null)
        {
            return Ok(ToSummary(existing));
        }

        var project = new Project
        {
            Name = request.Name,
            ProjectUrl = request.ProjectUrl,
            TargetKeyword = request.TargetKeyword,
            PreferredProvider = request.PreferredProvider
        };

        await _projectRepository.AddAsync(project, cancellationToken);
        await _projectRepository.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = project.Id }, ToSummary(project));
    }

    private static readonly TimeSpan StaleProjectMaxAge = TimeSpan.FromHours(24);

    [HttpGet]
    public async Task<ActionResult<List<ProjectSummaryResponse>>> GetRecent(CancellationToken cancellationToken)
    {
        await _projectRepository.PurgeStaleAsync(StaleProjectMaxAge, cancellationToken);
        var projects = await _projectRepository.GetRecentAsync(cancellationToken: cancellationToken);
        return Ok(projects.Select(ToSummary).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDetailResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetWithDetailsAsync(id, cancellationToken);
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
            g.BodyHtml, g.JsonLdSchema, g.RelatedArticleUrl, g.CreatedAtUtc)).ToList();

        var contentSet = project.GeneratedContents.Count == 0
            ? null
            : GeneratedContentSetAssembler.Assemble(
                project, _companyProfile.ArticleBaseUrl, _companyProfile.BlogBaseUrl, _companyProfile.ToolBaseUrl);

        return Ok(new ProjectDetailResponse(
            project.Id, project.Name, project.ProjectUrl, project.TargetKeyword, project.Status,
            project.PreferredProvider, crawl, keywordSources, generatedContent, contentSet));
    }

    private static ProjectSummaryResponse ToSummary(Project project) => new(
        project.Id, project.Name, project.ProjectUrl, project.TargetKeyword,
        project.Status, project.PreferredProvider, project.CreatedAtUtc);
}
