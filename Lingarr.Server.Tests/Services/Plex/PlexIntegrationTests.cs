using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Services.Integration.Plex;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexIntegrationTests
{
    private const string SampleToken = "test-token";

    [Fact]
    public void BuildAuthUrl_IncludesThePersistentClientIdAndPinCode()
    {
        var url = PlexMatcher.BuildAuthUrl("client-1", "pin-code");

        Assert.StartsWith("https://app.plex.tv/auth#?", url);
        Assert.Contains("clientID=client-1", url);
        Assert.Contains("code=pin-code", url);
        Assert.Contains("context[device][product]=Lingarr", url);
    }

    [Fact]
    public void Rank_PrefersALocalServerOverAFasterRelay()
    {
        var ranked = PlexMatcher.Rank(
        [
            new PlexReachableConnection("https://relay.plex.direct", false, true, 5),
            new PlexReachableConnection("http://192.168.1.20:32400", true, false, 50)
        ]);

        Assert.Equal("http://192.168.1.20:32400", ranked[0].Uri);
    }

    [Fact]
    public void MatchMetadata_MatchesTheWindowsPathForTheSameMovieStem()
    {
        var match = PlexMatcher.MatchMetadata(
            Metadata("E:\\media\\movies\\Example\\Example.mkv", EnglishStream),
            PlexMatcher.DirectoryCandidates("/media/media/movies/Example"),
            "Example");

        Assert.NotNull(match);
        Assert.Equal("42", match.RatingKey);
        Assert.Equal(7, match.PartId);
    }

    [Fact]
    public void MatchMetadata_DoesNotMatchADifferentFolderWithTheSameTitle()
    {
        var match = PlexMatcher.MatchMetadata(
            Metadata("E:\\media\\movies\\Other\\Example.mkv", EnglishStream),
            PlexMatcher.DirectoryCandidates("/media/media/movies/Example"),
            "Example");

        Assert.Null(match);
    }

    [Fact]
    public void MatchPage_FindsTheElixirByTmdbIdAndEnglishSlug()
    {
        var lookup = PlexLookup.Create(
            "The Elixir",
            "/media/media/movies/The Elixir (2025) {tmdb-1306525}",
            "The Elixir (2025) {tmdb-1306525} [WEBDL-1080p][EAC3 5.1][x264]-FHC");
        var page = """
            {"MediaContainer":{"totalSize":1,"Metadata":[{
              "ratingKey":"49051","librarySectionID":1,"type":"movie",
              "title":"Abadi Nan Jaya","originalTitle":"Abadi Nan Jaya","slug":"the-elixir-2025",
              "Guid":[{"id":"imdb://tt32643830"},{"id":"tmdb://1306525"}],
              "Media":[{"Part":[{"id":81451,"file":"E:\\media\\movies\\The Elixir (2025) {tmdb-1306525}\\The Elixir (2025) {tmdb-1306525} [WEBDL-1080p][EAC3 5.1][x264]-FHC.mkv"}]}]
            }]}}
            """;

        var scan = PlexMatcher.MatchPage(
            page,
            lookup,
            PlexMatcher.DirectoryCandidates("/media/media/movies/The Elixir (2025) {tmdb-1306525}"));

        Assert.Contains("tmdb://1306525", lookup.ExternalIds);
        Assert.Contains("the-elixir-2025", lookup.Slugs);
        Assert.NotNull(scan.PathMatch);
        Assert.Equal("49051", scan.PathMatch.RatingKey);
        Assert.Equal(81451, scan.PathMatch.PartId);
    }

    [Fact]
    public void PickNewSubtitle_SelectsTheNewBulgarianStreamAndSkipsForcedWhenAnotherExists()
    {
        var before = new List<PlexSubtitleStream>
        {
            Stream(1, "en", "eng", forced: false)
        };
        var after = new List<PlexSubtitleStream>
        {
            Stream(1, "en", "eng", forced: false),
            Stream(8, "bg", "bul", forced: true),
            Stream(9, "bg", "bul", forced: false, extended: "Bulgarian (SRT External)")
        };

        var picked = PlexMatcher.PickNewSubtitle(before, after, "bg");

        Assert.NotNull(picked);
        Assert.Equal(9, picked.Id);
        Assert.Null(PlexMatcher.PickNewSubtitle(before, after, "fr"));
    }

    [Fact]
    public void SameLanguage_TreatsPlexBibliographicCodesAsTheTranslationLanguage()
    {
        Assert.True(PlexLanguage.Same("bg", "bul"));
        Assert.True(PlexLanguage.Same("fr", "fre"));
        Assert.False(PlexLanguage.Same("bg", "en"));
    }

    [Fact]
    public async Task CreatePin_ParsesThePinAndSendsTheClientIdentifier()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            """{"id":564964751,"code":"pin-code","authToken":null}"""));
        var client = CreateClient(handler);

        var pin = await client.CreatePinAsync("client-1", CancellationToken.None);

        Assert.Equal(564964751, pin.Id);
        Assert.Equal("pin-code", pin.Code);
        Assert.Equal("client-1", handler.Requests[0].Request.Headers.GetValues("X-Plex-Client-Identifier").Single());
        Assert.DoesNotContain("X-Plex-Token", handler.Requests[0].Request.RequestUri!.Query);
    }

    [Fact]
    public async Task RefreshSection_GetsTheLibraryRefreshUrl()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        await client.RefreshSectionAsync("http://plex:32400", "token", "client-1", "2", CancellationToken.None);

        Assert.Equal(
            "http://plex:32400/library/sections/2/refresh",
            handler.Requests[0].Request.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Request.Method);
        Assert.DoesNotContain("X-Plex-Token=token", handler.Requests[0].Request.RequestUri!.Query);
    }

    [Fact]
    public async Task GetLibraries_ReadsRefreshing()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            """{"MediaContainer":{"Directory":[{"key":"1","type":"movie","title":"Movies","refreshing":true}]}}"""));
        var client = CreateClient(handler);

        var sections = await client.GetLibrariesAsync(
            "http://plex:32400",
            "token",
            "client-1",
            CancellationToken.None);

        Assert.Equal("1", sections[0].Key);
        Assert.True(sections[0].Refreshing);
    }

    [Fact]
    public async Task UploadSubtitle_PostsTheFileWithAnExplicitLanguageAndNoTokenInTheUrl()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);
        var bytes = Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        await client.UploadSubtitleAsync(
            "http://plex:32400",
            SampleToken,
            "client-1",
            "42",
            "Example.bg.srt",
            "srt",
            "bg",
            bytes,
            CancellationToken.None);

        var sent = handler.Requests[0];
        var uri = sent.Request.RequestUri!.ToString();
        Assert.Equal(HttpMethod.Post, sent.Request.Method);
        Assert.Contains("/library/metadata/42/subtitles", uri);
        Assert.Contains("language=bg", uri);
        Assert.Contains("title=Example.bg.srt", uri);
        Assert.Contains("format=srt", uri);
        Assert.DoesNotContain(SampleToken, uri);
        Assert.Equal(SampleToken, sent.Request.Headers.GetValues("X-Plex-Token").Single());
        Assert.Equal(bytes, sent.Body);
    }

    [Fact]
    public async Task PollPin_DoesNotSaveATokenPlexRejects()
    {
        var values = new Dictionary<string, string>
        {
            [SettingKeys.MediaServers.PlexClientIdentifier] = "client-1"
        };
        var settings = Settings(values);
        var plex = new FakePlex
        {
            PinToken = SampleToken,
            RejectUser = true
        };
        var logs = new ListLogger<PlexAuthService>();
        var auth = new PlexAuthService(settings.Object, plex, new MemoryCache(new MemoryCacheOptions()), logs);
        var started = await auth.StartPinAsync();

        var poll = await auth.PollPinAsync(started.PinId);

        Assert.Equal("invalid", poll.Status);
        Assert.False(values.ContainsKey(SettingKeys.MediaServers.PlexToken));
        Assert.DoesNotContain(logs.Messages, message => message.Contains(SampleToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PollPin_SavesTheAccountTokenWhenPlexAcceptsIt()
    {
        var values = new Dictionary<string, string>
        {
            [SettingKeys.MediaServers.PlexClientIdentifier] = "client-1"
        };
        var settings = Settings(values);
        var plex = new FakePlex { PinToken = SampleToken, Username = "ada" };
        var auth = new PlexAuthService(
            settings.Object,
            plex,
            new MemoryCache(new MemoryCacheOptions()),
            new ListLogger<PlexAuthService>());
        var started = await auth.StartPinAsync();

        var poll = await auth.PollPinAsync(started.PinId);

        Assert.Equal("connected", poll.Status);
        Assert.Equal(SampleToken, values[SettingKeys.MediaServers.PlexToken]);
        Assert.Equal("oauth", values[SettingKeys.MediaServers.PlexAuthMethod]);
        Assert.Equal("ada", values[SettingKeys.MediaServers.PlexUsername]);
    }

    [Fact]
    public async Task Logout_ClearsTheTokenAndKeepsTheClientIdentifier()
    {
        var values = new Dictionary<string, string>
        {
            [SettingKeys.MediaServers.PlexClientIdentifier] = "client-1",
            [SettingKeys.MediaServers.PlexToken] = SampleToken,
            [SettingKeys.MediaServers.PlexUsername] = "ada"
        };
        var auth = new PlexAuthService(
            Settings(values).Object,
            new FakePlex(),
            new MemoryCache(new MemoryCacheOptions()),
            new ListLogger<PlexAuthService>());

        await auth.LogoutAsync();

        Assert.Equal("", values[SettingKeys.MediaServers.PlexToken]);
        Assert.Equal("client-1", values[SettingKeys.MediaServers.PlexClientIdentifier]);
        Assert.Equal("true", values[SettingKeys.MediaServers.PlexIgnoreEnvironment]);
    }

    [Fact]
    public void Choose_UsesACardSignInInsteadOfTheEnvironmentToken()
    {
        var chosen = PlexCredentials.Choose(
            "http://host.docker.internal:32400",
            "env-token",
            "http://192.168.1.217:32400",
            "saved-token",
            "token",
            ignoreEnvironment: false,
            "client-1");

        Assert.Equal("http://192.168.1.217:32400", chosen.Url);
        Assert.Equal("saved-token", chosen.Token);
        Assert.Equal("setting", chosen.Source);
    }

    [Fact]
    public void Choose_SignOutIgnoresTheEnvironmentToken()
    {
        var chosen = PlexCredentials.Choose(
            "http://host.docker.internal:32400",
            "env-token",
            "",
            "",
            "",
            ignoreEnvironment: true,
            "client-1");

        Assert.True(string.IsNullOrWhiteSpace(chosen.Token));
        Assert.True(string.IsNullOrWhiteSpace(chosen.Url));
    }

    [Fact]
    public async Task Apply_UploadsTheSubtitleWhenRefreshDoesNotListIt()
    {
        var subtitlePath = Path.Combine(Path.GetTempPath(), $"lingarr-plex-{Guid.NewGuid():N}.bg.srt");
        await File.WriteAllTextAsync(subtitlePath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        try
        {
            var plex = new FakePlex
            {
                OldMetadata = Metadata("E:\\media\\movies\\Example\\Example.mkv", EnglishStream),
                NewMetadata = Metadata(
                    "E:\\media\\movies\\Example\\Example.mkv",
                    EnglishStream + "," + BulgarianStream)
            };
            var logs = new ListLogger<PlexSubtitleSelector>();
            var selector = Selector(plex, logs, subtitlePath);

            await selector.ApplyTranslatedSubtitleAsync(Request(subtitlePath, "bg"));

            Assert.Equal(1, plex.Uploads);
            Assert.Equal("bg", plex.UploadedLanguage);
            Assert.Equal(Path.GetFileName(subtitlePath), plex.UploadedFileName);
            Assert.Equal(9, plex.SelectedStream);
            Assert.DoesNotContain(logs.Messages, message => message.Contains(SampleToken, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(subtitlePath);
        }
    }

    [Fact]
    public async Task Apply_DoesNotUploadWhenTheRefreshAlreadyListsTheSubtitle()
    {
        var subtitlePath = Path.Combine(Path.GetTempPath(), $"lingarr-plex-{Guid.NewGuid():N}.bg.srt");
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        try
        {
            var found = Metadata(
                "E:\\media\\movies\\Example\\Example.mkv",
                EnglishStream + "," + BulgarianStream);
            var plex = new FakePlex
            {
                OldMetadata = Metadata("E:\\media\\movies\\Example\\Example.mkv", EnglishStream),
                NewMetadata = found,
                ReturnNewOnCall = 2
            };
            var selector = Selector(plex, new ListLogger<PlexSubtitleSelector>(), subtitlePath);

            await selector.ApplyTranslatedSubtitleAsync(Request(subtitlePath, "bg"));

            Assert.Equal(0, plex.Uploads);
            Assert.Equal(9, plex.SelectedStream);
        }
        finally
        {
            File.Delete(subtitlePath);
        }
    }

    [Fact]
    public async Task Apply_SwallowsPlexFailures()
    {
        var plex = new FakePlex { ThrowOnSearch = true };
        var logs = new ListLogger<PlexSubtitleSelector>();
        var selector = Selector(plex, logs, Path.Combine(Path.GetTempPath(), "missing.bg.srt"));

        await selector.ApplyTranslatedSubtitleAsync(Request(Path.Combine(Path.GetTempPath(), "missing.bg.srt"), "bg"));

        Assert.Equal(0, plex.Uploads);
        Assert.Null(plex.SelectedStream);
        Assert.DoesNotContain(logs.Messages, message => message.Contains(SampleToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetSetting_RejectsSelectedSubtitleWithoutALanguage()
    {
        var settings = new Mock<ISettingService>();
        settings.Setup(item => item.GetSetting(SettingKeys.MediaServers.PlexDefaultSubtitleLanguage))
            .ReturnsAsync("");
        var controller = new SettingController(settings.Object);

        var result = await controller.SetSetting(new Setting
        {
            Key = SettingKeys.MediaServers.PlexSetSelectedSubtitle,
            Value = "true"
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        settings.Verify(item => item.SetSetting(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private static PlexSubtitleSelector Selector(FakePlex plex, ListLogger<PlexSubtitleSelector> logs, string subtitlePath)
    {
        var values = new Dictionary<string, string>
        {
            [SettingKeys.MediaServers.PlexSetSelectedSubtitle] = "true",
            [SettingKeys.MediaServers.PlexDefaultSubtitleLanguage] = "bg",
            [SettingKeys.MediaServers.PlexUrl] = "http://plex:32400",
            [SettingKeys.MediaServers.PlexToken] = SampleToken,
            [SettingKeys.MediaServers.PlexClientIdentifier] = "client-1"
        };
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new LingarrDbContext(options);
        db.Movies.Add(new Movie
        {
            RadarrId = 1,
            Title = "Example",
            FileName = "Example",
            Path = "/media/media/movies/Example",
            DateAdded = DateTime.UtcNow
        });
        db.SaveChanges();
        return new PlexSubtitleSelector(
            Settings(values).Object,
            plex,
            db,
            new PlexPollOptions { Interval = TimeSpan.Zero, RefreshAttempts = 1, UploadAttempts = 1 },
            logs);
    }

    private static TranslationRequest Request(string subtitlePath, string target) => new()
    {
        Id = 5,
        Title = "Example",
        SourceLanguage = "en",
        TargetLanguage = target,
        MediaType = MediaType.Movie,
        Status = TranslationStatus.Completed,
        MediaId = 1,
        TranslatedSubtitle = subtitlePath
    };

    private static Mock<ISettingService> Settings(Dictionary<string, string> values)
    {
        var settings = new Mock<ISettingService>();
        settings.Setup(item => item.GetSetting(It.IsAny<string>()))
            .Returns((string key) => Task.FromResult<string?>(values.GetValueOrDefault(key)));
        settings.Setup(item => item.GetEncryptedSetting(It.IsAny<string>()))
            .Returns((string key) => Task.FromResult<string?>(values.GetValueOrDefault(key)));
        settings.Setup(item => item.SetSetting(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, value) => values[key] = value)
            .ReturnsAsync(true);
        settings.Setup(item => item.SetEncryptedSetting(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, value) => values[key] = value)
            .ReturnsAsync(true);
        settings.Setup(item => item.UpsertSetting(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, value) => values[key] = value)
            .Returns(Task.CompletedTask);
        return settings;
    }

    private static PlexClient CreateClient(RecordingHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return new PlexClient(factory.Object);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) => new(statusCode)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private const string EnglishStream =
        """{"id":1,"streamType":3,"languageCode":"eng","languageTag":"en","selected":1,"forced":0}""";

    private const string BulgarianStream =
        """{"id":9,"streamType":3,"languageCode":"bul","languageTag":"bg","selected":0,"forced":0,"title":"Example.bg.srt","extendedDisplayTitle":"Bulgarian (SRT External)"}""";

    private static string Metadata(string file, string streams)
    {
        var streamJson = "[" + streams + "]";
        var payload = new Dictionary<string, object>
        {
            ["MediaContainer"] = new Dictionary<string, object>
            {
                ["librarySectionID"] = 2,
                ["Metadata"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["ratingKey"] = "42",
                        ["librarySectionID"] = 2,
                        ["Media"] = new object[]
                        {
                            new Dictionary<string, object>
                            {
                                ["Part"] = new object[]
                                {
                                    new Dictionary<string, object>
                                    {
                                        ["id"] = 7,
                                        ["file"] = file,
                                        ["Stream"] = JsonSerializer.Deserialize<JsonElement>(streamJson)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static PlexSubtitleStream Stream(
        long id,
        string tag,
        string code,
        bool forced,
        string? extended = null) => new()
    {
        Id = id,
        LanguageTag = tag,
        LanguageCode = code,
        Forced = forced,
        ExtendedDisplayTitle = extended
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[]? body = null;
            if (request.Content != null)
            {
                body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            }

            Requests.Add(new SentRequest(request, body));
            return responseFactory(request);
        }
    }

    private sealed record SentRequest(HttpRequestMessage Request, byte[]? Body);

    private sealed class FakePlex : IPlexClient
    {
        public string? PinToken { get; init; }
        public bool RejectUser { get; init; }
        public string? Username { get; init; }
        public string OldMetadata { get; init; } = "{}";
        public string NewMetadata { get; init; } = "{}";
        public int ReturnNewOnCall { get; init; } = 3;
        public bool ThrowOnSearch { get; init; }
        public int MetadataCalls { get; private set; }
        public int Uploads { get; private set; }
        public string? UploadedLanguage { get; private set; }
        public string? UploadedFileName { get; private set; }
        public long? SelectedStream { get; private set; }

        public Task<PlexPin> CreatePinAsync(string clientId, CancellationToken cancellationToken) =>
            Task.FromResult(new PlexPin(7, "pin-code"));

        public Task<string?> PollPinAsync(long pinId, string clientId, CancellationToken cancellationToken) =>
            Task.FromResult(PinToken);

        public Task<PlexAccount?> GetUserAsync(string token, string clientId, CancellationToken cancellationToken) =>
            Task.FromResult(RejectUser ? null : new PlexAccount(Username, null));

        public Task<IReadOnlyList<PlexDiscoveredServer>> GetOwnedServersAsync(
            string token,
            string clientId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PlexProbe> ProbeAsync(
            string url,
            string token,
            string clientId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<string>> SearchRatingKeysAsync(
            string baseUrl,
            string token,
            string clientId,
            string query,
            CancellationToken cancellationToken)
        {
            if (ThrowOnSearch)
            {
                throw new HttpRequestException("Plex search failed (503).");
            }

            return Task.FromResult<IReadOnlyList<string>>(["42"]);
        }

        public Task<IReadOnlyList<PlexLibrarySection>> GetLibrariesAsync(
            string baseUrl,
            string token,
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlexLibrarySection>>([]);

        public Task<string> GetLibraryPageAsync(
            string baseUrl,
            string token,
            string clientId,
            string sectionKey,
            int metadataType,
            int start,
            int size,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<string> GetMetadataAsync(
            string baseUrl,
            string token,
            string clientId,
            string ratingKey,
            CancellationToken cancellationToken)
        {
            MetadataCalls++;
            return Task.FromResult(MetadataCalls >= ReturnNewOnCall ? NewMetadata : OldMetadata);
        }

        public Task RefreshMetadataAsync(
            string baseUrl,
            string token,
            string clientId,
            string ratingKey,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshSectionAsync(
            string baseUrl,
            string token,
            string clientId,
            string sectionId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshSectionPathAsync(
            string baseUrl,
            string token,
            string clientId,
            string sectionId,
            string directory,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetSelectedSubtitleAsync(
            string baseUrl,
            string token,
            string clientId,
            long partId,
            long streamId,
            bool allParts,
            CancellationToken cancellationToken)
        {
            SelectedStream = streamId;
            return Task.CompletedTask;
        }

        public Task UploadSubtitleAsync(
            string baseUrl,
            string token,
            string clientId,
            string ratingKey,
            string fileName,
            string format,
            string language,
            byte[] content,
            CancellationToken cancellationToken)
        {
            Uploads++;
            UploadedLanguage = language;
            UploadedFileName = fileName;
            return Task.CompletedTask;
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception != null)
            {
                Messages.Add(exception.Message);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
