using System.IO.Compression;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.Export;
using ContentWriter.Application.Services.Publish;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}")]
public class ExportController : ControllerBase
{
    private readonly IGeekBlogPublishService _publishService;
    private readonly IMdxExportService _mdxExportService;
    private readonly IGeekatyourspotCommitService _commitService;
    private readonly ILogger<ExportController> _logger;

    public ExportController(
        IGeekBlogPublishService publishService,
        IMdxExportService mdxExportService,
        IGeekatyourspotCommitService commitService,
        ILogger<ExportController> logger)
    {
        _publishService = publishService;
        _mdxExportService = mdxExportService;
        _commitService = commitService;
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

    [HttpGet("export/mdx")]
    public async Task<IActionResult> ExportMdx(Guid projectId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MdxDocument> documents;
        try
        {
            documents = await _mdxExportService.ExportAsync(projectId, cancellationToken);
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "MDX export failed for project {ProjectId}", projectId);
            return Problem(ex.Message, statusCode: 400, title: "Export failed");
        }

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var document in documents)
            {
                var entry = archive.CreateEntry(document.FileName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream);
                await writer.WriteAsync(document.Content);
            }
        }

        zipStream.Position = 0;
        return File(zipStream.ToArray(), "application/zip", $"{projectId}-mdx-export.zip");
    }

    [HttpPost("export/mdx/commit")]
    public async Task<IActionResult> CommitMdxExport(Guid projectId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _commitService.CommitExportAsync(projectId, cancellationToken);
            return Ok(new CommitMdxExportResponse(result.CommitSha, result.CommitUrl, result.FilePaths));
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "Commit-to-geekatyourspot failed for project {ProjectId}", projectId);
            return Problem(ex.Message, statusCode: 400, title: "Commit failed");
        }
    }
}

public sealed record CommitMdxExportResponse(string CommitSha, string CommitUrl, IReadOnlyList<string> FilePaths);

public sealed record PublishRequest(string? Department);

public sealed record PublishResponse(string CategorySlug, IReadOnlyList<PublishedPostResponse> Posts);

public sealed record PublishedPostResponse(
    string ContentType,
    int PostId,
    string Slug,
    string LanguageCode,
    int SectionCount,
    bool WasUpdated);
