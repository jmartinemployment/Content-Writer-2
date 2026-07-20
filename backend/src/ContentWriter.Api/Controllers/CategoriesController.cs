using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

public sealed record CategoryResponse(int Id, string Slug);

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    // Temporary static list — GeekBackend's live categories endpoint requires GeekOAuth
    // (which authenticates users, not this service-to-service call), so that fetch is
    // disabled for now. Replace with a live call once GeekBackend publish is reinstated.
    private static readonly IReadOnlyList<CategoryResponse> StaticCategories =
    [
        new(1, "Accounting"),
        new(2, "Customer Service"),
        new(3, "Human Resource"),
        new(4, "Marketing"),
        new(5, "Sales"),
    ];

    [HttpGet]
    public IActionResult GetCategories([FromQuery] Guid clientId, [FromQuery] string lang) =>
        Ok(StaticCategories);
}
