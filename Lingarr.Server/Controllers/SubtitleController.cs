using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Server.Attributes;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[LingarrAuthorize]
[Route("api/[controller]")]
public class SubtitleController : ControllerBase
{
    private readonly ISubtitleService _subtitleService;
    private readonly ISettingService _settings;
    private readonly IBackgroundJobClient _jobs;

    public SubtitleController(
        ISubtitleService subtitleService,
        ISettingService settings,
        IBackgroundJobClient jobs)
    {
        _subtitleService = subtitleService;
        _settings = settings;
        _jobs = jobs;
    }

    [HttpGet("picture-scan")]
    public async Task<IActionResult> PictureScanStatus()
    {
        var status = await _settings.GetSetting(SettingKeys.Subtitle.ScanStatus) ?? "idle";
        var summary = await _settings.GetSetting(SettingKeys.Subtitle.ScanSummary) ?? "";
        if (status == "running" && !PictureSubtitleScanJob.IsRunning)
        {
            status = "idle";
            summary = string.IsNullOrWhiteSpace(summary)
                ? "Library scan stopped before it finished."
                : "Library scan stopped before it finished. " + summary;
            await _settings.SetSetting(SettingKeys.Subtitle.ScanStatus, status);
            await _settings.SetSetting(SettingKeys.Subtitle.ScanSummary, summary);
        }

        return Ok(new { status, summary });
    }

    [HttpPost("picture-scan")]
    public async Task<IActionResult> StartPictureScan()
    {
        var policy = NonTextSubtitlePolicy.From(await _settings.GetSettings(NonTextSubtitlePolicy.Keys));
        if (!policy.AnyFormatEnabled)
        {
            return Ok(new
            {
                started = false,
                message = "Enable a picture or caption format before starting the library scan."
            });
        }

        if (!PictureSubtitleScanJob.TryEnter())
        {
            return Ok(new { started = false, message = "A library scan is already running." });
        }

        try
        {
            await _settings.SetSetting(SettingKeys.Subtitle.ScanStatus, "running");
            await _settings.SetSetting(
                SettingKeys.Subtitle.ScanSummary,
                "Library scan queued.");
            _jobs.Enqueue<PictureSubtitleScanJob>(job => job.Execute(0, 0, 0, 0, 0));
        }
        catch
        {
            PictureSubtitleScanJob.Exit();
            await _settings.SetSetting(SettingKeys.Subtitle.ScanStatus, "idle");
            throw;
        }

        return Ok(new
        {
            started = true,
            message = "Library scan started."
        });
    }
    
    /// <summary>
    /// Retrieves a list of subtitle files located at the specified path.
    /// </summary>
    /// <param name="subtitlePath">The directory path to search for subtitle files.This path is relative to the media folder
    /// and should not start with a forward slash.</param>
    /// <returns>Returns an HTTP 200 OK response with a list of <see cref="Subtitles"/> objects found at the specified path.</returns>
    [HttpPost("all")]
    public async Task<ActionResult<List<Subtitles>>> GetAllSubtitles([FromBody] SubtitlePath subtitlePath)
    {
        var value = await _subtitleService.GetAllSubtitles(subtitlePath.Path);
        return Ok(value);
    }
}