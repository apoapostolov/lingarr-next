using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Integration.Bazarr;
using Lingarr.Server.Services.SubtitleDownload;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Asks configured subtitle downloaders for an English sidecar on every movie
/// and episode that does not already have one. It does not extract, ask Bazarr,
/// or queue translation.
/// </summary>
public class MissingEnglishDownloadJob
{
    public const int ExamineBudget = 80;
    public const int AttemptBudget = 3;
    private static readonly HashSet<string> English = new(StringComparer.OrdinalIgnoreCase) { "en" };
    private static int _gate;

    private readonly LingarrDbContext _db;
    private readonly ISettingService _settings;
    private readonly ISubtitleService _subtitles;
    private readonly ISubtitleDownloadService _downloads;
    private readonly ISubtitlePlacementService _placement;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<MissingEnglishDownloadJob> _logger;

    public MissingEnglishDownloadJob(
        LingarrDbContext db,
        ISettingService settings,
        ISubtitleService subtitles,
        ISubtitleDownloadService downloads,
        ISubtitlePlacementService placement,
        IBackgroundJobClient jobs,
        ILogger<MissingEnglishDownloadJob> logger)
    {
        _db = db;
        _settings = settings;
        _subtitles = subtitles;
        _downloads = downloads;
        _placement = placement;
        _jobs = jobs;
        _logger = logger;
    }

    public static bool IsRunning => Volatile.Read(ref _gate) == 1;

    public static bool TryEnter() => Interlocked.CompareExchange(ref _gate, 1, 0) == 0;

    public static void Exit() => Interlocked.Exchange(ref _gate, 0);

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 2)]
    [Queue("default")]
    public async Task Execute(
        int startIndex,
        int examined,
        int missing,
        int placed,
        int missed,
        int failed)
    {
        var finished = true;
        try
        {
            if (!await _downloads.IsEnabledAsync())
            {
                await Finish("idle", "Lingarr subtitle downloaders are off.");
                return;
            }

            var chain = SubtitleDownloadChain.Parse(
                await _settings.GetSetting(SettingKeys.SubtitleDownload.Providers));
            if (chain.Count == 0)
            {
                await Finish("idle", "Lingarr has no subtitle providers configured.");
                return;
            }

            var movieIds = await _db.Movies.OrderBy(movie => movie.Id).Select(movie => movie.Id).ToListAsync();
            var episodeIds = await _db.Episodes.OrderBy(episode => episode.Id).Select(episode => episode.Id).ToListAsync();
            var total = movieIds.Count + episodeIds.Count;
            var index = Math.Max(0, startIndex);
            var looks = 0;
            var attempts = 0;

            while (index < total && looks < ExamineBudget && attempts < AttemptBudget)
            {
                var title = await LoadAsync(index, movieIds, episodeIds);
                index++;
                looks++;
                examined++;
                if (title?.Path == null || title.FileName == null || !Directory.Exists(title.Path))
                {
                    continue;
                }

                List<Subtitles> subtitles;
                try
                {
                    subtitles = await _subtitles.GetSubtitles(title.Path, title.FileName);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failed++;
                    _logger.LogWarning(
                        exception,
                        "Lingarr could not read subtitles for {Title}.",
                        title.FileName);
                    continue;
                }

                if (HasEnglish(subtitles))
                {
                    continue;
                }

                missing++;
                attempts++;
                var media = await LoadTrackedAsync(title);
                if (media == null)
                {
                    failed++;
                    _logger.LogWarning("Lingarr could not load {Title} for an English download.", title.FileName);
                    continue;
                }

                var mediaType = title.IsEpisode ? MediaType.Episode : MediaType.Movie;
                try
                {
                    var query = SubtitleDownloadService.BuildQuery(media, mediaType, "en");
                    var hit = await _downloads.TryDownloadAsync(query, CancellationToken.None);
                    if (hit == null)
                    {
                        missed++;
                        _logger.LogWarning("Lingarr found no English subtitle for {Title}.", media.Title);
                        continue;
                    }

                    var path = await _placement.PlaceAsync(media, hit, "en", subtitles, CancellationToken.None);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        failed++;
                        _logger.LogWarning(
                            "Lingarr could not write an English subtitle for {Title}.",
                            media.Title);
                        continue;
                    }

                    media.MediaHash = string.Empty;
                    await _db.SaveChangesAsync();
                    placed++;
                    _logger.LogInformation(
                        "Lingarr placed an English subtitle for {Title} from {Provider}.",
                        media.Title,
                        hit.Provider);
                }
                catch (Exception exception)
                {
                    failed++;
                    _logger.LogError(
                        exception,
                        "Lingarr English subtitle download failed for {Title}.",
                        media.Title);
                }
            }

            var summary = FormatSummary(examined, total, missing, placed, missed, failed, index >= total);
            if (index < total)
            {
                await _settings.SetSetting(SettingKeys.SubtitleDownload.MissingEnglishStatus, "running");
                await _settings.SetSetting(SettingKeys.SubtitleDownload.MissingEnglishSummary, summary);
                _logger.LogInformation("Lingarr English subtitle download still running. {Summary}", summary);
                _jobs.Enqueue<MissingEnglishDownloadJob>(job =>
                    job.Execute(index, examined, missing, placed, missed, failed));
                finished = false;
                return;
            }

            await Finish("succeeded", summary);
            _logger.LogInformation("Lingarr English subtitle download finished. {Summary}", summary);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Lingarr English subtitle download stopped.");
            await Finish("failed", "Lingarr English subtitle download stopped before it finished.");
        }
        finally
        {
            if (finished)
            {
                Exit();
            }
        }
    }

    internal static bool HasEnglish(IEnumerable<Subtitles> subtitles) =>
        subtitles.Any(subtitle =>
            BazarrSubtitleChoice.LanguageMatches(subtitle.Language, English)
            && SubtitleBytes.IsUsableSidecar(subtitle.Path, subtitle.Language ?? "en"));

    internal static string FormatSummary(
        int examined,
        int total,
        int missing,
        int placed,
        int missed,
        int failed,
        bool finished)
    {
        var lead = finished ? "Finished." : "Downloading.";
        return $"{lead} Looked at {examined} of {total} titles. Missing English on {missing}. Placed {placed}. No match on {missed}. Failed on {failed}.";
    }

    private async Task<TitleRef?> LoadAsync(int index, List<int> movieIds, List<int> episodeIds)
    {
        if (index < movieIds.Count)
        {
            var id = movieIds[index];
            var movie = await _db.Movies.AsNoTracking()
                .Where(item => item.Id == id)
                .Select(item => new { item.Id, item.Path, item.FileName })
                .FirstOrDefaultAsync();
            return movie == null ? null : new TitleRef(movie.Id, movie.Path, movie.FileName, false);
        }

        var episodeIndex = index - movieIds.Count;
        if (episodeIndex >= episodeIds.Count)
        {
            return null;
        }

        var episodeId = episodeIds[episodeIndex];
        var episode = await _db.Episodes.AsNoTracking()
            .Where(item => item.Id == episodeId)
            .Select(item => new { item.Id, item.Path, item.FileName })
            .FirstOrDefaultAsync();
        return episode == null ? null : new TitleRef(episode.Id, episode.Path, episode.FileName, true);
    }

    private async Task<IMedia?> LoadTrackedAsync(TitleRef title)
    {
        if (title.IsEpisode)
        {
            return await _db.Episodes
                .Include(episode => episode.Season)
                .ThenInclude(season => season.Show)
                .FirstOrDefaultAsync(episode => episode.Id == title.Id);
        }

        return await _db.Movies.FirstOrDefaultAsync(movie => movie.Id == title.Id);
    }

    private async Task Finish(string status, string summary)
    {
        await _settings.SetSetting(SettingKeys.SubtitleDownload.MissingEnglishStatus, status);
        await _settings.SetSetting(SettingKeys.SubtitleDownload.MissingEnglishSummary, summary);
    }

    private sealed record TitleRef(int Id, string? Path, string? FileName, bool IsEpisode);
}
