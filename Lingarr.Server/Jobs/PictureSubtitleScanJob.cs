using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Looks through the library for videos that have no English text subtitle
/// and a picture or caption track. Each run reads a short stretch of titles
/// and OCRs at most one, then queues the next stretch so translation can run
/// between files.
/// </summary>
public class PictureSubtitleScanJob
{
    private const int ProbeBudget = 40;
    private static int _gate;

    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settings;
    private readonly ISubtitleService _subtitleService;
    private readonly IMediaSubtitleProcessor _processor;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<PictureSubtitleScanJob> _logger;

    public PictureSubtitleScanJob(
        LingarrDbContext dbContext,
        ISettingService settings,
        ISubtitleService subtitleService,
        IMediaSubtitleProcessor processor,
        IBackgroundJobClient jobs,
        ILogger<PictureSubtitleScanJob> logger)
    {
        _dbContext = dbContext;
        _settings = settings;
        _subtitleService = subtitleService;
        _processor = processor;
        _jobs = jobs;
        _logger = logger;
    }

    public static bool IsRunning => Volatile.Read(ref _gate) == 1;

    public static bool TryEnter() => Interlocked.CompareExchange(ref _gate, 1, 0) == 0;

    public static void Exit() => Interlocked.Exchange(ref _gate, 0);

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [Queue("default")]
    public async Task Execute(int startIndex, int examined, int extracted, int prepared, int unread)
    {
        var finished = true;
        try
        {
            var policy = NonTextSubtitlePolicy.From(await _settings.GetSettings(NonTextSubtitlePolicy.Keys));
            if (!policy.AnyFormatEnabled)
            {
                await Finish(
                    "idle",
                    "Picture and caption formats are off, so the scan did not read the library.");
                return;
            }

            var movieIds = await _dbContext.Movies.OrderBy(movie => movie.Id).Select(movie => movie.Id).ToListAsync();
            var episodeIds = await _dbContext.Episodes.OrderBy(episode => episode.Id).Select(episode => episode.Id).ToListAsync();
            var total = movieIds.Count + episodeIds.Count;
            var index = Math.Max(0, startIndex);
            var probes = 0;

            while (index < total && probes < ProbeBudget)
            {
                var title = await LoadAsync(index, movieIds, episodeIds);
                index++;
                probes++;
                examined++;
                if (title?.Path == null || title.FileName == null || !Directory.Exists(title.Path))
                {
                    continue;
                }

                var subtitles = await _subtitleService.GetSubtitles(title.Path, title.FileName);
                if (subtitles.Any(subtitle => SubtitleNaming.NormalizeLanguage(subtitle.Language) == "en"))
                {
                    continue;
                }

                bool wrote;
                try
                {
                    wrote = await EmbeddedSubtitleExtractor.TryExtractEnglish(
                        title.Path,
                        title.FileName,
                        CancellationToken.None,
                        policy,
                        nonTextOnly: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    unread++;
                    _logger.LogWarning(exception, "Could not read picture subtitles for {File}", title.FileName);
                    continue;
                }

                if (!wrote)
                {
                    continue;
                }

                extracted++;
                var media = await LoadTrackedAsync(title);
                if (media != null)
                {
                    LibraryFolderStamp.Write(media, null);
                    media.MediaHash = string.Empty;
                    var mediaType = title.IsEpisode ? MediaType.Episode : MediaType.Movie;
                    if (await _processor.ProcessMedia(media, mediaType))
                    {
                        prepared++;
                    }
                }

                _logger.LogInformation("OCR wrote an English subtitle for {File}.", title.FileName);
                break;
            }

            var summary = Summary(examined, total, extracted, prepared, unread, finished: index >= total);
            if (index < total)
            {
                await _settings.SetSetting(SettingKeys.Subtitle.ScanStatus, "running");
                await _settings.SetSetting(SettingKeys.Subtitle.ScanSummary, summary);
                _jobs.Enqueue<PictureSubtitleScanJob>(job =>
                    job.Execute(index, examined, extracted, prepared, unread));
                finished = false;
                return;
            }

            await Finish("succeeded", summary);
            _logger.LogInformation("Picture subtitle scan finished. {Summary}", summary);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Picture subtitle scan stopped.");
            await Finish("failed", "Library scan stopped before it finished.");
        }
        finally
        {
            if (finished)
            {
                Exit();
            }
        }
    }

    private async Task<TitleRef?> LoadAsync(int index, List<int> movieIds, List<int> episodeIds)
    {
        if (index < movieIds.Count)
        {
            var id = movieIds[index];
            var movie = await _dbContext.Movies.AsNoTracking()
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
        var episode = await _dbContext.Episodes.AsNoTracking()
            .Where(item => item.Id == episodeId)
            .Select(item => new { item.Id, item.Path, item.FileName })
            .FirstOrDefaultAsync();
        return episode == null ? null : new TitleRef(episode.Id, episode.Path, episode.FileName, true);
    }

    private async Task<IMedia?> LoadTrackedAsync(TitleRef title)
    {
        if (title.IsEpisode)
        {
            return await _dbContext.Episodes.FirstOrDefaultAsync(episode => episode.Id == title.Id);
        }

        return await _dbContext.Movies.FirstOrDefaultAsync(movie => movie.Id == title.Id);
    }

    private sealed record TitleRef(int Id, string? Path, string? FileName, bool IsEpisode);

    private async Task Finish(string status, string summary)
    {
        await _settings.SetSetting(SettingKeys.Subtitle.ScanStatus, status);
        await _settings.SetSetting(SettingKeys.Subtitle.ScanSummary, summary);
    }

    private static string Summary(int examined, int total, int extracted, int prepared, int unread, bool finished)
    {
        var lead = finished ? "Finished." : "Scanning.";
        var unreadNote = unread > 0 ? $" {unread} videos could not be read." : "";
        return $"{lead} Looked at {examined} of {total} titles. Wrote {extracted} English subtitle files and queued {prepared} for translation.{unreadNote}";
    }
}
