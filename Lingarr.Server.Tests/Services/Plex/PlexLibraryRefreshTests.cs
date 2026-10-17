using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Services.Integration.Plex;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexLibraryRefreshTests
{
    public PlexLibraryRefreshTests()
    {
        PlexLibraryRefreshJob.Exit();
    }

    [Fact]
    public void ParseKeys_ReadsJsonArray()
    {
        var keys = PlexLibraryRefreshService.ParseKeys("""["1","2"]""");

        Assert.Contains("1", keys);
        Assert.Contains("2", keys);
        Assert.Empty(PlexLibraryRefreshService.ParseKeys(""));
        Assert.Empty(PlexLibraryRefreshService.ParseKeys("not-json"));
    }

    [Fact]
    public void Order_PutsMoviesBeforeShows()
    {
        var ordered = PlexLibraryRefreshService.Order(
        [
            new PlexLibrarySection("2", "show", "TV Shows"),
            new PlexLibrarySection("9", "artist", "Music"),
            new PlexLibrarySection("1", "movie", "Movies")
        ]);

        Assert.Equal(["1", "2", "9"], ordered.Select(section => section.Key).ToArray());
    }

    [Fact]
    public void PickSubtitle_PrefersExternalNonForcedAndSkipsWhenAlreadySelected()
    {
        var streams = new List<PlexSubtitleStream>
        {
            new() { Id = 1, LanguageTag = "en", LanguageCode = "eng", Selected = true },
            new() { Id = 8, LanguageTag = "bg", LanguageCode = "bul", Forced = true },
            new()
            {
                Id = 9,
                LanguageTag = "bg",
                LanguageCode = "bul",
                Forced = false,
                ExtendedDisplayTitle = "Bulgarian (SRT External)"
            }
        };

        var picked = PlexMatcher.PickSubtitle(streams, "bg");

        Assert.NotNull(picked);
        Assert.Equal(9, picked.Id);
        Assert.True(PlexMatcher.PickSubtitle(
        [
            new PlexSubtitleStream { Id = 9, LanguageTag = "bg", LanguageCode = "bul", Selected = true }
        ], "bg")!.Selected);
        Assert.Null(PlexMatcher.PickSubtitle(streams, "fr"));
    }

    [Fact]
    public void ReadLibraryPage_CollectsRatingKeys()
    {
        var page = PlexMatcher.ReadLibraryPage("""
            {"MediaContainer":{"totalSize":2,"Metadata":[
              {"ratingKey":"42","type":"movie"},
              {"ratingKey":"43","type":"movie"}
            ]}}
            """);

        Assert.Equal(["42", "43"], page.RatingKeys.ToArray());
        Assert.Equal(2, page.ItemCount);
        Assert.Equal(2, page.TotalSize);
    }

    [Fact]
    public void PrimaryPart_ReadsTheFirstVideoPartAndStreams()
    {
        var match = PlexMatcher.PrimaryPart(Metadata(
            "E:\\media\\movies\\Example\\Example.mkv",
            """{"id":9,"streamType":3,"languageCode":"bul","languageTag":"bg","selected":0,"forced":0}"""));

        Assert.NotNull(match);
        Assert.Equal("42", match.RatingKey);
        Assert.Equal(7, match.PartId);
        Assert.Equal(9, match.Subtitles[0].Id);
    }

    [Fact]
    public async Task List_MarksEnabledLibrariesFromSettings()
    {
        var plex = new RecordingPlex
        {
            Sections =
            [
                new PlexLibrarySection("2", "show", "TV Shows"),
                new PlexLibrarySection("1", "movie", "Movies")
            ]
        };
        var service = Service(plex, keys: """["2"]""");

        var listed = await service.ListAsync(CancellationToken.None);

        Assert.Equal(["1", "2"], listed.Libraries.Select(item => item.Key).ToArray());
        Assert.False(listed.Libraries[0].Scheduled);
        Assert.True(listed.Libraries[1].Scheduled);
        Assert.False(listed.Running);
    }

    [Fact]
    public async Task ScheduledRefresh_RunsEnabledLibrariesInOrderWithAGapAndNeverOverlaps()
    {
        var plex = new RecordingPlex
        {
            Sections =
            [
                new PlexLibrarySection("2", "show", "TV Shows"),
                new PlexLibrarySection("1", "movie", "Movies")
            ],
            Pages =
            {
                ["1"] = Page("11"),
                ["2"] = Page("22")
            },
            Metadata =
            {
                ["11"] = Metadata(
                    "E:\\media\\movies\\Example\\Example.mkv",
                    """{"id":9,"streamType":3,"languageCode":"bul","languageTag":"bg","selected":0,"forced":0,"extendedDisplayTitle":"Bulgarian (SRT External)"}"""),
                ["22"] = Metadata(
                    "E:\\media\\tvshows\\Show\\S01E01.mkv",
                    """{"id":4,"streamType":3,"languageCode":"eng","languageTag":"en","selected":1,"forced":0}""")
            }
        };
        var service = Service(
            plex,
            keys: """["1","2"]""",
            options: new PlexLibraryRefreshOptions
            {
                Stagger = TimeSpan.FromMilliseconds(40),
                PollInterval = TimeSpan.Zero,
                MinimumScanWait = TimeSpan.Zero,
                ScanTimeout = TimeSpan.FromSeconds(1),
                PageSize = 50
            });

        var started = DateTime.UtcNow;
        var result = await service.RefreshScheduledAsync(CancellationToken.None);
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(["1", "2"], plex.RefreshedSections.ToArray());
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(40));
        Assert.Equal(2, result.Libraries);
        Assert.Equal(1, result.Selected);
        Assert.Equal(1, result.Missing);
        Assert.Equal(9, plex.Selected[0].StreamId);
    }

    [Fact]
    public async Task ScheduledRefresh_SkipsLibrariesThatAreSwitchedOff()
    {
        var plex = new RecordingPlex
        {
            Sections =
            [
                new PlexLibrarySection("1", "movie", "Movies"),
                new PlexLibrarySection("2", "show", "TV Shows")
            ]
        };
        var service = Service(plex, keys: """["1"]""");

        var result = await service.RefreshScheduledAsync(CancellationToken.None);

        Assert.Equal(["1"], plex.RefreshedSections.ToArray());
        Assert.Equal(1, result.Libraries);
    }

    [Fact]
    public async Task ManualRefresh_RefreshesOnlyThatLibrary()
    {
        var plex = new RecordingPlex
        {
            Sections =
            [
                new PlexLibrarySection("1", "movie", "Movies"),
                new PlexLibrarySection("2", "show", "TV Shows")
            ]
        };
        var service = Service(plex, keys: "[]");

        await service.RefreshLibraryAsync("2", CancellationToken.None);

        Assert.Equal(["2"], plex.RefreshedSections.ToArray());
    }

    [Fact]
    public void JobGate_RejectsASecondEnterUntilExit()
    {
        Assert.True(PlexLibraryRefreshJob.TryEnter());
        Assert.True(PlexLibraryRefreshJob.IsRunning);
        Assert.False(PlexLibraryRefreshJob.TryEnter());
        PlexLibraryRefreshJob.Exit();
        Assert.False(PlexLibraryRefreshJob.IsRunning);
        Assert.True(PlexLibraryRefreshJob.TryEnter());
        PlexLibraryRefreshJob.Exit();
    }

    [Fact]
    public async Task ManualRefresh_GetMetadataTimeout_CountsMissingAndDoesNotThrow()
    {
        var plex = new RecordingPlex
        {
            Sections = [new PlexLibrarySection("1", "movie", "Movies")],
            Pages = { ["1"] = Page("11") },
            TimeoutOnMetadata = true
        };
        var service = Service(plex, keys: "[]");

        var result = await service.RefreshLibraryAsync("1", CancellationToken.None);

        Assert.Equal(["1"], plex.RefreshedSections.ToArray());
        Assert.Equal(1, result.Missing);
        Assert.Equal(0, result.Selected);
    }

    [Fact]
    public async Task ManualRefresh_RefreshSectionTimeout_StillSelectsSubtitles()
    {
        var plex = new RecordingPlex
        {
            Sections = [new PlexLibrarySection("1", "movie", "Movies")],
            Pages = { ["1"] = Page("11") },
            Metadata =
            {
                ["11"] = Metadata(
                    "E:\\media\\movies\\Example\\Example.mkv",
                    """{"id":9,"streamType":3,"languageCode":"bul","languageTag":"bg","selected":0,"forced":0,"extendedDisplayTitle":"Bulgarian (SRT External)"}""")
            },
            TimeoutOnRefresh = true
        };
        var service = Service(plex, keys: "[]");

        var result = await service.RefreshLibraryAsync("1", CancellationToken.None);

        Assert.Equal(["1"], plex.RefreshedSections.ToArray());
        Assert.Equal(1, result.Selected);
        Assert.Equal(9, plex.Selected[0].StreamId);
    }

    private static PlexLibraryRefreshService Service(
        RecordingPlex plex,
        string keys,
        PlexLibraryRefreshOptions? options = null)
    {
        var values = new Dictionary<string, string>
        {
            [SettingKeys.MediaServers.PlexUrl] = "http://plex:32400",
            [SettingKeys.MediaServers.PlexToken] = "test-token",
            [SettingKeys.MediaServers.PlexClientIdentifier] = "client-1",
            [SettingKeys.MediaServers.PlexDefaultSubtitleLanguage] = "bg",
            [SettingKeys.MediaServers.PlexLibraryRefreshKeys] = keys
        };
        var settings = new Mock<ISettingService>();
        settings.Setup(item => item.GetSetting(It.IsAny<string>()))
            .Returns((string key) => Task.FromResult<string?>(values.GetValueOrDefault(key)));
        settings.Setup(item => item.GetEncryptedSetting(It.IsAny<string>()))
            .Returns((string key) => Task.FromResult<string?>(values.GetValueOrDefault(key)));
        return new PlexLibraryRefreshService(
            settings.Object,
            plex,
            options ?? new PlexLibraryRefreshOptions
            {
                Stagger = TimeSpan.Zero,
                PollInterval = TimeSpan.Zero,
                MinimumScanWait = TimeSpan.Zero,
                ScanTimeout = TimeSpan.FromSeconds(1),
                PageSize = 50
            },
            NullLogger<PlexLibraryRefreshService>.Instance);
    }

    private static string Page(string ratingKey) =>
        "{\"MediaContainer\":{\"totalSize\":1,\"Metadata\":[{\"ratingKey\":\""
        + ratingKey
        + "\",\"type\":\"movie\"}]}}";

    private static string Metadata(string file, string stream)
    {
        var payload = new Dictionary<string, object>
        {
            ["MediaContainer"] = new Dictionary<string, object>
            {
                ["librarySectionID"] = 1,
                ["Metadata"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["ratingKey"] = "42",
                        ["librarySectionID"] = 1,
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
                                        ["Stream"] = JsonSerializer.Deserialize<JsonElement>("[" + stream + "]")
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

    private sealed class RecordingPlex : IPlexClient
    {
        public IReadOnlyList<PlexLibrarySection> Sections { get; init; } = [];
        public Dictionary<string, string> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RefreshedSections { get; } = [];
        public List<(long PartId, long StreamId)> Selected { get; } = [];
        public bool TimeoutOnMetadata { get; init; }
        public bool TimeoutOnRefresh { get; init; }

        public Task<PlexPin> CreatePinAsync(string clientId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<string?> PollPinAsync(long pinId, string clientId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PlexAccount?> GetUserAsync(string token, string clientId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

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
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<PlexLibrarySection>> GetLibrariesAsync(
            string baseUrl,
            string token,
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Sections);

        public Task<string> GetLibraryPageAsync(
            string baseUrl,
            string token,
            string clientId,
            string sectionKey,
            int metadataType,
            int start,
            int size,
            CancellationToken cancellationToken)
        {
            if (start > 0 || !Pages.TryGetValue(sectionKey, out var page))
            {
                return Task.FromResult("""{"MediaContainer":{"totalSize":0,"Metadata":[]}}""");
            }

            return Task.FromResult(page);
        }

        public Task<string> GetMetadataAsync(
            string baseUrl,
            string token,
            string clientId,
            string ratingKey,
            CancellationToken cancellationToken)
        {
            if (TimeoutOnMetadata)
            {
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
            }

            if (!Metadata.TryGetValue(ratingKey, out var body))
            {
                throw new HttpRequestException("missing");
            }

            return Task.FromResult(body);
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
            CancellationToken cancellationToken)
        {
            RefreshedSections.Add(sectionId);
            if (TimeoutOnRefresh)
            {
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
            }

            return Task.CompletedTask;
        }

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
            Selected.Add((partId, streamId));
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
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
