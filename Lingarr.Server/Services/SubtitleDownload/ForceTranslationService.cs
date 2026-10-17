using Lingarr.Contracts.Interfaces;
using Lingarr.Contracts.Models;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Integration.Bazarr;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.SubtitleDownload;

public sealed class ForceTranslationService : IForceTranslationService
{
    private readonly LingarrDbContext _db;
    private readonly ISettingService _settings;
    private readonly ISubtitleService _subtitles;
    private readonly ISubtitleDownloadService _downloads;
    private readonly ITranslationRequestService _requests;
    private readonly ILogger<ForceTranslationService> _logger;

    public ForceTranslationService(
        LingarrDbContext db,
        ISettingService settings,
        ISubtitleService subtitles,
        ISubtitleDownloadService downloads,
        ITranslationRequestService requests,
        ILogger<ForceTranslationService> logger)
    {
        _db = db;
        _settings = settings;
        _subtitles = subtitles;
        _downloads = downloads;
        _requests = requests;
        _logger = logger;
    }

    public async Task<ForceTranslateResult> ForceAsync(
        int mediaId,
        MediaType mediaType,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var media = await LoadMedia(mediaId, mediaType, cancellationToken);
        if (media?.Path == null || media.FileName == null)
        {
            return new ForceTranslateResult
            {
                Message = mediaType == MediaType.Episode
                    ? "Lingarr could not find that episode."
                    : "Lingarr could not find that movie."
            };
        }

        var sourceLanguages = await LanguageCodes<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await LanguageCodes<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
        var subtitles = await _subtitles.GetSubtitles(media.Path, media.FileName);
        var hasSource = HasAnyLanguage(subtitles, sourceLanguages);

        if (overwrite && !hasSource)
        {
            return new ForceTranslateResult
            {
                SourceMissing = true,
                Message = "Lingarr needs a source subtitle before it can retranslate."
            };
        }

        var remaining = targetLanguages
            .Where(language => overwrite || !SubtitleDownloadService.HasLanguage(subtitles, language))
            .ToList();

        var requestIds = remaining.Count > 0 && hasSource
            ? await QueueRemaining(media, mediaType, subtitles, sourceLanguages, remaining)
            : [];

        media.MediaHash = null;
        _db.Update(media);
        await _db.SaveChangesAsync(cancellationToken);

        return new ForceTranslateResult
        {
            SourceMissing = !hasSource && remaining.Count > 0,
            RequestIds = requestIds,
            Message = BuildMessage(0, requestIds.Count, remaining.Count, hasSource)
        };
    }

    public async Task<ForceTranslateResult> DownloadAsync(
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken)
    {
        var media = await LoadMedia(mediaId, mediaType, cancellationToken);
        if (media?.Path == null || media.FileName == null)
        {
            return new ForceTranslateResult
            {
                Message = mediaType == MediaType.Episode
                    ? "Lingarr could not find that episode."
                    : "Lingarr could not find that movie."
            };
        }

        if (!await _downloads.IsEnabledAsync())
        {
            return new ForceTranslateResult
            {
                Message = "Lingarr subtitle downloaders are off."
            };
        }

        var chain = SubtitleDownloadChain.Parse(
            await _settings.GetSetting(SettingKeys.SubtitleDownload.Providers));
        if (chain.Count == 0)
        {
            return new ForceTranslateResult
            {
                Message = "Lingarr has no subtitle providers configured."
            };
        }

        var sourceLanguages = await LanguageCodes<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await LanguageCodes<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
        var subtitles = await ExtractConfiguredText(
            media, await _subtitles.GetSubtitles(media.Path, media.FileName), sourceLanguages, targetLanguages);

        var sourceHits = new List<string>();
        if (!HasAnyLanguage(subtitles, sourceLanguages) && sourceLanguages.Count > 0)
        {
            sourceHits = (await _downloads.DownloadLanguagesAsync(
                media,
                mediaType,
                sourceLanguages,
                subtitles,
                overwrite: false,
                cancellationToken)).ToList();
            if (sourceHits.Count > 0)
            {
                subtitles = await _subtitles.GetSubtitles(media.Path, media.FileName);
            }
        }

        var targetHits = (await _downloads.DownloadLanguagesAsync(
            media,
            mediaType,
            targetLanguages,
            subtitles,
            overwrite: false,
            cancellationToken)).ToList();
        if (targetHits.Count > 0)
        {
            subtitles = await _subtitles.GetSubtitles(media.Path, media.FileName);
        }

        var hasSource = HasAnyLanguage(subtitles, sourceLanguages);
        var remaining = targetLanguages
            .Where(language => !SubtitleDownloadService.HasLanguage(subtitles, language))
            .ToList();

        var requestIds = new List<int>();
        if (sourceHits.Count > 0 && targetHits.Count == 0 && remaining.Count > 0 && hasSource)
        {
            requestIds = await QueueRemaining(
                media,
                mediaType,
                subtitles,
                sourceLanguages,
                remaining);
        }

        media.MediaHash = null;
        _db.Update(media);
        await _db.SaveChangesAsync(cancellationToken);

        var downloaded = sourceHits.Concat(targetHits).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new ForceTranslateResult
        {
            SourceMissing = !hasSource && remaining.Count > 0,
            RequestIds = requestIds,
            Downloaded = downloaded,
            Message = BuildDownloadMessage(sourceHits.Count, requestIds.Count, downloaded.Count)
        };
    }

    private async Task<List<int>> QueueRemaining(
        IMedia media,
        MediaType mediaType,
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        IReadOnlyList<string> remaining)
    {
        var requestIds = new List<int>();
        var ignoreCaptions = await _settings.GetSetting(SettingKeys.Translation.IgnoreCaptions) ?? "false";
        var selected = _subtitles.SelectSourceSubtitle(subtitles, sourceLanguages, ignoreCaptions);
        if (selected == null)
        {
            return requestIds;
        }

        foreach (var target in remaining)
        {
            var id = await _requests.CreateRequest(new TranslateAbleSubtitle
            {
                MediaId = media.Id,
                MediaType = mediaType,
                SubtitlePath = selected.Subtitle.Path,
                SourceLanguage = selected.SourceLanguage,
                TargetLanguage = target,
                SubtitleFormat = selected.Subtitle.Format
            });
            requestIds.Add(id);
            _logger.LogInformation(
                "Force translation queued {Source} to {Target} for {Title}.",
                selected.SourceLanguage,
                target,
                media.Title);
        }

        return requestIds;
    }

    private async Task<List<Subtitles>> ExtractConfiguredText(
        IMedia media,
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        HashSet<string> targetLanguages)
    {
        var wanted = sourceLanguages.Concat(targetLanguages)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0 || media.Path == null || media.FileName == null)
        {
            return subtitles;
        }

        try
        {
            if (await EmbeddedSubtitleExtractor.TryExtractTextLanguages(
                    media.Path,
                    media.FileName,
                    wanted,
                    subtitles.Select(subtitle => subtitle.Language),
                    CancellationToken.None))
            {
                return await _subtitles.GetSubtitles(media.Path, media.FileName);
            }
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
            _logger.LogInformation(exception, "Could not extract embedded subtitles from {File}.", media.FileName);
        }

        return subtitles;
    }

    private async Task<IMedia?> LoadMedia(int mediaId, MediaType mediaType, CancellationToken cancellationToken)
    {
        return mediaType switch
        {
            MediaType.Movie => await _db.Movies.FirstOrDefaultAsync(movie => movie.Id == mediaId, cancellationToken),
            MediaType.Episode => await _db.Episodes
                .Include(episode => episode.Season)
                .ThenInclude(season => season.Show)
                .FirstOrDefaultAsync(episode => episode.Id == mediaId, cancellationToken),
            _ => null
        };
    }

    private async Task<HashSet<string>> LanguageCodes<T>(string key) where T : class, ILanguage
    {
        var languages = await _settings.GetSettingAsJson<T>(key);
        return languages.Select(language => language.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasAnyLanguage(IEnumerable<Subtitles> subtitles, IReadOnlySet<string> languages) =>
        subtitles.Any(subtitle =>
            BazarrSubtitleChoice.LanguageMatches(subtitle.Language, languages)
            && SubtitleBytes.IsUsableSidecar(subtitle.Path, subtitle.Language ?? ""));

    private static string BuildMessage(int downloaded, int queued, int remaining, bool hasSource)
    {
        if (downloaded > 0 && queued > 0)
        {
            return "Lingarr placed downloaded subtitles and queued translation.";
        }

        if (downloaded > 0 && remaining > 0 && !hasSource)
        {
            return "Lingarr placed downloaded subtitles and still needs a source subtitle to translate the rest.";
        }

        if (downloaded > 0)
        {
            return "Lingarr placed downloaded subtitles.";
        }

        if (queued > 0)
        {
            return "Lingarr queued translation for this title.";
        }

        if (remaining > 0 && !hasSource)
        {
            return "Lingarr needs a source subtitle before it can translate.";
        }

        return "Lingarr found nothing to translate.";
    }

    private static string BuildDownloadMessage(int sourceHits, int queued, int downloaded)
    {
        if (sourceHits > 0 && queued > 0)
        {
            return "Lingarr placed a source subtitle and queued translation.";
        }

        if (downloaded > 0)
        {
            return "Lingarr placed downloaded subtitles.";
        }

        return "Lingarr found no subtitles to download.";
    }
}
