using Lingarr.Contracts.Models;
using Lingarr.Core.Configuration;
using Lingarr.Server.Attributes;
using Microsoft.AspNetCore.Mvc;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Lingarr.Server.Services.Translation;

namespace Lingarr.Server.Controllers;

[ApiController]
[LingarrAuthorize]
[Route("api/[controller]")]
public class TranslateController : ControllerBase
{
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly ITranslationRequestService _translationRequestService;
    private readonly ISettingService _settings;
    private readonly LanguageCodeService _languageCodeService;
    private readonly ILogger<TranslateController> _logger;
    private readonly IProviderHealthService _providerHealth;
    private readonly ITranslationPromptProfileService _promptProfiles;

    public TranslateController(
        ITranslationServiceFactory translationServiceFactory,
        ITranslationRequestService translationRequestService,
        ISettingService settings,
        LanguageCodeService languageCodeService,
        IProviderHealthService providerHealth,
        ITranslationPromptProfileService promptProfiles,
        ILogger<TranslateController> logger)
    {
        _translationServiceFactory = translationServiceFactory;
        _translationRequestService = translationRequestService;
        _settings = settings;
        _languageCodeService = languageCodeService;
        _providerHealth = providerHealth;
        _promptProfiles = promptProfiles;
        _logger = logger;
    }

    /// <summary>
    /// Initiates a translation job for the provided subtitle data.
    /// </summary>
    /// <param name="translateAbleSubtitle">The subtitle data to be translated. 
    /// This includes the subtitle path, subtitle source language and subtitle target language.</param>
    /// <returns>Returns an HTTP 200 OK response if the job was successfully enqueued.</returns>
    [HttpPost("file")]
    public async Task<ActionResult<TranslationJobDto>> Translate([FromBody] TranslateAbleSubtitle translateAbleSubtitle)
    {
        var jobId = await _translationRequestService.CreateRequest(translateAbleSubtitle);
        return Ok(new TranslationJobDto
        {
            JobId = jobId,
        });
    }

    /// <summary>
    /// Initiates translation jobs for multiple media items.
    /// Handles subtitle discovery and source language resolution server-side.
    /// </summary>
    /// <param name="request">The bulk translate request containing media IDs, target language, and media type.</param>
    /// <returns>Returns a list of created translation request IDs.</returns>
    [HttpPost("bulk")]
    public async Task<ActionResult> BulkTranslate([FromBody] BulkTranslateRequest request)
    {
        try
        {
            await _translationRequestService.CreateBulkRequest(request);
            return Ok();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Translate a single subtitle line
    /// </summary>
    /// <param name="translateAbleSubtitleLine">The subtitle to be translated. 
    /// This includes the subtitle line, subtitle source language and subtitle target language.</param>
    /// <param name="cancellationToken">Token to cancel the translation operation</param>
    /// <returns>Returns translated string if the translation was successful.</returns>
    [HttpPost("line")]
    public async Task<string> TranslateLine(
        [FromBody] TranslateAbleSubtitleLine translateAbleSubtitleLine,
        CancellationToken cancellationToken)
    {
        var chain = TranslationChain.Parse(await _settings.GetSetting(SettingKeys.Translation.ServiceType));
        await _promptProfiles.ResolveChainAsync(chain, cancellationToken: cancellationToken);
        TranslationChain.StampLanguages(
            chain,
            translateAbleSubtitleLine.SourceLanguage,
            translateAbleSubtitleLine.TargetLanguage);
        var subtitleTranslator = new SubtitleTranslationService(
            _translationServiceFactory.CreateTranslationServices(chain),
            _logger,
            providerHealth: _providerHealth);

        if (translateAbleSubtitleLine.SubtitleLine == "")
        {
            return translateAbleSubtitleLine.SubtitleLine;
        }

        var stripHtml = SubtitleHtml.Enabled(await _settings.GetSetting(SettingKeys.Translation.StripSubtitleHtml));
        if (stripHtml)
        {
            translateAbleSubtitleLine.SubtitleLine = SubtitleHtml.Strip(translateAbleSubtitleLine.SubtitleLine);
        }

        var result = await subtitleTranslator.TranslateSubtitleLine(translateAbleSubtitleLine, cancellationToken);
        return stripHtml ? SubtitleHtml.Strip(result.Translation) : result.Translation;
    }

    /// <summary>
    /// Translates subtitle content, supporting both single line and batch translation.
    /// </summary>
    /// <param name="translateAbleSubtitleContent">The translation request containing one or more subtitle items</param>
    /// <param name="cancellationToken">Token to cancel the translation operation</param>
    /// <returns>Translated subtitle content</returns>
    [HttpPost("content")]
    public async Task<ActionResult<BatchTranslatedLine[]>> TranslateContent(
        [FromBody] TranslateAbleSubtitleContent translateAbleSubtitleContent,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await _translationRequestService.TranslateContentAsync(translateAbleSubtitleContent, cancellationToken);
            return Ok(results);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Returns the canonical culture list used by the language picker.
    /// Per-service capability gating happens at translate time.
    /// </summary>
    /// <returns>A list of cultures with code and English name. Targets is always empty.</returns>
    [HttpGet("languages")]
    public ActionResult<IReadOnlyList<SourceLanguage>> GetLanguages()
    {
        var languageCodes = _languageCodeService.GetSupportedLanguages();
        return Ok(languageCodes);
    }

}
