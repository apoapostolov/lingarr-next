using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Integration.Plex;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

public class WebhookJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly IMediaService _mediaService;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly ISettingService _settings;
    private readonly IPlexClient _plex;
    private readonly IRadarrService _radarr;
    private readonly ISonarrService _sonarr;
    private readonly ILogger<WebhookJob> _logger;

    public WebhookJob(
        LingarrDbContext dbContext,
        IMediaService mediaService,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        ISettingService settings,
        IPlexClient plex,
        IRadarrService radarr,
        ISonarrService sonarr,
        ILogger<WebhookJob> logger)
    {
        _dbContext = dbContext;
        _mediaService = mediaService;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _settings = settings;
        _plex = plex;
        _radarr = radarr;
        _sonarr = sonarr;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 2 * 60)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessRadarrWebhook(RadarrWebhookPayload payload)
    {
        if (payload.Movie == null)
        {
            _logger.LogWarning("Radarr webhook payload has no movie data. Skipping.");
            return;
        }

        try
        {
            var movieId = await _mediaService.GetMovieIdOrSyncFromRadarrMovieId(payload.Movie.Id);
            if (movieId == 0)
            {
                _logger.LogWarning("Failed to sync or find movie with Radarr ID {RadarrId}. Movie may not have a file yet.",
                    payload.Movie.Id);
                return;
            }
            
            var movie = await _dbContext.Movies.FirstOrDefaultAsync(m => m.Id == movieId);
            if (movie == null)
            {
                _logger.LogError("Movie with ID {MovieId} not found in database after sync", movieId);
                return;
            }

            await _mediaSubtitleProcessor.ProcessMedia(movie, MediaType.Movie);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Radarr webhook for movie {MovieTitle} (Radarr ID: {RadarrId})",
                payload.Movie.Title, payload.Movie.Id);
            throw;
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 2 * 60)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessSonarrWebhook(SonarrWebhookPayload payload)
    {
        if (payload.Series == null || payload.Episodes == null || !payload.Episodes.Any())
        {
            _logger.LogWarning("Sonarr webhook payload has no series or episode data. Skipping.");
            return;
        }

        try
        {
            foreach (var episode in payload.Episodes)
            {
                var episodeId = await _mediaService.GetEpisodeIdOrSyncFromSonarrEpisodeId(episode.Id);
                if (episodeId == 0)
                {
                    _logger.LogWarning("Failed to sync or find episode with Sonarr ID {SonarrEpisodeId}. Episode may not have a file yet.",
                        episode.Id);
                    continue;
                }
                
                var episodeEntity = await _dbContext.Episodes.FirstOrDefaultAsync(e => e.Id == episodeId);
                if (episodeEntity == null)
                {
                    _logger.LogError("Episode with ID {EpisodeId} not found in database after sync", episodeId);
                    continue;
                }

                await _mediaSubtitleProcessor.ProcessMedia(episodeEntity, MediaType.Episode);
            }

            _logger.LogInformation("Completed processing Sonarr webhook for series: {SeriesTitle}", payload.Series.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Sonarr webhook for series {SeriesTitle} (Sonarr ID: {SonarrId})",
                payload.Series.Title, payload.Series.Id);
            throw;
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 2 * 60)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessPlexWebhook(PlexAddedMovie payload)
    {
        if (payload == null)
        {
            return;
        }

        try
        {
            if (string.Equals(payload.Kind, "episode", StringComparison.OrdinalIgnoreCase))
            {
                await TranslateAddedEpisode(payload, SettingKeys.MediaServers.PlexTranslateEpisodesOnLibraryNew, "Plex");
            }
            else
            {
                await TranslateAddedMovie(payload, SettingKeys.MediaServers.PlexTranslateMoviesOnLibraryNew, "Plex");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Plex webhook for {Title} ({RatingKey})",
                payload.Title, payload.RatingKey);
            throw;
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 2 * 60)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessJellyfinWebhook(PlexAddedMovie payload)
    {
        if (payload == null)
        {
            return;
        }

        try
        {
            if (string.Equals(payload.Kind, "episode", StringComparison.OrdinalIgnoreCase))
            {
                await TranslateAddedEpisode(payload, null, "Jellyfin");
            }
            else
            {
                await TranslateAddedMovie(payload, null, "Jellyfin");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Jellyfin webhook for {Title} ({ItemId})",
                payload.Title, payload.RatingKey);
            throw;
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 2 * 60)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessEmbyWebhook(PlexAddedMovie payload)
    {
        if (payload == null)
        {
            return;
        }

        try
        {
            if (string.Equals(payload.Kind, "episode", StringComparison.OrdinalIgnoreCase))
            {
                await TranslateAddedEpisode(payload, null, "Emby");
            }
            else
            {
                await TranslateAddedMovie(payload, null, "Emby");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Emby webhook for {Title} ({ItemId})",
                payload.Title, payload.RatingKey);
            throw;
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 2 * 60)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessPluginWebhook(string source, PlexAddedMovie payload)
    {
        if (payload == null)
        {
            return;
        }

        try
        {
            if (string.Equals(payload.Kind, "episode", StringComparison.OrdinalIgnoreCase))
            {
                await TranslateAddedEpisode(payload, null, source);
            }
            else
            {
                await TranslateAddedMovie(payload, null, source);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {Source} webhook for {Title}", source, payload.Title);
            throw;
        }
    }

    private async Task TranslateAddedMovie(PlexAddedMovie payload, string? settingKey, string source)
    {
        if (!await LibraryNewEnabled(settingKey, source, payload.Title, "movies"))
        {
            return;
        }

        var tags = PlexMovieTags.FromGuids(payload.Guids);
        if (tags.Count == 0 && !string.IsNullOrWhiteSpace(payload.RatingKey))
        {
            tags = await TagsFromPlexItem(payload.RatingKey);
        }

        if (tags.Count == 0)
        {
            _logger.LogInformation(
                "{Source} added {Title} ({ItemId}) without a tmdb, imdb, or tvdb id. Lingarr left it alone.",
                source,
                payload.Title,
                payload.RatingKey);
            return;
        }

        var movieIds = await FindMovieIds(tags);
        if (movieIds.Count == 0)
        {
            var synced = await TrySyncFromRadarr(tags);
            if (synced > 0)
            {
                movieIds.Add(synced);
            }
        }

        if (movieIds.Count == 0)
        {
            _logger.LogInformation(
                "{Source} added {Title}, and Lingarr has no movie tagged {Tags}.",
                source,
                payload.Title,
                string.Join(", ", tags));
            return;
        }

        foreach (var movieId in movieIds.Distinct())
        {
            var movie = await _dbContext.Movies.FirstOrDefaultAsync(item => item.Id == movieId);
            if (movie == null)
            {
                continue;
            }

            var created = await _mediaSubtitleProcessor.ProcessMedia(movie, MediaType.Movie);
            if (created)
            {
                _logger.LogInformation(
                    "{Source} added {Title}. Lingarr queued a translation for movie {MovieId}.",
                    source,
                    movie.Title,
                    movie.Id);
            }
            else
            {
                _logger.LogInformation(
                    "{Source} added {Title}. Movie {MovieId} was not queued. The source subtitle is missing, the target subtitle is already there, or a request already exists.",
                    source,
                    movie.Title,
                    movie.Id);
            }
        }
    }

    private async Task<IReadOnlyList<string>> TagsFromPlexItem(string ratingKey)
    {
        try
        {
            var credentials = await PlexCredentials.ResolveAsync(_settings);
            if (string.IsNullOrWhiteSpace(credentials.Url) || string.IsNullOrWhiteSpace(credentials.Token))
            {
                return [];
            }

            var metadata = await _plex.GetMetadataAsync(
                credentials.Url,
                credentials.Token,
                credentials.ClientId,
                ratingKey,
                CancellationToken.None);
            return PlexMovieTags.FromGuids(PlexWebhookReader.GuidsFromMetadata(metadata));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lingarr could not read Plex item {RatingKey} for a new movie.", ratingKey);
            return [];
        }
    }

    private async Task<List<int>> FindMovieIds(IReadOnlyList<string> tags)
    {
        var hits = new List<int>();
        foreach (var tag in tags)
        {
            var tagHits = await _dbContext.Movies
                .Where(movie =>
                    (movie.Path != null && movie.Path.Contains(tag))
                    || (movie.FileName != null && movie.FileName.Contains(tag)))
                .Select(movie => movie.Id)
                .ToListAsync();
            hits.AddRange(tagHits);
        }

        return hits.Distinct().ToList();
    }

    private async Task<int> TrySyncFromRadarr(IReadOnlyList<string> tags)
    {
        foreach (var tag in tags)
        {
            if (!PlexMovieTags.TryParse(tag, out var kind, out var id) || kind is not ("tmdb" or "imdb"))
            {
                continue;
            }

            try
            {
                var radarrMovie = await _radarr.FindLibraryMovie(kind, id);
                if (radarrMovie == null)
                {
                    continue;
                }

                var movieId = await _mediaService.GetMovieIdOrSyncFromRadarrMovieId(radarrMovie.Id);
                if (movieId > 0)
                {
                    return movieId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lingarr could not ask Radarr for {Tag}.", tag);
            }
        }

        return 0;
    }

    private async Task TranslateAddedEpisode(PlexAddedMovie payload, string? settingKey, string source)
    {
        if (!await LibraryNewEnabled(settingKey, source, payload.Title, "episodes"))
        {
            return;
        }

        await FillEpisode(payload);
        if (payload.SeasonNumber == null || payload.EpisodeNumber == null)
        {
            _logger.LogInformation(
                "{Source} added episode {Title} ({ItemId}) without a season and episode number. Lingarr left it alone.",
                source,
                payload.Title,
                payload.RatingKey);
            return;
        }

        var showTags = await ShowTags(payload);
        var episodeIds = await FindEpisodeIds(payload, showTags);
        if (episodeIds.Count == 0)
        {
            var synced = await TrySyncEpisodeFromSonarr(payload, showTags);
            if (synced > 0)
            {
                episodeIds.Add(synced);
            }
        }

        if (episodeIds.Count == 0)
        {
            _logger.LogInformation(
                "{Source} added {Show} S{Season}E{Episode}, and Lingarr has no matching episode.",
                source,
                payload.ShowTitle ?? payload.Title,
                payload.SeasonNumber,
                payload.EpisodeNumber);
            return;
        }

        foreach (var episodeId in episodeIds.Distinct())
        {
            var episode = await _dbContext.Episodes.FirstOrDefaultAsync(item => item.Id == episodeId);
            if (episode == null)
            {
                continue;
            }

            var created = await _mediaSubtitleProcessor.ProcessMedia(episode, MediaType.Episode);
            if (created)
            {
                _logger.LogInformation(
                    "{Source} added {Show} S{Season}E{Episode}. Lingarr queued a translation for episode {EpisodeId}.",
                    source,
                    payload.ShowTitle ?? episode.Title,
                    payload.SeasonNumber,
                    payload.EpisodeNumber,
                    episode.Id);
            }
            else
            {
                _logger.LogInformation(
                    "{Source} added {Show} S{Season}E{Episode}. Episode {EpisodeId} was not queued. The source subtitle is missing, the target subtitle is already there, or a request already exists.",
                    source,
                    payload.ShowTitle ?? episode.Title,
                    payload.SeasonNumber,
                    payload.EpisodeNumber,
                    episode.Id);
            }
        }
    }

    private async Task FillEpisode(PlexAddedMovie payload)
    {
        if (payload.SeasonNumber != null
            && payload.EpisodeNumber != null
            && !string.IsNullOrWhiteSpace(payload.ShowTitle))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(payload.RatingKey))
        {
            return;
        }

        var metadata = await ReadPlexMetadata(payload.RatingKey);
        if (metadata != null)
        {
            PlexWebhookReader.FillMissing(payload, metadata);
        }
    }

    private async Task<IReadOnlyList<string>> ShowTags(PlexAddedMovie payload)
    {
        var guids = new List<string>();
        foreach (var guid in payload.Guids)
        {
            if (TryReadShowGuid(guid, out var showGuid))
            {
                guids.Add(showGuid);
            }
        }

        if (!string.IsNullOrWhiteSpace(payload.ShowRatingKey))
        {
            var metadata = await ReadPlexMetadata(payload.ShowRatingKey);
            if (metadata != null)
            {
                guids.AddRange(PlexWebhookReader.GuidsFromMetadata(metadata));
            }
        }

        return PlexMovieTags.FromGuids(guids);
    }

    private async Task<List<int>> FindEpisodeIds(PlexAddedMovie payload, IReadOnlyList<string> showTags)
    {
        var seasonNumber = payload.SeasonNumber ?? -1;
        var episodeNumber = payload.EpisodeNumber ?? -1;
        var candidates = await (
            from episode in _dbContext.Episodes
            join season in _dbContext.Seasons on episode.SeasonId equals season.Id
            join show in _dbContext.Shows on season.ShowId equals show.Id
            where episode.EpisodeNumber == episodeNumber && season.SeasonNumber == seasonNumber
            select new EpisodeCandidate(episode.Id, episode.Path, episode.FileName, show.Title, show.Path))
            .ToListAsync();

        var tagged = candidates.Where(candidate => HasTag(candidate, showTags)).Select(candidate => candidate.Id).Distinct().ToList();
        if (tagged.Count > 0)
        {
            return tagged;
        }

        var titled = candidates
            .Where(candidate => SameShow(candidate.ShowTitle, payload.ShowTitle)
                || SameShow(LastSegment(candidate.ShowPath), payload.ShowTitle))
            .Select(candidate => candidate.Id)
            .Distinct()
            .ToList();
        if (titled.Count == 1)
        {
            return titled;
        }

        if (titled.Count > 1)
        {
            _logger.LogInformation(
                "A library event for {Show} S{Season}E{Episode} matched {Count} shows. Lingarr left it alone.",
                payload.ShowTitle,
                seasonNumber,
                episodeNumber,
                titled.Count);
        }

        return [];
    }

    private async Task<int> TrySyncEpisodeFromSonarr(PlexAddedMovie payload, IReadOnlyList<string> showTags)
    {
        if (payload.SeasonNumber == null || payload.EpisodeNumber == null)
        {
            return 0;
        }

        foreach (var tag in showTags)
        {
            if (!PlexMovieTags.TryParse(tag, out var kind, out var id))
            {
                continue;
            }

            try
            {
                var sonarrEpisode = await _sonarr.FindLibraryEpisode(
                    kind,
                    id,
                    payload.SeasonNumber.Value,
                    payload.EpisodeNumber.Value);
                if (sonarrEpisode == null)
                {
                    continue;
                }

                var episodeId = await _mediaService.GetEpisodeIdOrSyncFromSonarrEpisodeId(sonarrEpisode.Id);
                if (episodeId > 0)
                {
                    return episodeId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lingarr could not ask Sonarr for {Tag}.", tag);
            }
        }

        return 0;
    }

    private async Task<bool> LibraryNewEnabled(string? settingKey, string source, string title, string kind)
    {
        if (settingKey == null)
        {
            return true;
        }

        var enabled = await _settings.GetSetting(settingKey);
        if (string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        _logger.LogInformation(
            "{Source} added {Title}, and translation for new {Kind} is turned off.",
            source,
            title,
            kind);
        return false;
    }

    private async Task<string?> ReadPlexMetadata(string ratingKey)
    {
        try
        {
            var credentials = await PlexCredentials.ResolveAsync(_settings);
            if (string.IsNullOrWhiteSpace(credentials.Url) || string.IsNullOrWhiteSpace(credentials.Token))
            {
                return null;
            }

            return await _plex.GetMetadataAsync(
                credentials.Url,
                credentials.Token,
                credentials.ClientId,
                ratingKey,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lingarr could not read Plex item {RatingKey}.", ratingKey);
            return null;
        }
    }

    private static bool TryReadShowGuid(string guid, out string showGuid)
    {
        showGuid = string.Empty;
        if (string.IsNullOrWhiteSpace(guid))
        {
            return false;
        }

        var parts = guid.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4 || !parts[0].Contains(':'))
        {
            return false;
        }

        showGuid = parts[0] + "//" + parts[1];
        return true;
    }

    private static bool HasTag(EpisodeCandidate candidate, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
        {
            return false;
        }

        var haystack = (candidate.ShowPath ?? "") + "\n" + (candidate.Path ?? "") + "\n" + (candidate.FileName ?? "");
        return tags.Any(tag => haystack.Contains(tag, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameShow(string? candidate, string? plexTitle)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(plexTitle))
        {
            return false;
        }

        var left = PlexLookup.ToSlug(candidate);
        var right = PlexLookup.ToSlug(plexTitle);
        return left.Length >= 3 && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string? LastSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.TrimEnd('/', '\\');
        var slash = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private sealed record EpisodeCandidate(
        int Id,
        string? Path,
        string? FileName,
        string ShowTitle,
        string ShowPath);
}
