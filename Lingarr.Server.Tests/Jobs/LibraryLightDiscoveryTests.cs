using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class LibraryLightDiscoveryTests
{
    [Fact]
    public async Task TryDiscover_TranslatesFakeNewFilesAndSkipsACompletePair()
    {
        var root = Directory.CreateTempSubdirectory("lingarr-fake-added");
        try
        {
            var newMovieDir = Path.Combine(root.FullName, "Fake New Movie (2026) {tmdb-111}");
            var pairedDir = Path.Combine(root.FullName, "Already Paired (2020) {tmdb-555}");
            var episodeDir = Path.Combine(root.FullName, "Fake Show", "Season 1");
            Directory.CreateDirectory(newMovieDir);
            Directory.CreateDirectory(pairedDir);
            Directory.CreateDirectory(episodeDir);
            File.WriteAllText(Path.Combine(newMovieDir, "Fake New Movie.mkv"), "fake-video");
            File.WriteAllText(Path.Combine(pairedDir, "Already Paired.mkv"), "fake-video");
            File.WriteAllText(Path.Combine(pairedDir, "Already Paired.en.srt"), "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
            File.WriteAllText(Path.Combine(pairedDir, "Already Paired.bg.srt"), "1\n00:00:01,000 --> 00:00:02,000\nЗдравей\n");
            File.WriteAllText(Path.Combine(episodeDir, "Fake Show - S01E01.mkv"), "fake-video");

            await using var database = new LingarrDbContext(
                new DbContextOptionsBuilder<LingarrDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options);
            database.Movies.Add(Movie(1, 100, "Fake New Movie", newMovieDir, "Fake New Movie", null));
            database.Movies.Add(Movie(2, 200, "Already Paired", pairedDir, "Already Paired", "bg,en"));
            database.Episodes.Add(Episode(3, 300));
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);

            var opened = new List<string>();
            var processor = new Mock<IMediaSubtitleProcessor>();
            processor
                .Setup(service => service.ProcessMedia(It.IsAny<IMedia>(), It.IsAny<MediaType>()))
                .Callback<IMedia, MediaType>((media, _) => opened.Add(media.Title))
                .ReturnsAsync(true);
            var media = new Mock<IMediaService>();
            media.Setup(service => service.GetMovieIdOrSyncFromRadarrMovieId(100)).ReturnsAsync(1);
            media.Setup(service => service.GetMovieIdOrSyncFromRadarrMovieId(200)).ReturnsAsync(2);
            media.Setup(service => service.GetEpisodeIdOrSyncFromSonarrEpisodeId(300)).ReturnsAsync(3);
            var radarr = new Mock<IRadarrService>();
            radarr.Setup(service => service.GetImportedMovieIdsSince(It.IsAny<DateTime>()))
                .ReturnsAsync([100, 200]);
            var sonarr = new Mock<ISonarrService>();
            sonarr.Setup(service => service.GetImportedEpisodeIdsSince(It.IsAny<DateTime>()))
                .ReturnsAsync([300]);
            var plex = new Mock<IPlexClient>();
            plex.Setup(client => client.GetLibrariesAsync(
                    "http://plex.test:32400",
                    "plex-token",
                    "client-id",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([new PlexLibrarySection("1", "movie", "Movies")]);
            plex.Setup(client => client.GetLibraryPageAsync(
                    "http://plex.test:32400",
                    "plex-token",
                    "client-id",
                    "1",
                    1,
                    0,
                    40,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("""
                    {"MediaContainer":{"Metadata":[
                      {"type":"movie","ratingKey":"555","title":"Already Paired","Guid":[{"id":"tmdb://555"}]},
                      {"type":"movie","ratingKey":"111","title":"Fake New Movie","Guid":[{"id":"tmdb://111"}]}
                    ]}}
                    """);
            plex.Setup(client => client.GetMetadataAsync(
                    "http://plex.test:32400",
                    "plex-token",
                    "client-id",
                    "111",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("""
                    {"MediaContainer":{"Metadata":[{"Media":[{"Part":[{"Stream":[
                      {"streamType":3,"languageTag":"en"}
                    ]}]}]}]}}
                    """);

            var discovery = new LibraryLightDiscovery(
                database,
                Settings(),
                plex.Object,
                radarr.Object,
                sonarr.Object,
                media.Object,
                processor.Object,
                NullLogger<LibraryLightDiscovery>.Instance);

            var started = await discovery.TryDiscover(5, TestContext.Current.CancellationToken);

            Assert.Equal(3, started);
            Assert.Contains("Fake New Movie", opened);
            Assert.Contains("Fake Episode", opened);
            Assert.DoesNotContain("Already Paired", opened);
            plex.Verify(client => client.GetMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "555",
                It.IsAny<CancellationToken>()), Times.Never);
            Assert.True(File.Exists(Path.Combine(pairedDir, "Already Paired.bg.srt")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TryDiscover_ProcessesAReplacedFileEvenWhenAnOldTranslationCompleted()
    {
        await using var database = new LingarrDbContext(
            new DbContextOptionsBuilder<LingarrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        database.Episodes.Add(Episode(3, 300, "Show - S03E01 WEBDL-720p", "en"));
        database.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = 3,
            MediaType = MediaType.Episode,
            Title = "Show - S03E01",
            SourceLanguage = "en",
            TargetLanguage = "bg",
            Status = TranslationStatus.Completed,
            SubtitleToTranslate = "/tv/Show/Show - S03E01 WEBDL-1080p.en.srt"
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        var opened = new List<string>();
        var discovery = Discovery(
            database,
            ArrSettings(),
            processor: Processor(opened),
            radarr: Radarr(),
            sonarr: Sonarr(300),
            media: Media(episodeSonarrId: 300, episodeId: 3));

        var started = await discovery.TryDiscover(5, TestContext.Current.CancellationToken);

        Assert.Equal(1, started);
        Assert.Contains("Fake Episode", opened);
    }

    [Fact]
    public async Task TryDiscover_SkipsWhenACompletedRequestMatchesTheCurrentFile()
    {
        await using var database = new LingarrDbContext(
            new DbContextOptionsBuilder<LingarrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        database.Episodes.Add(Episode(3, 300, "Show - S03E01 WEBDL-720p", "en"));
        database.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = 3,
            MediaType = MediaType.Episode,
            Title = "Show - S03E01",
            SourceLanguage = "en",
            TargetLanguage = "bg",
            Status = TranslationStatus.Completed,
            SubtitleToTranslate = "/tv/Show/Show - S03E01 WEBDL-720p.en.srt"
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        var opened = new List<string>();
        var discovery = Discovery(
            database,
            ArrSettings(),
            processor: Processor(opened),
            radarr: Radarr(),
            sonarr: Sonarr(300),
            media: Media(episodeSonarrId: 300, episodeId: 3));

        var started = await discovery.TryDiscover(5, TestContext.Current.CancellationToken);

        Assert.Equal(0, started);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task TryDiscover_ProcessesMovieWhenCoveredTargetSidecarIsEmpty()
    {
        var root = Directory.CreateTempSubdirectory("lingarr-empty-covered");
        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "Leftover.en.srt"),
                "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
            File.WriteAllBytes(Path.Combine(root.FullName, "Leftover.bg.srt"), []);

            await using var database = new LingarrDbContext(
                new DbContextOptionsBuilder<LingarrDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options);
            database.Movies.Add(Movie(1, 100, "Leftover", root.FullName, "Leftover", "bg,en"));
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);

            var opened = new List<string>();
            var discovery = Discovery(
                database,
                ArrSettings(),
                processor: Processor(opened),
                radarr: Radarr(100),
                sonarr: Sonarr(),
                media: Media(moviePairs: [(100, 1)]));

            var started = await discovery.TryDiscover(5, TestContext.Current.CancellationToken);

            Assert.Equal(1, started);
            Assert.Equal("Leftover", Assert.Single(opened));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TryDiscover_ProcessesWhenCompletedTranslationWroteAnEmptyFile()
    {
        var root = Directory.CreateTempSubdirectory("lingarr-empty-translated");
        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "Show - S03E01 WEBDL-720p.en.srt"),
                "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
            var emptyBg = Path.Combine(root.FullName, "Show - S03E01 WEBDL-720p.bg.srt");
            File.WriteAllBytes(emptyBg, []);

            await using var database = new LingarrDbContext(
                new DbContextOptionsBuilder<LingarrDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options);
            var episode = Episode(3, 300, "Show - S03E01 WEBDL-720p", "en");
            episode.Path = root.FullName;
            database.Episodes.Add(episode);
            database.TranslationRequests.Add(new TranslationRequest
            {
                MediaId = 3,
                MediaType = MediaType.Episode,
                Title = "Show - S03E01",
                SourceLanguage = "en",
                TargetLanguage = "bg",
                Status = TranslationStatus.Completed,
                SubtitleToTranslate = Path.Combine(root.FullName, "Show - S03E01 WEBDL-720p.en.srt"),
                TranslatedSubtitle = emptyBg
            });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);

            var opened = new List<string>();
            var discovery = Discovery(
                database,
                ArrSettings(),
                processor: Processor(opened),
                radarr: Radarr(),
                sonarr: Sonarr(300),
                media: Media(episodeSonarrId: 300, episodeId: 3));

            var started = await discovery.TryDiscover(5, TestContext.Current.CancellationToken);

            Assert.Equal(1, started);
            Assert.Contains("Fake Episode", opened);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TryDiscover_KeepsSeenAtWhenTheTranslationCapLeavesImports()
    {
        await using var database = new LingarrDbContext(
            new DbContextOptionsBuilder<LingarrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        database.Movies.Add(Movie(1, 100, "First", "/movies/First", "First", null));
        database.Movies.Add(Movie(2, 200, "Second", "/movies/Second", "Second", null));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        var settings = ArrSettings();
        settings.Set(SettingKeys.Automation.LibraryLightSeenAt, "2026-10-20T12:00:00.0000000Z");
        var opened = new List<string>();
        var discovery = Discovery(
            database,
            settings,
            processor: Processor(opened),
            radarr: Radarr(100, 200),
            sonarr: Sonarr(),
            media: Media(moviePairs: [(100, 1), (200, 2)]));

        var started = await discovery.TryDiscover(1, TestContext.Current.CancellationToken);

        Assert.Equal(1, started);
        Assert.Equal("First", Assert.Single(opened));
        Assert.Equal(
            "2026-10-20T12:00:00.0000000Z",
            await settings.GetSetting(SettingKeys.Automation.LibraryLightSeenAt));
    }

    [Fact]
    public async Task TryDiscover_AdvancesSeenAtWhenEveryImportFits()
    {
        await using var database = new LingarrDbContext(
            new DbContextOptionsBuilder<LingarrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        database.Movies.Add(Movie(1, 100, "First", "/movies/First", "First", null));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        var settings = ArrSettings();
        settings.Set(SettingKeys.Automation.LibraryLightSeenAt, "2026-10-20T12:00:00.0000000Z");
        var discovery = Discovery(
            database,
            settings,
            processor: Processor([]),
            radarr: Radarr(100),
            sonarr: Sonarr(),
            media: Media(moviePairs: [(100, 1)]));

        var started = await discovery.TryDiscover(5, TestContext.Current.CancellationToken);

        Assert.Equal(1, started);
        var seenAt = await settings.GetSetting(SettingKeys.Automation.LibraryLightSeenAt);
        Assert.NotEqual("2026-10-20T12:00:00.0000000Z", seenAt);
        Assert.False(string.IsNullOrWhiteSpace(seenAt));
    }

    [Fact]
    public async Task TryDiscover_FallsBackWhenNoHelperIsConfigured()
    {
        await using var database = new LingarrDbContext(
            new DbContextOptionsBuilder<LingarrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var discovery = new LibraryLightDiscovery(
            database,
            new JsonSettings(),
            new Mock<IPlexClient>().Object,
            new Mock<IRadarrService>().Object,
            new Mock<ISonarrService>().Object,
            new Mock<IMediaService>().Object,
            new Mock<IMediaSubtitleProcessor>().Object,
            NullLogger<LibraryLightDiscovery>.Instance);

        var started = await discovery.TryDiscover(5, TestContext.Current.CancellationToken);

        Assert.Null(started);
    }

    private static JsonSettings Settings()
    {
        var settings = ArrSettings();
        settings.Set(SettingKeys.MediaServers.PlexAuthMethod, "oauth");
        settings.Set(SettingKeys.MediaServers.PlexUrl, "http://plex.test:32400");
        settings.Set(SettingKeys.MediaServers.PlexToken, "plex-token");
        settings.Set(SettingKeys.MediaServers.PlexClientIdentifier, "client-id");
        settings.Set(SettingKeys.MediaServers.PlexIgnoreEnvironment, "false");
        return settings;
    }

    private static JsonSettings ArrSettings()
    {
        var settings = new JsonSettings();
        settings.Set(SettingKeys.Translation.SourceLanguages, """[{"name":"English","code":"en"}]""");
        settings.Set(SettingKeys.Translation.TargetLanguages, """[{"name":"Bulgarian","code":"bg"}]""");
        settings.Set("radarr_url", "http://radarr.test");
        settings.Set("radarr_api_key", "radarr-key");
        settings.Set("sonarr_url", "http://sonarr.test");
        settings.Set("sonarr_api_key", "sonarr-key");
        return settings;
    }

    private static LibraryLightDiscovery Discovery(
        LingarrDbContext database,
        JsonSettings settings,
        Mock<IMediaSubtitleProcessor> processor,
        Mock<IRadarrService> radarr,
        Mock<ISonarrService> sonarr,
        Mock<IMediaService> media) =>
        new(
            database,
            settings,
            new Mock<IPlexClient>().Object,
            radarr.Object,
            sonarr.Object,
            media.Object,
            processor.Object,
            NullLogger<LibraryLightDiscovery>.Instance);

    private static Mock<IMediaSubtitleProcessor> Processor(List<string> opened)
    {
        var processor = new Mock<IMediaSubtitleProcessor>();
        processor
            .Setup(service => service.ProcessMedia(It.IsAny<IMedia>(), It.IsAny<MediaType>()))
            .Callback<IMedia, MediaType>((media, _) => opened.Add(media.Title))
            .ReturnsAsync(true);
        return processor;
    }

    private static Mock<IRadarrService> Radarr(params int[] ids)
    {
        var radarr = new Mock<IRadarrService>();
        radarr.Setup(service => service.GetImportedMovieIdsSince(It.IsAny<DateTime>()))
            .ReturnsAsync(ids.ToList());
        return radarr;
    }

    private static Mock<ISonarrService> Sonarr(params int[] ids)
    {
        var sonarr = new Mock<ISonarrService>();
        sonarr.Setup(service => service.GetImportedEpisodeIdsSince(It.IsAny<DateTime>()))
            .ReturnsAsync(ids.ToList());
        return sonarr;
    }

    private static Mock<IMediaService> Media(
        int? episodeSonarrId = null,
        int? episodeId = null,
        params (int RadarrId, int MovieId)[] moviePairs)
    {
        var media = new Mock<IMediaService>();
        foreach (var pair in moviePairs)
        {
            media.Setup(service => service.GetMovieIdOrSyncFromRadarrMovieId(pair.RadarrId)).ReturnsAsync(pair.MovieId);
        }

        if (episodeSonarrId is int sonarrId && episodeId is int id)
        {
            media.Setup(service => service.GetEpisodeIdOrSyncFromSonarrEpisodeId(sonarrId)).ReturnsAsync(id);
        }

        return media;
    }

    private static Movie Movie(int id, int radarrId, string title, string path, string fileName, string? coverage) => new()
    {
        Id = id,
        RadarrId = radarrId,
        Title = title,
        Path = path,
        FileName = fileName,
        LanguageCoverage = coverage,
        DateAdded = DateTime.UtcNow,
        IncludeInTranslation = true
    };

    private static Episode Episode(int id, int sonarrId) =>
        Episode(id, sonarrId, "Fake Show - S01E01", null);

    private static Episode Episode(int id, int sonarrId, string fileName, string? coverage)
    {
        var show = new Show
        {
            Id = id,
            SonarrId = id,
            Title = "Fake Show",
            Path = "/unused",
            DateAdded = DateTime.UtcNow
        };
        var season = new Season
        {
            Id = id + 10,
            SeasonNumber = 1,
            ShowId = show.Id,
            Show = show
        };
        return new Episode
        {
            Id = id,
            SonarrId = sonarrId,
            EpisodeNumber = 1,
            Title = "Fake Episode",
            FileName = fileName,
            Path = "/unused/Season 1",
            LanguageCoverage = coverage,
            SeasonId = season.Id,
            Season = season,
            IncludeInTranslation = true
        };
    }

    private sealed class JsonSettings : ISettingService
    {
        private readonly Dictionary<string, string> _values = new();

        public event SettingChangedHandler? SettingChanged
        {
            add { }
            remove { }
        }

        public void Set(string key, string value) => _values[key] = value;

        public Task<string?> GetSetting(string key) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task<Dictionary<string, string>> GetSettings(IEnumerable<string> keys) =>
            Task.FromResult(keys.ToDictionary(key => key, key => _values.TryGetValue(key, out var value) ? value : ""));

        public Task<bool> SetSetting(string key, string value)
        {
            _values[key] = value;
            return Task.FromResult(true);
        }

        public Task UpsertSetting(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<bool> SetSettings(Dictionary<string, string> settings)
        {
            foreach (var pair in settings)
            {
                _values[pair.Key] = pair.Value;
            }

            return Task.FromResult(true);
        }

        public Task<List<T>> GetSettingAsJson<T>(string key) where T : class
        {
            var json = _values[key];
            var list = JsonSerializer.Deserialize<List<T>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return Task.FromResult(list ?? []);
        }

        public Task<bool> SetEncryptedSetting(string key, string value) => SetSetting(key, value);

        public Task<string?> GetEncryptedSetting(string key) => GetSetting(key);

        public Task<Dictionary<string, string>> GetEncryptedSettings(IEnumerable<string> keys) => GetSettings(keys);
    }
}
