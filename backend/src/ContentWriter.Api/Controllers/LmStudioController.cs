using ContentWriter.Application.Providers;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/llm/lm-studio")]
public class LmStudioController : ControllerBase
{
    private readonly LmStudioProvider _lmStudio;

    public LmStudioController(LmStudioProvider lmStudio)
    {
        _lmStudio = lmStudio;
    }

    [HttpGet("status")]
    public async Task<ActionResult<LmStudioHealthStatus>> Status(CancellationToken cancellationToken)
    {
        var status = await _lmStudio.CheckHealthAsync(cancellationToken);
        return Ok(status);
    }
}
