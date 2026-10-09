using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Contracts.Plugins;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

public class StatisticsJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleService _subtitleService;
    private readonly IScheduleService _scheduleService;
    private readonly ISettingService _settings;
    private readonly PluginShelf _shelf;
    private readonly ILogger<StatisticsJob> _logger;

    public StatisticsJob(
        LingarrDbContext dbContext,
        ISubtitleService subtitleService,
        IScheduleService scheduleService,
        ISettingService settings,
        PluginShelf shelf,
        ILogger<StatisticsJob> logger)
    {
        _dbContext = dbContext;
        _subtitleService = subtitleService;
        _scheduleService = scheduleService;
        _settings = settings;
        _shelf = shelf;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        if (await _settings.GetSetting(SettingKeys.Automation.LibraryDiskScanEnabled) == "false")
        {
            _logger.LogInformation("Library disk scan is paused. Statistics did not read subtitle folders.");
            await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
            return;
        }

        var movieSubtitles = 0;
        var episodeSubtitles = 0;
        var byLanguage = new Dictionary<string, int>();
        var coverageByLanguageAndMediaType = new Dictionary<string, int>();
        var processedPaths = new HashSet<string>();
        await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());
        
        var movies = await _dbContext.Movies.ToListAsync();
        foreach (var movie in movies)
        {
            try 
            {
                if (movie.Path == null)
                {
                    continue;
                }
                var subtitles = string.IsNullOrWhiteSpace(movie.FileName)
                    ? await _subtitleService.GetAllSubtitles(movie.Path)
                    : await _subtitleService.GetSubtitles(movie.Path, movie.FileName);
                movieSubtitles += subtitles.Count;
                
                // Group by language for language counts
                foreach (var subtitle in subtitles)
                {
                    if (!byLanguage.ContainsKey(subtitle.Language))
                    {
                        byLanguage[subtitle.Language] = 0;
                    }
                    byLanguage[subtitle.Language]++;
                }

                foreach (var language in subtitles
                             .Select(subtitle => subtitle.Language.ToLowerInvariant())
                             .Where(language => !string.IsNullOrWhiteSpace(language))
                             .Distinct())
                {
                    Increment(
                        coverageByLanguageAndMediaType,
                        CoverageKey("Movie", language));
                }
            }
            catch (DirectoryNotFoundException)
            {
                await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
                _logger.LogWarning("Directory not found for movie: {MovieTitle} at path: {Path}", 
                    movie.Title, movie.Path);
            }
        }

        var shows = await _dbContext.Shows
            .Include(s => s.Seasons)
            .ThenInclude(season => season.Episodes)
            .ToListAsync();

        foreach (var show in shows)
        {
            foreach (var season in show.Seasons)
            {
                if (string.IsNullOrEmpty(season.Path)) continue;
                
                if (processedPaths.Any(p => season.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                try
                {
                    var subtitles = await _subtitleService.GetAllSubtitles(season.Path);
                    episodeSubtitles += subtitles.Count;
                    processedPaths.Add(season.Path);
                    
                    // Group by language for language counts
                    foreach (var subtitle in subtitles)
                    {
                        if (!byLanguage.ContainsKey(subtitle.Language))
                        {
                            byLanguage[subtitle.Language] = 0;
                        }
                        byLanguage[subtitle.Language]++;
                    }

                    foreach (var episode in season.Episodes)
                    {
                        if (string.IsNullOrWhiteSpace(episode.FileName))
                        {
                            continue;
                        }

                        var episodeLanguages = subtitles
                            .Where(subtitle =>
                                subtitle.FileName.Equals(
                                    episode.FileName,
                                    StringComparison.OrdinalIgnoreCase) ||
                                subtitle.FileName.StartsWith(
                                    episode.FileName + ".",
                                    StringComparison.OrdinalIgnoreCase))
                            .Select(subtitle => subtitle.Language.ToLowerInvariant())
                            .Where(language => !string.IsNullOrWhiteSpace(language))
                            .Distinct();

                        foreach (var language in episodeLanguages)
                        {
                            Increment(
                                coverageByLanguageAndMediaType,
                                CoverageKey("Episode", language));
                        }
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
                    _logger.LogWarning("Directory not found for season at path: {Path}", season.Path);
                }
            }
        }
        
        // Update statistics
        var stats = await _dbContext.Statistics.SingleOrDefaultAsync();
        if (stats == null)
        {
            stats = new Statistics();
            _dbContext.Statistics.Add(stats);
        }
        stats.TotalEpisodes = await _dbContext.Episodes.CountAsync();
        stats.TotalMovies = movies.Count;
        stats.TotalSubtitles = movieSubtitles + episodeSubtitles;
        foreach (var (key, count) in coverageByLanguageAndMediaType)
        {
            byLanguage[key] = count;
        }
        stats.SubtitlesByLanguage = byLanguage;
        await _dbContext.SaveChangesAsync();
        await _shelf.ExportAsync(new StatisticsSnapshot
        {
            Movies = stats.TotalMovies,
            Episodes = stats.TotalEpisodes,
            SubtitleFiles = stats.TotalSubtitles
        });
        await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
    }

    private static string CoverageKey(string mediaType, string language) =>
        $"coverage:{mediaType}:{language.ToLowerInvariant()}";

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }
}
