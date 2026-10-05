using Lingarr.Server.Attributes;
using Lingarr.Server.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[LingarrAuthorize]
[Route("api/bazarr")]
public class BazarrController : ControllerBase
{
    private readonly IBazarrService _bazarr;

    public BazarrController(IBazarrService bazarr)
    {
        _bazarr = bazarr;
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken cancellationToken)
    {
        var (ok, message) = await _bazarr.Test(cancellationToken);
        return Ok(new { ok, message });
    }
}
