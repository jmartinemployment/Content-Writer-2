using ContentWriter.Api.Contracts;
using ContentWriter.Application.Services;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/keyword-sources")]
public class KeywordSourcesController : ControllerBase
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB per manually-scraped HTML/text file

    private readonly IProjectRepository _projectRepository;
    private readonly IKeywordHtmlParserService _parserService;

    public KeywordSourcesController(IProjectRepository projectRepository, IKeywordHtmlParserService parserService)
    {
        _projectRepository = projectRepository;
        _parserService = parserService;
    }

    /// <summary>
    /// Uploads one manually-scraped input file: a keyword SERP result, an .edu/.gov/wikipedia page,
    /// a local pack result, a competitor crawl, or a People-Also-Asked text dump.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<KeywordSourceResponse>> Upload(
        Guid projectId, [FromForm] KeywordSourceCategory category, IFormFile file, CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound($"Project {projectId} was not found.");
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest("A non-empty file is required.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest($"File exceeds the {MaxFileSizeBytes / (1024 * 1024)}MB limit.");
        }

        string rawContent;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            rawContent = await reader.ReadToEndAsync(cancellationToken);
        }

        var parsed = _parserService.Parse(category, file.FileName, rawContent);

        var entity = new KeywordSource
        {
            ProjectId = projectId,
            Category = category,
            OriginalFileName = file.FileName,
            RawContent = rawContent,
            ExtractedTitle = parsed.Title,
            ExtractedHeadings = parsed.Headings,
            ExtractedParagraphs = parsed.Paragraphs,
            ExtractedQuestions = parsed.Questions
        };

        await _projectRepository.AddKeywordSourceAsync(entity, cancellationToken);
        await _projectRepository.SaveChangesAsync(cancellationToken);

        return Ok(new KeywordSourceResponse(
            entity.Id, entity.Category, entity.OriginalFileName, entity.ExtractedTitle,
            entity.ExtractedHeadings.Count, entity.ExtractedParagraphs.Count, entity.ExtractedQuestions.Count));
    }

    [HttpDelete("{keywordSourceId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid keywordSourceId, CancellationToken cancellationToken)
    {
        var sources = await _projectRepository.GetWithDetailsAsync(projectId, cancellationToken);
        var target = sources?.KeywordSources.FirstOrDefault(k => k.Id == keywordSourceId);
        if (target is null)
        {
            return NotFound();
        }

        _projectRepository.RemoveKeywordSource(target);
        await _projectRepository.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
