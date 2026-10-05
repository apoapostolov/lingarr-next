using System.Security.Cryptography;
using Hangfire;
using Lingarr.Contracts.Interfaces;
using Lingarr.Contracts.Models;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Integration.Bazarr;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class MediaSubtitleProcessor : IMediaSubtitleProcessor
{
    private readonly ITranslationRequestService _translationRequestService;
    private readonly ILogger<IMediaSubtitleProcessor> _logger;
    private readonly ISubtitleService _subtitleService;
    private readonly ISettingService _settingService;
    private readonly LingarrDbContext _dbContext;
    private readonly IBazarrService _bazarr;
    private readonly IBackgroundJobClient _jobs;
    private string _hash = string.Empty;
    private IMedia _media = null!;
    private MediaType _mediaType;

    public MediaSubtitleProcessor(
        ITranslationRequestService translationRequestService,
        ILogger<IMediaSubtitleProcessor> logger,
        ISettingService settingService,
        ISubtitleService subtitleService,
        LingarrDbContext dbContext,
        IBazarrService bazarr,
        IBackgroundJobClient jobs)
    {
        _translationRequestService = translationRequestService;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _dbContext = dbContext;
        _bazarr = bazarr;
        _jobs = jobs;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ProcessMedia(
        IMedia media, 
        MediaType mediaType)
    {
        if (media.Path == null || media.FileName == null)
        {
            _logger.LogWarning(
                "Skipping media processing: Path or FileName is null for {MediaType} with ID {MediaId}",
                mediaType, media.Id);
            return false;
        }

        if (LibraryFolderStamp.IsUnchanged(media.Path, LibraryFolderStamp.Read(media)))
        {
            return false;
        }
        
        var sourceLanguages = await GetLanguagesSetting<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
        var subtitles = await _subtitleService.GetSubtitles(media.Path, media.FileName);
        var bazarrPolicy = BazarrRetryPolicy.From(await _settingService.GetSettings(BazarrRetryPolicy.Keys));
        if (NeedsRealSource(subtitles, sourceLanguages, bazarrPolicy.ReplaceOcr))
        {
            var extractFirst = BazarrSourceOrder.ExtractFirst(
                await _settingService.GetSetting(SettingKeys.Integration.BazarrExtractFirst));
            var englishSource = BazarrSourceOrder.SourceIncludesEnglish(sourceLanguages);
            var picturePolicy = NonTextSubtitlePolicy.From(
                await _settingService.GetSettings(NonTextSubtitlePolicy.Keys)
                ?? new Dictionary<string, string>());
            if (extractFirst && englishSource)
            {
                var beforeBazarr = picturePolicy.LastResort ? picturePolicy.TextOnly() : picturePolicy;
                subtitles = await TryExtractSource(media, subtitles, sourceLanguages, beforeBazarr, nonTextOnly: false);
            }
            else if (!picturePolicy.LastResort && englishSource)
            {
                subtitles = await TryExtractSource(media, subtitles, sourceLanguages, picturePolicy, nonTextOnly: true);
            }

            if (NeedsRealSource(subtitles, sourceLanguages, bazarrPolicy.ReplaceOcr)
                && await _bazarr.IsEnabled())
            {
                if (await _bazarr.TryEnsureSource(media, mediaType, sourceLanguages, CancellationToken.None))
                {
                    subtitles = await AdoptDownloadedSubtitle(media, bazarrPolicy.ReplaceOcr);
                }
                else if (NeedsRealSource(subtitles, sourceLanguages, bazarrPolicy.ReplaceOcr))
                {
                    await ScheduleBazarrRetry(media, mediaType);
                }
            }

            if (_subtitleService.SelectSourceSubtitle(subtitles, sourceLanguages, "false") == null
                && picturePolicy.LastResort
                && englishSource)
            {
                var foundAt = NonTextSubtitlePolicy.FoundAt(media);
                if (picturePolicy.ConvertImagesNow(foundAt, DateTime.UtcNow))
                {
                    subtitles = await TryExtractSource(
                        media,
                        subtitles,
                        sourceLanguages,
                        picturePolicy,
                        nonTextOnly: false);
                }
                else
                {
                    ScheduleLastResort(media, mediaType, picturePolicy, foundAt);
                }
            }

            if (bazarrPolicy.ReplaceOcr
                && NeedsRealSource(subtitles, sourceLanguages, true)
                && await _bazarr.IsEnabled())
            {
                await ScheduleBazarrRetry(media, mediaType);
            }
        }

        ApplyCoverage(media, subtitles.Select(subtitle => subtitle.Language));
        if (!subtitles.Any())
        {
            await RememberFolder(media);
            return false;
        }

        var targetLanguages = await GetLanguagesSetting<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
        var ignoreCaptions = await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions) ?? "false";

        _media = media;
        _mediaType = mediaType;
        _hash = CreateHash(subtitles, sourceLanguages, targetLanguages, ignoreCaptions);
        if (!string.IsNullOrEmpty(media.MediaHash) && media.MediaHash == _hash)
        {
            await RememberFolder(media);
            return false;
        }
        
        _logger.LogInformation("Initiating subtitle processing.");
        return await ProcessSubtitles(subtitles, sourceLanguages, targetLanguages, ignoreCaptions);
    }

    private static bool NeedsRealSource(
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        bool replaceOcr)
    {
        return !subtitles.Any(subtitle =>
            sourceLanguages.Any(code => string.Equals(code, subtitle.Language, StringComparison.OrdinalIgnoreCase))
            && !(replaceOcr && SubtitleNaming.IsOcr(subtitle.Caption)));
    }

    private async Task<List<Subtitles>> AdoptDownloadedSubtitle(IMedia media, bool replaceOcr)
    {
        if (replaceOcr && media.Path != null && media.FileName != null)
        {
            var removed = SubtitleNaming.RemoveOcrSidecars(media.Path, media.FileName);
            if (removed > 0)
            {
                _logger.LogInformation(
                    "Replaced {Count} OCR subtitle file(s) for {File}.",
                    removed,
                    media.FileName);
            }
        }

        return await _subtitleService.GetSubtitles(media.Path!, media.FileName!);
    }

    private async Task<List<Subtitles>> TryExtractSource(
        IMedia media,
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        NonTextSubtitlePolicy policy,
        bool nonTextOnly)
    {
        if (_subtitleService.SelectSourceSubtitle(subtitles, sourceLanguages, "false") != null)
        {
            return subtitles;
        }

        try
        {
            if (await EmbeddedSubtitleExtractor.TryExtractEnglish(
                    media.Path!,
                    media.FileName!,
                    CancellationToken.None,
                    policy,
                    nonTextOnly))
            {
                _logger.LogInformation("Extracted an English subtitle from {File}.", media.FileName);
                return await _subtitleService.GetSubtitles(media.Path!, media.FileName!);
            }
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
            _logger.LogInformation(exception, "Could not extract an English subtitle from {File}.", media.FileName);
        }

        return subtitles;
    }

    private async Task ScheduleBazarrRetry(IMedia media, MediaType mediaType)
    {
        var policy = BazarrRetryPolicy.From(await _settingService.GetSettings(BazarrRetryPolicy.Keys));
        var foundAt = NonTextSubtitlePolicy.FoundAt(media);
        var delay = policy.NextDelay(foundAt, DateTime.UtcNow);
        if (delay == null || delay <= TimeSpan.Zero || !BazarrRetryJob.TryMark(media.Id, mediaType))
        {
            return;
        }

        try
        {
            _jobs.Schedule<BazarrRetryJob>(job => job.Execute(media.Id, (int)mediaType), delay.Value);
            _logger.LogInformation(
                "Bazarr will search again for {File} in {Hours} hours, and stops {Timeout} hours after the file was added.",
                media.FileName,
                policy.RetryHours,
                policy.TimeoutHours);
        }
        catch (Exception exception)
        {
            BazarrRetryJob.Clear(media.Id, mediaType);
            _logger.LogWarning(exception, "Could not schedule another Bazarr search for {File}.", media.FileName);
        }
    }

    private void ScheduleLastResort(
        IMedia media,
        MediaType mediaType,
        NonTextSubtitlePolicy policy,
        DateTime? foundAt)
    {
        var delay = policy.DelayUntilImages(foundAt, DateTime.UtcNow);
        if (delay == null || delay <= TimeSpan.Zero || !PictureSubtitleDeferredJob.TryMark(media.Id, mediaType))
        {
            return;
        }

        try
        {
            _jobs.Schedule<PictureSubtitleDeferredJob>(
                job => job.Execute(media.Id, (int)mediaType),
                delay.Value);
            _logger.LogInformation(
                "Picture subtitles for {File} wait {Hours} hours after the file was found.",
                media.FileName,
                policy.WaitHours);
        }
        catch (Exception exception)
        {
            PictureSubtitleDeferredJob.Clear(media.Id, mediaType);
            _logger.LogWarning(exception, "Could not schedule picture subtitle conversion for {File}.", media.FileName);
        }
    }

    /// <summary>
    /// Processes subtitle files for translation based on configured languages.
    /// </summary>
    /// <param name="subtitles">List of subtitle files to process.</param>
    /// <param name="sourceLanguages">The source languages.</param>
    /// <param name="targetLanguages">The target languages.</param>
    /// <param name="ignoreCaptions">The ignore captions setting.</param>
    /// <returns>True if new translation requests were created, false otherwise.</returns>
    private async Task<bool> ProcessSubtitles(
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        HashSet<string> targetLanguages,
        string ignoreCaptions)
    {
        if (sourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            _logger.LogWarning(
                "Source or target languages are empty. Source languages: {SourceCount}, Target languages: {TargetCount}",
                sourceLanguages.Count, targetLanguages.Count);
            await UpdateHash();
            return false;
        }

        var selected = _subtitleService.SelectSourceSubtitle(subtitles, sourceLanguages, ignoreCaptions);
        if (selected == null || !targetLanguages.Any())
        {
            // Common when target (e.g. bg) already exists and source en is gone — not an error condition.
            _logger.LogDebug(
                "No valid source language or target languages found for media |Green|{FileName}|/Green|. " +
                "Existing languages: |Red|{ExistingLanguages}|/Red|, " +
                "Source languages: |Red|{SourceLanguages}|/Red|, " +
                "Target languages: |Red|{TargetLanguages}|/Red|",
                string.Join(", ", _media?.FileName),
                string.Join(", ", subtitles.Select(s => s.Language.ToLowerInvariant())),
                string.Join(", ", sourceLanguages),
                string.Join(", ", targetLanguages));

            await UpdateHash();
            return false;
        }

        var languagesToTranslate = targetLanguages.Except(selected.AvailableLanguages).ToList();
        if (ignoreCaptions == "true")
        {
            var targetLanguagesWithCaptions = subtitles
                .Where(s => targetLanguages.Contains(s.Language) && !string.IsNullOrEmpty(s.Caption))
                .Select(s => s.Language)
                .Distinct()
                .ToList();

            if (targetLanguagesWithCaptions.Any())
            {
                _logger.LogInformation(
                    "Translation skipped because captions exist for target languages: |Green|{CaptionLanguages}|/Green| and ignoreCaptions is disabled",
                    string.Join(", ", targetLanguagesWithCaptions));
                await UpdateHash();
                return false;
            }
        }
        
        var activeStatuses = new[]
        {
            TranslationStatus.Pending,
            TranslationStatus.InProgress,
            TranslationStatus.Completed
        };
        var existingTranslationRequests = await _dbContext.TranslationRequests
            .Where(translationRequest => translationRequest.MediaId == _media.Id
                                         && translationRequest.MediaType == _mediaType
                                         && activeStatuses.Contains(translationRequest.Status))
            .Select(translationRequest => translationRequest.TargetLanguage)
            .Distinct()
            .ToListAsync();

        languagesToTranslate = languagesToTranslate.Except(existingTranslationRequests).ToList();
        if (!languagesToTranslate.Any())
        {
            await UpdateHash();
            return false;
        }

        foreach (var targetLanguage in languagesToTranslate)
        {
            await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
            {
                MediaId = _media.Id,
                MediaType = _mediaType,
                SubtitlePath = selected.Subtitle.Path,
                TargetLanguage = targetLanguage,
                SourceLanguage = selected.SourceLanguage,
                SubtitleFormat = selected.Subtitle.Format
            });
            _logger.LogInformation(
                "Initiating translation from |Orange|{sourceLanguage}|/Orange| to |Orange|{targetLanguage}|/Orange| for |Green|{subtitleFile}|/Green|",
                selected.SourceLanguage,
                targetLanguage,
                selected.Subtitle.Path);
        }

        await UpdateHash();
        return true;
    }

    /// <summary>
    /// Creates a hash of the current subtitle file state.
    /// </summary>
    /// <param name="subtitles">List of subtitle file paths to include in the hash.</param>
    /// <param name="sourceLanguages">The source languages.</param>
    /// <param name="targetLanguages">The target languages.</param>
    /// <param name="ignoreCaptions">The ignore captions setting.</param>
    /// <returns>A Base64 encoded string representing the hash of the current subtitle state.</returns>
    private string CreateHash(
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        HashSet<string> targetLanguages,
        string ignoreCaptions)
    {
        using var sha256 = SHA256.Create();
        var subtitlePaths = string.Join("|", subtitles.Select(subtitle => subtitle.Path)
            .ToList()
            .OrderBy(f => f));
        
        var sourceLangs = string.Join(",", sourceLanguages.OrderBy(l => l));
        var targetLangs = string.Join(",", targetLanguages.OrderBy(l => l));
        
        var hashInput = $"{subtitlePaths}|{sourceLangs}|{targetLangs}|{ignoreCaptions}";
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Retrieves language settings from the application configuration.
    /// </summary>
    /// <typeparam name="T">The type of language setting to retrieve (Source or Target).</typeparam>
    /// <param name="settingName">The name of the setting to retrieve.</param>
    /// <returns>A HashSet of language codes from the configuration.</returns>
    private async Task<HashSet<string>> GetLanguagesSetting<T>(string settingName) where T : class, ILanguage
    {
        var languages = await _settingService.GetSettingAsJson<T>(settingName);
        return languages
            .Select(lang => lang.Code)
            .ToHashSet();
    }

    /// <summary>
    /// Updates the media hash in the database.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task UpdateHash()
    {
        _media.MediaHash = _hash;
        LibraryFolderStamp.Write(_media, LibraryFolderStamp.Capture(_media.Path));
        _dbContext.Update(_media);
        await _dbContext.SaveChangesAsync();
    }

    private static void ApplyCoverage(IMedia media, IEnumerable<string> languages)
    {
        var coverage = LibraryCoverage.Format(languages);
        switch (media)
        {
            case Movie movie:
                movie.LanguageCoverage = coverage;
                break;
            case Episode episode:
                episode.LanguageCoverage = coverage;
                break;
        }
    }

    private async Task RememberFolder(IMedia media)
    {
        LibraryFolderStamp.Write(media, LibraryFolderStamp.Capture(media.Path));
        _dbContext.Update(media);
        await _dbContext.SaveChangesAsync();
    }
}