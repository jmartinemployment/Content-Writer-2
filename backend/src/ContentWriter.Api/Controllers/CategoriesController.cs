using ContentWriter.Application.Providers;
using ContentWriter.Application.Services.Publish;
using ContentWriter.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly IGeekBackendClient _geekBackendClient;
    private readonly ContentWriterDbContext _db;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(IGeekBackendClient geekBackendClient, ContentWriterDbContext db, ILogger<CategoriesController> logger)
    {
        _geekBackendClient = geekBackendClient;
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories(
        [FromQuery] Guid clientId, [FromQuery] string lang, CancellationToken cancellationToken)
    {
        var client = await _db.Clients
            .Include(c => c.PublishTarget)
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client is null)
        {
            return NotFound($"Client {clientId} was not found.");
        }

        try
        {
            var target = PublishTargetResolver.Resolve(client, requireAuthorId: false);
            var categories = await _geekBackendClient.GetCategoriesAsync(
                target, string.IsNullOrWhiteSpace(lang) ? "en" : lang, cancellationToken);
            return Ok(categories);
        }
        catch (ContentGenerationException ex)
        {
            return Problem(ex.Message, statusCode: 400, title: "Client publish target not configured");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch categories from GeekBackend");
            return Problem(ex.Message, statusCode: 502, title: "GeekBackend categories fetch failed");
        }
    }
}
