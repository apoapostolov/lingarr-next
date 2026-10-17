using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Server.Attributes;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Services.SubtitleDownload;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[LingarrAuthorize]
[Route("api/subtitle-download")]
public class SubtitleDownloadController : ControllerBase
{
    private readonly ISettingService _settings;
    private readonly IBackgroundJobClient _jobs;

    public SubtitleDownloadController(ISettingService settings, IBackgroundJobClient jobs)
    {
        _settings = settings;
        _jobs = jobs;
    }

    /// <summary>
    /// Lists every subtitle download provider ported from Bazarr, with settings fields.
    /// </summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<PluginResponse>> List()
    {
        return Ok(SubtitleDownloadCatalog.All.Select(ToResponse).ToList());
    }

    /// <summary>
    /// Returns the settings form for one subtitle download provider.
    /// </summary>
    [HttpGet("{provider}")]
    public ActionResult<PluginResponse> Get(string provider)
    {
        var definition = SubtitleDownloadCatalog.Find(provider);
        if (definition == null)
        {
            return NotFound();
        }

        return Ok(ToResponse(definition));
    }

    /// <summary>
    /// Status of the missing-English downloader sweep.
    /// </summary>
    [HttpGet("missing-english")]
    public async Task<IActionResult> MissingEnglishStatus()
    {
        var status = await _settings.GetSetting(SettingKeys.SubtitleDownload.MissingEnglishStatus) ?? "idle";
        var summary = await _settings.GetSetting(SettingKeys.SubtitleDownload.MissingEnglishSummary) ?? "";
        if (status == "running" && !MissingEnglishDownloadJob.IsRunning)
        {
            status = "idle";
            summary = string.IsNullOrWhiteSpace(summary)
                ? "Lingarr English subtitle download stopped before it finished."
                : "Lingarr English subtitle download stopped before it finished. " + summary;
            await _settings.SetSetting(SettingKeys.SubtitleDownload.MissingEnglishStatus, status);
            await _settings.SetSetting(SettingKeys.SubtitleDownload.MissingEnglishSummary, summary);
        }

        return Ok(new { status, summary });
    }

    /// <summary>
    /// Queues a library walk that asks downloaders for a missing English subtitle.
    /// </summary>
    [HttpPost("missing-english")]
    public async Task<IActionResult> StartMissingEnglish()
    {
        var enabled = await _settings.GetSetting(SettingKeys.SubtitleDownload.Enabled);
        if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                started = false,
                message = "Lingarr subtitle downloaders are off."
            });
        }

        if (SubtitleDownloadChain.Parse(
                await _settings.GetSetting(SettingKeys.SubtitleDownload.Providers)).Count == 0)
        {
            return Ok(new
            {
                started = false,
                message = "Lingarr has no subtitle providers configured."
            });
        }

        if (!MissingEnglishDownloadJob.TryEnter())
        {
            return Ok(new
            {
                started = false,
                message = "Lingarr is already downloading missing English subtitles."
            });
        }

        try
        {
            await _settings.SetSetting(SettingKeys.SubtitleDownload.MissingEnglishStatus, "running");
            await _settings.SetSetting(
                SettingKeys.SubtitleDownload.MissingEnglishSummary,
                "Lingarr queued a missing English subtitle download.");
            _jobs.Enqueue<MissingEnglishDownloadJob>(job => job.Execute(0, 0, 0, 0, 0, 0));
        }
        catch
        {
            MissingEnglishDownloadJob.Exit();
            await _settings.SetSetting(SettingKeys.SubtitleDownload.MissingEnglishStatus, "idle");
            throw;
        }

        return Ok(new
        {
            started = true,
            message = "Lingarr started downloading missing English subtitles."
        });
    }

    private static PluginResponse ToResponse(SubtitleProviderDefinition provider) => new()
    {
        Provider = provider.Id,
        DisplayName = provider.DisplayName,
        Description = provider.Description,
        IsBuiltIn = true,
        Settings = provider.ManifestSettings,
        HasRequestTemplate = false,
        SupportsInstructionProfiles = false,
        Enabled = true,
        Capabilities = ["subtitle-download"]
    };
}
