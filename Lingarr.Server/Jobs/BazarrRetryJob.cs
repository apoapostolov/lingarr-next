using System.Collections.Concurrent;
using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Integration.Bazarr;
using Lingarr.Server.Services.Plugins;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Asks Bazarr again after a miss, until the retry window closes.
/// </summary>
public class BazarrRetryJob
{
    private static readonly ConcurrentDictionary<string, byte> Scheduled = new();

    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settings;
    private readonly ISubtitleService _subtitleService;
    private readonly IBazarrService _bazarr;
    private readonly IMediaSubtitleProcessor _processor;
    private readonly IBackgroundJobClient _jobs;
    private readonly PluginShelf _shelf;
    private readonly ILogger<BazarrRetryJob> _logger;

    public BazarrRetryJob(
        LingarrDbContext dbContext,
        ISettingService settings,
        ISubtitleService subtitleService,
        IBazarrService bazarr,
        IMediaSubtitleProcessor processor,
        IBackgroundJobClient jobs,
        PluginShelf shelf,
        ILogger<BazarrRetryJob> logger)
    {
        _dbContext = dbContext;
        _settings = settings;
        _subtitleService = subtitleService;
        _bazarr = bazarr;
        _processor = processor;
        _jobs = jobs;
        _shelf = shelf;
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
        if (media?.Path == null || media.FileName == null || !await _bazarr.IsEnabled())
        {
            return;
        }

        var sourceLanguages = await SourceLanguages();
        if (sourceLanguages.Count == 0)
        {
            return;
        }

        var policy = BazarrRetryPolicy.From(await _settings.GetSettings(BazarrRetryPolicy.Keys));
        if (await HasRealSource(media, sourceLanguages, policy.ReplaceOcr))
        {
            await AdoptAndQueue(media, mediaType, policy.ReplaceOcr);
            return;
        }

        var foundAt = NonTextSubtitlePolicy.FoundAt(media);
        var now = DateTime.UtcNow;
        if (!policy.AttemptAllowed(foundAt, now))
        {
            _logger.LogInformation("Bazarr retries for {File} stopped. The search window has closed.", media.FileName);
            return;
        }

        var found = await _bazarr.TryEnsureSource(media, mediaType, sourceLanguages, CancellationToken.None);
        if (found || await HasRealSource(media, sourceLanguages, policy.ReplaceOcr))
        {
            _logger.LogInformation("Bazarr found a source subtitle for {File} on a retry.", media.FileName);
            await AdoptAndQueue(media, mediaType, policy.ReplaceOcr);
            return;
        }

        await ScheduleNext(media, mediaType, policy, foundAt, now);
    }

    private async Task ScheduleNext(
        IMedia media,
        MediaType mediaType,
        BazarrRetryPolicy policy,
        DateTime? foundAt,
        DateTime now)
    {
        var delay = await _shelf.AdjustDelayAsync(
            policy.NextDelay(foundAt, now),
            "bazarr-miss",
            media.FileName,
            foundAt,
            now,
            policy.TimeoutHours);
        if (delay == null || delay <= TimeSpan.Zero || !TryMark(media.Id, mediaType))
        {
            return;
        }

        _jobs.Schedule<BazarrRetryJob>(job => job.Execute(media.Id, (int)mediaType), delay.Value);
        _logger.LogInformation(
            "Bazarr will search again for {File} in {Hours} hours.",
            media.FileName,
            delay.Value.TotalHours);
    }

    private async Task AdoptAndQueue(IMedia media, MediaType mediaType, bool replaceOcr)
    {
        if (replaceOcr && media.Path != null && media.FileName != null)
        {
            SubtitleNaming.RemoveOcrSidecars(media.Path, media.FileName);
        }

        LibraryFolderStamp.Write(media, null);
        media.MediaHash = string.Empty;
        await _processor.ProcessMedia(media, mediaType);
    }

    private async Task<bool> HasRealSource(IMedia media, HashSet<string> sourceLanguages, bool replaceOcr)
    {
        var subtitles = await _subtitleService.GetSubtitles(media.Path!, media.FileName!);
        return subtitles.Any(subtitle =>
            sourceLanguages.Any(code => string.Equals(code, subtitle.Language, StringComparison.OrdinalIgnoreCase))
            && !(replaceOcr && SubtitleNaming.IsOcr(subtitle.Caption)));
    }

    private async Task<HashSet<string>> SourceLanguages()
    {
        var languages = await _settings.GetSettingAsJson<Lingarr.Contracts.Models.SourceLanguage>(
            SettingKeys.Translation.SourceLanguages);
        return languages.Select(language => language.Code).ToHashSet();
    }

    private static string Key(int mediaId, MediaType mediaType) => $"{(int)mediaType}:{mediaId}";
}
