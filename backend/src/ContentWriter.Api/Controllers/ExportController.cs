using System.IO.Compression;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.Export;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}")]
public class ExportController : ControllerBase
{
    private readonly IMdxExportService _mdxExportService;
    private readonly IGeekatyourspotCommitService _commitService;
    private readonly ILogger<ExportController> _logger;

    public ExportController(
        IMdxExportService mdxExportService,
        IGeekatyourspotCommitService commitService,
        ILogger<ExportController> logger)
    {
        _mdxExportService = mdxExportService;
        _commitService = commitService;
        _logger = logger;
    }

    [HttpGet("export/mdx")]
    public async Task<IActionResult> ExportMdx(Guid projectId, bool includeRevise = false, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MdxDocument> documents;
        try
        {
            documents = await _mdxExportService.ExportAsync(projectId, includeRevise, cancellationToken);
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
    public async Task<IActionResult> CommitMdxExport(Guid projectId, bool includeRevise = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _commitService.CommitExportAsync(projectId, includeRevise, cancellationToken);
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
