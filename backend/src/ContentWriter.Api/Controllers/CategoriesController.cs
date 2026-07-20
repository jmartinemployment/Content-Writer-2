using ContentWriter.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

public sealed record CategoryResponse(int Id, string Slug, string Name);

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    // Temporary static list — GeekBackend's live categories endpoint requires GeekOAuth
    // (which authenticates users, not this service-to-service call), so that fetch is
    // disabled for now. Replace with a live call once GeekBackend publish is reinstated.
    private static readonly IReadOnlyList<CategoryResponse> StaticCategories =
        Departments.Slugs.Select((slug, i) => new CategoryResponse(i + 1, slug, Departments.DisplayName(slug))).ToList();

    [HttpGet]
    public IActionResult GetCategories([FromQuery] Guid clientId, [FromQuery] string lang) =>
        Ok(StaticCategories);
}
