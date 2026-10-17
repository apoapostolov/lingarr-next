using Hangfire;
using Lingarr.Server.Attributes;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Services.Integration.Plex;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[LingarrAuthorize]
[Route("api/plex")]
public sealed class PlexController : ControllerBase
{
    private readonly IPlexAuthService _auth;
    private readonly PlexLibraryRefreshService _libraries;
    private readonly IBackgroundJobClient _jobs;

    public PlexController(
        IPlexAuthService auth,
        PlexLibraryRefreshService libraries,
        IBackgroundJobClient jobs)
    {
        _auth = auth;
        _libraries = libraries;
        _jobs = jobs;
    }

    [HttpGet("status")]
    public Task<PlexStatusResponse> GetStatus(CancellationToken cancellationToken) =>
        _auth.GetStatusAsync(cancellationToken);

    [HttpPost("oauth/pin")]
    public Task<PlexPinResponse> StartPin(CancellationToken cancellationToken) =>
        _auth.StartPinAsync(cancellationToken);

    [HttpPost("oauth/pin/{pinId:long}/poll")]
    public Task<PlexPollResponse> PollPin(long pinId, CancellationToken cancellationToken) =>
        _auth.PollPinAsync(pinId, cancellationToken);

    [HttpGet("servers")]
    public Task<IReadOnlyList<PlexServerResponse>> GetServers(CancellationToken cancellationToken) =>
        _auth.GetServersAsync(cancellationToken);

    [HttpPost("server")]
    public async Task<ActionResult<PlexStatusResponse>> SelectServer(
        [FromBody] PlexServerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _auth.SelectServerAsync(request, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { detail = exception.Message });
        }
    }

    [HttpPost("token")]
    public async Task<ActionResult<PlexStatusResponse>> SaveToken(
        [FromBody] PlexTokenRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _auth.SaveTokenAsync(request, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { detail = exception.Message });
        }
    }

    [HttpPost("test")]
    public Task<PlexTestResponse> Test(CancellationToken cancellationToken) =>
        _auth.TestAsync(cancellationToken);

    [HttpDelete("session")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _auth.LogoutAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("libraries")]
    public async Task<ActionResult<PlexLibrariesResponse>> GetLibraries(CancellationToken cancellationToken)
    {
        try
        {
            return await _libraries.ListAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { detail = "Lingarr could not list Plex libraries." });
        }
    }

    [HttpPost("libraries/{key}/refresh")]
    public async Task<ActionResult<PlexTestResponse>> RefreshLibrary(
        string key,
        CancellationToken cancellationToken)
    {
        if (PlexLibraryRefreshJob.IsBusy())
        {
            return Conflict(new { detail = "Plex library refresh is already running." });
        }

        try
        {
            var listed = await _libraries.ListAsync(cancellationToken);
            var library = listed.Libraries.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (library == null)
            {
                return NotFound(new { detail = "Plex has no library with that key." });
            }

            _jobs.Enqueue<PlexLibraryRefreshJob>(job => job.ExecuteSection(library.Key));
            return new PlexTestResponse
            {
                Ok = true,
                Message = $"Plex library refresh started for {library.Title}."
            };
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { detail = exception.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { detail = "Lingarr could not start the Plex library refresh." });
        }
    }
}
