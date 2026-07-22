using System.IO.Compression;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.Export;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}")]
public class ExportController : ControllerBase
{
    private readonly IHtmlExportService _htmlExportService;
    private readonly IGeekatyourspotCommitService _commitService;
    private readonly ILogger<ExportController> _logger;

    public ExportController(
        IHtmlExportService htmlExportService,
        IGeekatyourspotCommitService commitService,
        ILogger<ExportController> logger)
    {
        _htmlExportService = htmlExportService;
        _commitService = commitService;
        _logger = logger;
    }

    [HttpGet("export/html")]
    public async Task<IActionResult> ExportHtml(Guid projectId, bool includeRevise = true, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ExportedHtmlDocument> documents;
        try
        {
            documents = await _htmlExportService.ExportAsync(projectId, includeRevise, cancellationToken);
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "HTML export failed for project {ProjectId}", projectId);
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
        return File(zipStream.ToArray(), "application/zip", $"{projectId}-html-export.zip");
    }

    [HttpPost("export/html/commit")]
    public async Task<IActionResult> CommitHtmlExport(Guid projectId, bool includeRevise = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _commitService.CommitExportAsync(projectId, includeRevise, cancellationToken);
            return Ok(new CommitHtmlExportResponse(result.CommitSha, result.CommitUrl, result.FilePaths));
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "Commit-to-geekatyourspot failed for project {ProjectId}", projectId);
            return Problem(ex.Message, statusCode: 400, title: "Commit failed");
        }
    }
}

public sealed record CommitHtmlExportResponse(string CommitSha, string CommitUrl, IReadOnlyList<string> FilePaths);
