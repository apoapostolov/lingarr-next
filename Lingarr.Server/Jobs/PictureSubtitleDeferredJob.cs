using System.Collections.Concurrent;
using Hangfire;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Converts picture or caption tracks after the last-resort wait, once other
/// subtitle sources have had time to provide a text file.
/// </summary>
public class PictureSubtitleDeferredJob
{
    private static readonly ConcurrentDictionary<string, byte> Scheduled = new();

    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settings;
    private readonly ISubtitleService _subtitleService;
    private readonly IMediaSubtitleProcessor _processor;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<PictureSubtitleDeferredJob> _logger;

    public PictureSubtitleDeferredJob(
        LingarrDbContext dbContext,
        ISettingService settings,
        ISubtitleService subtitleService,
        IMediaSubtitleProcessor processor,
        IBackgroundJobClient jobs,
        ILogger<PictureSubtitleDeferredJob> logger)
    {
        _dbContext = dbContext;
        _settings = settings;
        _subtitleService = subtitleService;
        _processor = processor;
        _jobs = jobs;
        _logger = logger;
    }

    public static bool TryMark(int mediaId, MediaType mediaType) =>
        Scheduled.TryAdd(Key(mediaId, mediaType), 0);

    public static void Clear(int mediaId, MediaType mediaType) =>
        Scheduled.TryRemove(Key(mediaId, mediaType), out _);

    [AutomaticRetry(Attempts = 0)]
    [Queue("default")]
    public async Task Execute(int mediaId, int mediaTypeValue)
    {
        if (!Enum.IsDefined(typeof(MediaType), mediaTypeValue))
        {
            return;
        }

        var mediaType = (MediaType)mediaTypeValue;
        Clear(mediaId, mediaType);
        IMedia? media = mediaType == MediaType.Episode
            ? await _dbContext.Episodes.FirstOrDefaultAsync(episode => episode.Id == mediaId)
            : await _dbContext.Movies.FirstOrDefaultAsync(movie => movie.Id == mediaId);
        if (media?.Path == null || media.FileName == null)
        {
            return;
        }

        var policy = NonTextSubtitlePolicy.From(await _settings.GetSettings(NonTextSubtitlePolicy.Keys));
        var foundAt = NonTextSubtitlePolicy.FoundAt(media);
        var now = DateTime.UtcNow;
        if (!policy.ConvertImagesNow(foundAt, now))
        {
            var delay = policy.DelayUntilImages(foundAt, now);
            if (delay > TimeSpan.Zero && TryMark(mediaId, mediaType))
            {
                _jobs.Schedule<PictureSubtitleDeferredJob>(
                    job => job.Execute(mediaId, mediaTypeValue),
                    delay.Value);
            }

            return;
        }

        var subtitles = await _subtitleService.GetSubtitles(media.Path, media.FileName);
        if (subtitles.Any(subtitle => SubtitleNaming.NormalizeLanguage(subtitle.Language) == "en"))
        {
            return;
        }

        var wrote = await EmbeddedSubtitleExtractor.TryExtractEnglish(
            media.Path,
            media.FileName,
            CancellationToken.None,
            policy,
            nonTextOnly: true);
        if (!wrote)
        {
            _logger.LogInformation("No picture subtitle was converted for {File}.", media.FileName);
            return;
        }

        LibraryFolderStamp.Write(media, null);
        media.MediaHash = string.Empty;
        await _processor.ProcessMedia(media, mediaType);
        _logger.LogInformation("Converted a picture subtitle for {File} after the wait.", media.FileName);
    }

    private static string Key(int mediaId, MediaType mediaType) => $"{(int)mediaType}:{mediaId}";
}
