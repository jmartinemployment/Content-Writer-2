using ContentWriter.Application.Services.Publish;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly IGeekBackendClient _geekBackendClient;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(IGeekBackendClient geekBackendClient, ILogger<CategoriesController> logger)
    {
        _geekBackendClient = geekBackendClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories([FromQuery] string lang, CancellationToken cancellationToken)
    {
        try
        {
            var categories = await _geekBackendClient.GetCategoriesAsync(string.IsNullOrWhiteSpace(lang) ? "en" : lang, cancellationToken);
            return Ok(categories);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch categories from GeekBackend");
            return Problem(ex.Message, statusCode: 502, title: "GeekBackend categories fetch failed");
        }
    }
}
