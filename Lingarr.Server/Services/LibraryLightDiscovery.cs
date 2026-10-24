using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Services.Integration.Plex;
using Lingarr.Server.Services.SubtitleDownload;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class LibraryLightDiscovery : ILibraryLightDiscovery
{
    private const int PlexPageSize = 40;

    private readonly LingarrDbContext _db;
    private readonly ISettingService _settings;
    private readonly IPlexClient _plex;
    private readonly IRadarrService _radarr;
    private readonly ISonarrService _sonarr;
    private readonly IMediaService _media;
    private readonly IMediaSubtitleProcessor _processor;
    private readonly ILogger<LibraryLightDiscovery> _logger;

    public LibraryLightDiscovery(
        LingarrDbContext db,
        ISettingService settings,
        IPlexClient plex,
        IRadarrService radarr,
        ISonarrService sonarr,
        IMediaService media,
        IMediaSubtitleProcessor processor,
        ILogger<LibraryLightDiscovery> logger)
    {
        _db = db;
        _settings = settings;
        _plex = plex;
        _radarr = radarr;
        _sonarr = sonarr;
        _media = media;
        _processor = processor;
        _logger = logger;
    }

    public async Task<int?> TryDiscover(int translationLimit, CancellationToken cancellationToken)
    {
        var plex = await TryPlex();
        var radarr = await HasSetting("radarr_url", "radarr_api_key");
        var sonarr = await HasSetting("sonarr_url", "sonarr_api_key");
        if (plex == null && !radarr && !sonarr)
        {
            return null;
        }

        var sources = await LanguageCodes(SettingKeys.Translation.SourceLanguages);
        var targets = await LanguageCodes(SettingKeys.Translation.TargetLanguages);
        if (sources.Count == 0 || targets.Count == 0)
        {
            _logger.LogInformation("Light library check skipped. Source or target languages are not set.");
            return 0;
        }

        var completed = await LoadCompletedLanguages(cancellationToken);
        var seenAt = await ReadSeenAt();
        var started = 0;
        var truncated = false;
        if (radarr)
        {
            var movies = await NewMovies(seenAt, sources, targets, completed, translationLimit, cancellationToken);
            started += movies.Started;
            truncated |= movies.Truncated;
        }

        if (sonarr && started < translationLimit)
        {
            var episodes = await NewEpisodes(
                seenAt,
                sources,
                targets,
                completed,
                translationLimit - started,
                cancellationToken);
            started += episodes.Started;
            truncated |= episodes.Truncated;
        }

        if (plex != null)
        {
            started += await ClassifyPlexPage(
                plex,
                sources,
                targets,
                completed,
                translationLimit,
                started,
                cancellationToken);
        }

        if (!truncated)
        {
            await _settings.UpsertSetting(SettingKeys.Automation.LibraryLightSeenAt, DateTime.UtcNow.ToString("O"));
        }
        _logger.LogInformation(
            "Light library check used {Helpers}. Started {Count} translations without walking the drive.",
            Helpers(plex != null, radarr, sonarr),
            started);
        return started;
    }

    private readonly record struct DiscoveryBatch(int Started, bool Truncated);

    private async Task<DiscoveryBatch> NewMovies(
        DateTime seenAt,
        HashSet<string> sources,
        HashSet<string> targets,
        Dictionary<(MediaType Type, int Id), HashSet<string>> completed,
        int limit,
        CancellationToken cancellationToken)
    {
        List<int> ids;
        try
        {
            ids = await _radarr.GetImportedMovieIdsSince(seenAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lingarr could not read new movies from Radarr.");
            return new DiscoveryBatch(0, false);
        }

        var started = 0;
        var truncated = false;
        foreach (var radarrId in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (started >= limit)
            {
                truncated = true;
                break;
            }

            var movieId = await _media.GetMovieIdOrSyncFromRadarrMovieId(radarrId);
            if (movieId <= 0)
            {
                continue;
            }

            var movie = await _db.Movies.FirstOrDefaultAsync(item => item.Id == movieId, cancellationToken);
            if (movie == null || !movie.IncludeInTranslation || Skip(movie, movie.LanguageCoverage, completed, MediaType.Movie, movie.Id, sources, targets))
            {
                continue;
            }

            if (await _processor.ProcessMedia(movie, MediaType.Movie))
            {
                started++;
            }
        }

        return new DiscoveryBatch(started, truncated);
    }

    private async Task<DiscoveryBatch> NewEpisodes(
        DateTime seenAt,
        HashSet<string> sources,
        HashSet<string> targets,
        Dictionary<(MediaType Type, int Id), HashSet<string>> completed,
        int limit,
        CancellationToken cancellationToken)
    {
        List<int> ids;
        try
        {
            ids = await _sonarr.GetImportedEpisodeIdsSince(seenAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lingarr could not read new episodes from Sonarr.");
            return new DiscoveryBatch(0, false);
        }

        var started = 0;
        var truncated = false;
        foreach (var sonarrId in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (started >= limit)
            {
                truncated = true;
                break;
            }

            var episodeId = await _media.GetEpisodeIdOrSyncFromSonarrEpisodeId(sonarrId);
            if (episodeId <= 0)
            {
                continue;
            }

            var episode = await _db.Episodes.FirstOrDefaultAsync(item => item.Id == episodeId, cancellationToken);
            if (episode == null || !episode.IncludeInTranslation || Skip(episode, episode.LanguageCoverage, completed, MediaType.Episode, episode.Id, sources, targets))
            {
                continue;
            }

            if (await _processor.ProcessMedia(episode, MediaType.Episode))
            {
                started++;
            }
        }

        return new DiscoveryBatch(started, truncated);
    }

    private async Task<int> ClassifyPlexPage(
        ResolvedPlexCredentials plex,
        HashSet<string> sources,
        HashSet<string> targets,
        Dictionary<(MediaType Type, int Id), HashSet<string>> completed,
        int limit,
        int alreadyStarted,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PlexLibrarySection> sections;
        try
        {
            sections = await _plex.GetLibrariesAsync(plex.Url!, plex.Token!, plex.ClientId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lingarr could not read Plex libraries.");
            return 0;
        }

        var usable = sections.Where(section => section.Type is "movie" or "show").ToList();
        if (usable.Count == 0)
        {
            return 0;
        }

        var sectionIndex = await ReadInt(SettingKeys.Automation.LibraryLightPlexSection);
        var offset = await ReadInt(SettingKeys.Automation.LibraryLightPlexOffset);
        if (sectionIndex >= usable.Count)
        {
            sectionIndex = 0;
            offset = 0;
        }

        var section = usable[sectionIndex];
        var metadataType = section.Type == "movie" ? 1 : 4;
        string page;
        try
        {
            page = await _plex.GetLibraryPageAsync(
                plex.Url!,
                plex.Token!,
                plex.ClientId,
                section.Key,
                metadataType,
                offset,
                PlexPageSize,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lingarr could not read a Plex library page.");
            return 0;
        }

        var started = alreadyStarted;
        var count = 0;
        foreach (var item in ReadPlexItems(page))
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            var media = item.Kind == "episode"
                ? await FindEpisode(item, cancellationToken)
                : await FindMovie(item.Guids, cancellationToken);
            if (media == null || string.IsNullOrWhiteSpace(item.RatingKey))
            {
                continue;
            }

            var type = item.Kind == "episode" ? MediaType.Episode : MediaType.Movie;
            if (Skip(media, ReadCoverage(media), completed, type, media.Id, sources, targets))
            {
                continue;
            }

            string metadata;
            try
            {
                metadata = await _plex.GetMetadataAsync(
                    plex.Url!,
                    plex.Token!,
                    plex.ClientId,
                    item.RatingKey,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lingarr could not read Plex item {RatingKey}.", item.RatingKey);
                continue;
            }

            var languages = ReadSubtitleLanguages(FirstMetadata(metadata));
            var coverage = LibraryCoverage.Format(languages);
            if (!string.IsNullOrEmpty(coverage))
            {
                SetCoverage(media, coverage);
            }

            if (Skip(media, ReadCoverage(media), completed, type, media.Id, sources, targets)
                || started >= limit
                || !HasSource(languages, sources)
                || targets.All(target => HasLanguage(languages, target)))
            {
                continue;
            }

            if (await _processor.ProcessMedia(media, type))
            {
                started++;
            }
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (count < PlexPageSize)
        {
            sectionIndex = (sectionIndex + 1) % usable.Count;
            offset = 0;
        }
        else
        {
            offset += PlexPageSize;
        }

        await _settings.UpsertSetting(SettingKeys.Automation.LibraryLightPlexSection, sectionIndex.ToString());
        await _settings.UpsertSetting(SettingKeys.Automation.LibraryLightPlexOffset, offset.ToString());
        return started - alreadyStarted;
    }

    private async Task<IMedia?> FindMovie(IReadOnlyList<string> guids, CancellationToken cancellationToken)
    {
        foreach (var tag in PlexMovieTags.FromGuids(guids))
        {
            var movie = await _db.Movies.FirstOrDefaultAsync(
                item => (item.Path != null && item.Path.Contains(tag))
                    || (item.FileName != null && item.FileName.Contains(tag)),
                cancellationToken);
            if (movie != null)
            {
                return movie;
            }
        }

        return null;
    }

    private async Task<IMedia?> FindEpisode(PlexLightItem item, CancellationToken cancellationToken)
    {
        if (item.SeasonNumber == null || item.EpisodeNumber == null || string.IsNullOrWhiteSpace(item.ShowTitle))
        {
            return null;
        }

        var rows = await (
            from episode in _db.Episodes
            join season in _db.Seasons on episode.SeasonId equals season.Id
            join show in _db.Shows on season.ShowId equals show.Id
            where episode.EpisodeNumber == item.EpisodeNumber && season.SeasonNumber == item.SeasonNumber
            select new { episode, show.Title, show.Path }).ToListAsync(cancellationToken);
        return rows.FirstOrDefault(row =>
            SameTitle(row.Title, item.ShowTitle) || SameTitle(LastSegment(row.Path), item.ShowTitle))?.episode;
    }

    private static bool Skip(
        IMedia media,
        string? coverage,
        Dictionary<(MediaType Type, int Id), HashSet<string>> completed,
        MediaType type,
        int id,
        HashSet<string> sources,
        HashSet<string> targets)
    {
        completed.TryGetValue((type, id), out var extra);
        if (LibraryCoverage.HasFullPair(coverage, extra, sources, targets))
        {
            if (LibraryCoverage.HasFullPair(coverage, null, sources, targets)
                && !LibraryCoverage.HasUsablePair(media.Path, media.FileName, sources, targets))
            {
                return false;
            }

            return true;
        }

        return LibraryCoverage.CheckedWithoutSource(coverage, sources);
    }

    private async Task<Dictionary<(MediaType Type, int Id), HashSet<string>>> LoadCompletedLanguages(
        CancellationToken cancellationToken)
    {
        var rows = await _db.TranslationRequests
            .AsNoTracking()
            .Where(request => request.Status == TranslationStatus.Completed && request.MediaId != null)
            .Select(request => new
            {
                request.MediaId,
                request.MediaType,
                request.SourceLanguage,
                request.TargetLanguage,
                request.SubtitleToTranslate,
                request.TranslatedSubtitle
            })
            .ToListAsync(cancellationToken);
        var movieIds = rows
            .Where(row => row.MediaType == MediaType.Movie && row.MediaId != null)
            .Select(row => row.MediaId!.Value)
            .Distinct()
            .ToList();
        var episodeIds = rows
            .Where(row => row.MediaType == MediaType.Episode && row.MediaId != null)
            .Select(row => row.MediaId!.Value)
            .Distinct()
            .ToList();
        var movieNames = await _db.Movies.AsNoTracking()
            .Where(movie => movieIds.Contains(movie.Id))
            .ToDictionaryAsync(movie => movie.Id, movie => movie.FileName, cancellationToken);
        var episodeNames = await _db.Episodes.AsNoTracking()
            .Where(episode => episodeIds.Contains(episode.Id))
            .ToDictionaryAsync(episode => episode.Id, episode => episode.FileName, cancellationToken);
        var map = new Dictionary<(MediaType Type, int Id), HashSet<string>>();
        foreach (var row in rows)
        {
            if (row.MediaId == null)
            {
                continue;
            }

            var fileName = row.MediaType == MediaType.Movie
                ? movieNames.GetValueOrDefault(row.MediaId.Value)
                : row.MediaType == MediaType.Episode
                    ? episodeNames.GetValueOrDefault(row.MediaId.Value)
                    : null;
            if (!LibraryCoverage.MatchesCurrentFile(row.SubtitleToTranslate, fileName))
            {
                continue;
            }

            var key = (row.MediaType, row.MediaId.Value);
            if (!map.TryGetValue(key, out var languages))
            {
                languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[key] = languages;
            }

            var source = LibraryCoverage.Format([row.SourceLanguage]);
            var target = LibraryCoverage.Format([row.TargetLanguage]);
            if (!string.IsNullOrEmpty(source))
            {
                languages.Add(source);
            }

            if (!string.IsNullOrEmpty(target)
                && SubtitleBytes.IsUsableSidecar(row.TranslatedSubtitle, target))
            {
                languages.Add(target);
            }
        }

        return map;
    }

    private async Task<HashSet<string>> LanguageCodes(string key)
    {
        try
        {
            var values = await _settings.GetSettingAsJson<Contracts.Models.SourceLanguage>(key);
            return values
                .Select(language => LibraryCoverage.Format([language.Code]))
                .Where(code => !string.IsNullOrEmpty(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<DateTime> ReadSeenAt()
    {
        var raw = await _settings.GetSetting(SettingKeys.Automation.LibraryLightSeenAt);
        if (DateTime.TryParse(raw, out var seen))
        {
            return seen.ToUniversalTime();
        }

        return DateTime.UtcNow.AddDays(-1);
    }

    private async Task<int> ReadInt(string key)
    {
        var raw = await _settings.GetSetting(key);
        return int.TryParse(raw, out var value) && value >= 0 ? value : 0;
    }

    private async Task<bool> HasSetting(string urlKey, string apiKey)
    {
        var url = await _settings.GetSetting(urlKey);
        var key = await _settings.GetEncryptedSetting(apiKey);
        return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key);
    }

    private async Task<ResolvedPlexCredentials?> TryPlex()
    {
        var credentials = await PlexCredentials.ResolveAsync(_settings);
        return string.IsNullOrWhiteSpace(credentials.Url) || string.IsNullOrWhiteSpace(credentials.Token)
            ? null
            : credentials;
    }

    private static void SetCoverage(IMedia media, string coverage)
    {
        switch (media)
        {
            case Movie movie:
                movie.LanguageCoverage = Merge(movie.LanguageCoverage, coverage);
                break;
            case Episode episode:
                episode.LanguageCoverage = Merge(episode.LanguageCoverage, coverage);
                break;
        }
    }

    private static string? ReadCoverage(IMedia media) => media switch
    {
        Movie movie => movie.LanguageCoverage,
        Episode episode => episode.LanguageCoverage,
        _ => null
    };

    private static string Merge(string? current, string added)
    {
        var codes = new List<string>();
        if (!string.IsNullOrWhiteSpace(current))
        {
            codes.AddRange(current.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (!string.IsNullOrWhiteSpace(added))
        {
            codes.AddRange(added.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return LibraryCoverage.Format(codes);
    }

    private static IEnumerable<PlexLightItem> ReadPlexItems(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("MediaContainer", out var container)
            || !container.TryGetProperty("Metadata", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in items.EnumerateArray())
        {
            var type = item.TryGetProperty("type", out var typeProperty) ? typeProperty.GetString() : null;
            if (type is not ("movie" or "episode"))
            {
                continue;
            }

            var ratingKey = item.TryGetProperty("ratingKey", out var key)
                ? key.ValueKind == JsonValueKind.Number ? key.GetRawText() : key.GetString()
                : null;
            yield return new PlexLightItem(
                type,
                ratingKey,
                item.TryGetProperty("grandparentTitle", out var show) ? show.GetString() : null,
                ReadInt(item, "parentIndex"),
                ReadInt(item, "index"),
                ReadGuids(item));
        }
    }

    private static List<string> ReadGuids(JsonElement item)
    {
        var guids = new List<string>();
        if (!item.TryGetProperty("Guid", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return guids;
        }

        foreach (var guid in list.EnumerateArray())
        {
            if (guid.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                var value = id.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    guids.Add(value);
                }
            }
        }

        return guids;
    }

    private static List<string> ReadSubtitleLanguages(JsonElement item)
    {
        var languages = new List<string>();
        if (!item.TryGetProperty("Media", out var media) || media.ValueKind != JsonValueKind.Array)
        {
            return languages;
        }

        foreach (var entry in media.EnumerateArray())
        {
            if (!entry.TryGetProperty("Part", out var parts) || parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (!part.TryGetProperty("Stream", out var streams) || streams.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var stream in streams.EnumerateArray())
                {
                    if (!stream.TryGetProperty("streamType", out var streamType) || streamType.GetInt32() != 3)
                    {
                        continue;
                    }

                    var code = stream.TryGetProperty("languageTag", out var tag) ? tag.GetString() : null;
                    code ??= stream.TryGetProperty("languageCode", out var languageCode) ? languageCode.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        languages.Add(code);
                    }
                }
            }
        }

        return languages;
    }

    private static int? ReadInt(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number) ? number : null;
    }

    private static bool HasSource(IReadOnlyList<string> languages, IReadOnlySet<string> sources) =>
        languages.Any(language => sources.Contains(LibraryCoverage.Format([language])));

    private static bool HasLanguage(IReadOnlyList<string> languages, string target) =>
        languages.Any(language => string.Equals(LibraryCoverage.Format([language]), target, StringComparison.OrdinalIgnoreCase));

    private static JsonElement FirstMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("MediaContainer", out var container)
            && container.TryGetProperty("Metadata", out var items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                return item.Clone();
            }
        }

        return document.RootElement.Clone();
    }

    private static bool SameTitle(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var a = PlexLookup.ToSlug(left);
        var b = PlexLookup.ToSlug(right);
        return a.Length >= 3 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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

    private static string Helpers(bool plex, bool radarr, bool sonarr)
    {
        var names = new List<string>();
        if (plex)
        {
            names.Add("Plex");
        }

        if (radarr)
        {
            names.Add("Radarr");
        }

        if (sonarr)
        {
            names.Add("Sonarr");
        }

        return string.Join(", ", names);
    }

    private sealed record PlexLightItem(
        string Kind,
        string? RatingKey,
        string? ShowTitle,
        int? SeasonNumber,
        int? EpisodeNumber,
        IReadOnlyList<string> Guids);
}
