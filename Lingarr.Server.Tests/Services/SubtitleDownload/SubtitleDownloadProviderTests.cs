using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.ProviderHealth;
using Lingarr.Server.Services.SubtitleDownload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.SubtitleDownload;

/// <summary>
/// The two key-based providers against recorded payloads, plus the pacer that keeps them inside
/// their published rates. No network, and no real waiting: the pacer runs on a clock the test
/// moves by hand.
/// </summary>
public class SubtitleDownloadProviderTests
{
    private const string Srt =
        "1\n00:00:01,000 --> 00:00:02,000\nMy name is Walter Hartwell White.\n\n" +
        "2\n00:00:03,000 --> 00:00:04,500\nThe night nurse is here and the kettle is on.\n\n" +
        "3\n00:00:05,000 --> 00:00:06,500\nYou don't have to get up for this one.\n";

    private static readonly SubtitleDownloadQuery EpisodeQuery = new()
    {
        MediaType = MediaType.Episode,
        MediaId = 1396,
        Title = "Pilot",
        SeriesTitle = "Breaking Bad",
        SeasonNumber = 1,
        EpisodeNumber = 1,
        Language = "en",
        FileName = "Breaking.Bad.S01E01.1080p.BluRay.x265-RARBG.mkv"
    };

    /// <summary>
    /// Two rows for the same season, in the order SubSource returns them: the wrong episode first
    /// so a provider that takes the first row downloads the wrong subtitle.
    /// </summary>
    private const string SubsourceSubtitlesPayload =
        """
        {
          "success": true,
          "total": 2,
          "data": [
            {
              "subtitleId": 111,
              "language": "english",
              "releaseInfo": ["Breaking.Bad.S01E02.1080p.BluRay.x265-RARBG"],
              "downloads": 912,
              "hearingImpaired": false,
              "rating": { "good": 12, "bad": 1 },
              "productionType": "tv",
              "releaseType": "bluray"
            },
            {
              "subtitleId": 222,
              "language": "english",
              "releaseInfo": ["Breaking.Bad.S01E01.1080p.BluRay.x265-RARBG_forced"],
              "downloads": 4,
              "hearingImpaired": false,
              "rating": { "good": 1, "bad": 0 },
              "productionType": "tv",
              "releaseType": "bluray"
            }
          ]
        }
        """;

    private const string SubsourceMoviesPayload =
        """
        {
          "success": true,
          "data": [
            { "movieId": 7192, "title": "Breaking Bad", "year": 2008 }
          ]
        }
        """;

    [Fact]
    public async Task Subsource_RefusesTheWrongEpisodeAndDownloadsTheMatchingRow()
    {
        var provider = SubtitleDownloadCatalog.Find("subsource")!;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetEncryptedSetting(provider.SettingKey("apikey"))).ReturnsAsync("fixture-key");
        var calls = new List<Uri>();
        var keys = new List<string?>();
        using var handler = new Handler(request =>
        {
            calls.Add(request.RequestUri!);
            keys.Add(request.Headers.TryGetValues("X-API-Key", out var values) ? string.Join(",", values) : null);
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/movies/search" => Json(SubsourceMoviesPayload),
                "/api/v1/subtitles" => Json(SubsourceSubtitlesPayload),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Srt, Encoding.UTF8, "application/zip")
                }
            };
        });

        var hit = await Gateway(handler, settings).DownloadAsync(
            provider, EpisodeQuery, CancellationToken.None);

        Assert.NotNull(hit);
        Assert.Equal(3, calls.Count);
        Assert.Contains("searchType=text", calls[0].Query);
        Assert.Contains("q=Breaking%20Bad", calls[0].Query);
        Assert.Contains("movieId=7192", calls[1].Query);
        Assert.Contains("language=english", calls[1].Query);
        Assert.Contains("limit=100", calls[1].Query);
        Assert.Contains("page=1", calls[1].Query);
        Assert.Contains("sort=popular", calls[1].Query);
        Assert.Equal("/api/v1/subtitles/222/download", calls[2].AbsolutePath);
        Assert.DoesNotContain(calls, uri => uri.AbsolutePath.Contains("/subtitles/111/"));
        Assert.All(keys, key => Assert.Equal("fixture-key", key));
        Assert.DoesNotContain(calls, uri => uri.Query.Contains("apikey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Subsource_UnmappedLanguageAsksTheProviderForNothing()
    {
        var provider = SubtitleDownloadCatalog.Find("subsource")!;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetEncryptedSetting(provider.SettingKey("apikey"))).ReturnsAsync("fixture-key");
        var calls = 0;
        using var handler = new Handler(_ =>
        {
            calls++;
            return Json(SubsourceMoviesPayload);
        });

        var query = new SubtitleDownloadQuery
        {
            MediaType = MediaType.Movie,
            MediaId = 1,
            Title = "Example Movie",
            Year = 2024,
            Language = "xx"
        };

        var hit = await Gateway(handler, settings).DownloadAsync(provider, query, CancellationToken.None);

        Assert.Null(hit);
        Assert.Equal(0, calls);
        Assert.Null(SubtitleDownloadGateway.SubsourceLanguage("xx"));
        Assert.Equal("bulgarian", SubtitleDownloadGateway.SubsourceLanguage("bg"));
    }

    /// <summary>
    /// A season pack, unpacked by the API. The first file is the wrong episode, so a provider that
    /// takes the first URL downloads S01E02 for an S01E01 request.
    /// </summary>
    private const string SubdlPayload =
        """
        {
          "status": true,
          "subtitles": [
            {
              "url": "/subtitle/48213.zip",
              "language": "EN",
              "season": 1,
              "episode": 0,
              "hi": false,
              "unpack_files": [
                {
                  "file_n_id": 778898,
                  "name": "Breaking.Bad.S01E02.720p.HDTV.x264.en.srt",
                  "release_name": "Breaking.Bad.S01E02.720p.HDTV.x264",
                  "season": 1,
                  "episode": 2,
                  "language": "EN",
                  "hi": false,
                  "format": "srt",
                  "url": "/subtitle/48213/778898"
                },
                {
                  "file_n_id": 778899,
                  "name": "Breaking.Bad.S01E01.720p.HDTV.x264.en.srt",
                  "release_name": "Breaking.Bad.S01E01.720p.HDTV.x264",
                  "season": 1,
                  "episode": 1,
                  "language": "EN",
                  "hi": false,
                  "format": "srt",
                  "url": "/subtitle/48213/778899"
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task Subdl_DownloadsTheUnpackedEpisodeAndNotTheSeasonPack()
    {
        var provider = SubtitleDownloadCatalog.Find("subdl")!;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetEncryptedSetting(provider.SettingKey("api_key"))).ReturnsAsync("fixture-key");
        var calls = new List<Uri>();
        using var handler = new Handler(request =>
        {
            calls.Add(request.RequestUri!);
            return calls.Count == 1
                ? Json(SubdlPayload)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Srt, Encoding.UTF8, "text/plain")
                };
        });

        var hit = await Gateway(handler, settings).DownloadAsync(provider, EpisodeQuery, CancellationToken.None);

        Assert.NotNull(hit);
        Assert.Equal(2, calls.Count);
        Assert.Contains("unpack=1", calls[0].Query);
        Assert.Contains("subs_per_page=30", calls[0].Query);
        Assert.Contains("client=custom_integration", calls[0].Query);
        Assert.Contains("episode_number=1", calls[0].Query);
        Assert.Equal("/subtitle/48213/778899", calls[1].AbsolutePath);
        Assert.DoesNotContain(calls, uri => uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Subdl_SelectionFallsBackToThePackWhenNoFileMatches()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            """
            {
              "subtitles": [
                {
                  "url": "/subtitle/48213.zip",
                  "hi": false,
                  "unpack_files": [
                    { "episode": 4, "season": 1, "language": "EN", "hi": false, "url": "/subtitle/48213/778890" }
                  ]
                },
                { "url": "/subtitle/48214.zip", "hi": true }
              ]
            }
            """);

        var selection = SubtitleDownloadGateway.SelectSubdlFile(
            doc.RootElement.GetProperty("subtitles"), EpisodeQuery);

        Assert.Null(selection.FileUrl);
        Assert.Equal("https://dl.subdl.com/subtitle/48213.zip", selection.PackUrl);
    }

    [Fact]
    public async Task Pacer_RetryAfterKeepsTheProviderUnavailableUntilTheWindowPasses()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var pacer = new SubtitleProviderPacer(clock);
        var provider = SubtitleDownloadCatalog.Find("subsource")!;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetEncryptedSetting(provider.SettingKey("apikey"))).ReturnsAsync("fixture-key");
        var calls = 0;
        using var handler = new Handler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.TryAddWithoutValidation("Retry-After", "120");
            return response;
        });
        var gateway = Gateway(handler, settings, pacer);

        Assert.Null(await gateway.DownloadAsync(provider, EpisodeQuery, CancellationToken.None));
        Assert.Equal(1, calls);

        var blocked = pacer.Check(SubtitleProviderPacer.Subsource);
        Assert.False(blocked.Allowed);
        Assert.Equal(TimeSpan.FromSeconds(120), blocked.RetryAfter);
        Assert.Contains("429", blocked.Reason);

        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.False(pacer.Check(SubtitleProviderPacer.Subsource).Allowed);
        Assert.Null(await gateway.DownloadAsync(provider, EpisodeQuery, CancellationToken.None));
        Assert.Equal(1, calls);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(pacer.Check(SubtitleProviderPacer.Subsource).Allowed);
    }

    [Fact]
    public async Task Pacer_RateLimitHeadersPauseTheHourAndFinishTheDay()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var pacer = new SubtitleProviderPacer(clock);

        pacer.Observe(SubtitleProviderPacer.Subsource, 200, null, remainingHour: 12, remainingDay: 400);
        var paused = pacer.Check(SubtitleProviderPacer.Subsource);
        Assert.False(paused.Allowed);
        Assert.Equal(TimeSpan.FromMinutes(10), paused.RetryAfter);

        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.True(pacer.Check(SubtitleProviderPacer.Subsource).Allowed);

        pacer.Observe(SubtitleProviderPacer.Subsource, 200, null, remainingHour: 400, remainingDay: 0);
        var finished = pacer.Check(SubtitleProviderPacer.Subsource);
        Assert.False(finished.Allowed);
        Assert.Contains("finished for today", finished.Reason);
    }

    [Fact]
    public async Task Pacer_ServerErrorsBackOffFurtherEachTimeUpToTheCap()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var pacer = new SubtitleProviderPacer(clock);

        pacer.Observe(SubtitleProviderPacer.Subdl, 503);
        Assert.Equal(TimeSpan.FromSeconds(60), pacer.Check(SubtitleProviderPacer.Subdl).RetryAfter);

        clock.Advance(TimeSpan.FromMinutes(2));
        pacer.Observe(SubtitleProviderPacer.Subdl, 502);
        Assert.Equal(TimeSpan.FromSeconds(120), pacer.Check(SubtitleProviderPacer.Subdl).RetryAfter);

        for (var attempt = 0; attempt < 6; attempt++)
        {
            clock.Advance(TimeSpan.FromMinutes(31));
            pacer.Observe(SubtitleProviderPacer.Subdl, 500);
        }

        Assert.Equal(TimeSpan.FromSeconds(1800), pacer.Check(SubtitleProviderPacer.Subdl).RetryAfter);

        clock.Advance(TimeSpan.FromMinutes(31));
        pacer.Observe(SubtitleProviderPacer.Subdl, 200);
        Assert.True(pacer.Check(SubtitleProviderPacer.Subdl).Allowed);
    }

    [Fact]
    public async Task Pacer_StopsAtTheDailyRequestBudgetWithoutSleeping()
    {
        var pacer = new SubtitleProviderPacer(new InstantClock());

        for (var call = 1; call <= 300; call++)
        {
            var decision = await pacer.AcquireAsync(
                SubtitleProviderPacer.Subdl, PacerCallKind.Request, CancellationToken.None);
            Assert.True(decision.Allowed, $"call {call} was refused: {decision.Reason}");
        }

        Assert.Equal(300, pacer.RequestsUsed(SubtitleProviderPacer.Subdl));

        var blocked = await pacer.AcquireAsync(
            SubtitleProviderPacer.Subdl, PacerCallKind.Request, CancellationToken.None);
        Assert.False(blocked.Allowed);
        Assert.Contains("daily request budget is spent", blocked.Reason);
        Assert.Contains("300/300", blocked.Reason);
    }

    [Fact]
    public async Task Pacer_CountsDownloadsSeparatelyAndStopsAtTheirBudget()
    {
        var pacer = new SubtitleProviderPacer(new InstantClock());

        for (var call = 1; call <= 40; call++)
        {
            var decision = await pacer.AcquireAsync(
                SubtitleProviderPacer.Subdl, PacerCallKind.Download, CancellationToken.None);
            Assert.True(decision.Allowed, $"download {call} was refused: {decision.Reason}");
        }

        var blocked = pacer.Check(SubtitleProviderPacer.Subdl, PacerCallKind.Download);
        Assert.False(blocked.Allowed);
        Assert.Contains("daily download budget is spent", blocked.Reason);
        Assert.Equal(40, pacer.DownloadsUsed(SubtitleProviderPacer.Subdl));
        Assert.True(pacer.Check(SubtitleProviderPacer.Subdl, PacerCallKind.Request).Allowed);
    }

    [Fact]
    public async Task Pacer_FinishedProviderIsSkippedWithOneLineAndNoRequest()
    {
        var pacer = new SubtitleProviderPacer(new InstantClock());
        for (var call = 1; call <= 300; call++)
        {
            await pacer.AcquireAsync(SubtitleProviderPacer.Subdl, PacerCallKind.Request, CancellationToken.None);
        }

        var provider = SubtitleDownloadCatalog.Find("subdl")!;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetEncryptedSetting(provider.SettingKey("api_key"))).ReturnsAsync("fixture-key");
        var calls = 0;
        using var handler = new Handler(_ =>
        {
            calls++;
            return Json(SubdlPayload);
        });
        var logger = new Mock<ILogger<SubtitleDownloadGateway>>();

        var gateway = new SubtitleDownloadGateway(
            Factory(handler), settings.Object, logger.Object, pacer);

        Assert.Null(await gateway.DownloadAsync(provider, EpisodeQuery, CancellationToken.None));
        Assert.Equal(0, calls);
        Assert.Single(
            logger.Invocations,
            invocation => invocation.Method.Name == "Log"
                          && Equals(invocation.Arguments[0], LogLevel.Information));
    }

    [Fact]
    public void Pacer_429WithoutRetryAfterCoolsDownForFifteenMinutes()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var pacer = new SubtitleProviderPacer(clock);

        pacer.Observe(SubtitleProviderPacer.Subdl, 429);

        Assert.Equal(TimeSpan.FromSeconds(900), pacer.Check(SubtitleProviderPacer.Subdl).RetryAfter);
    }

    /// <summary>
    /// The registration in <c>ServiceCollectionExtensions</c> is one line with no factory, so the
    /// pacer's clock must be optional or the container cannot build it.
    /// </summary>
    [Fact]
    public void Pacer_ResolvesAsASingletonWithoutARegisteredClock()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SubtitleProviderPacer>();
        using var provider = services.BuildServiceProvider();

        var pacer = provider.GetRequiredService<SubtitleProviderPacer>();

        Assert.Same(TimeProvider.System, pacer.Clock);
        Assert.Same(pacer, provider.GetRequiredService<SubtitleProviderPacer>());
    }

    [Fact]
    public void Subsource_EpisodeGateReadsReleaseNames()
    {
        Assert.True(SubtitleDownloadGateway.ReleaseEpisodeMatches(
            "Breaking.Bad.S03E01.1080p.BluRay.x265-RARBG_forced", 3, 1));
        Assert.True(SubtitleDownloadGateway.ReleaseEpisodeMatches("Breaking.Bad.S03.E01.1080p", 3, 1));
        Assert.True(SubtitleDownloadGateway.ReleaseEpisodeMatches("Breaking Bad 3x01 720p", 3, 1));
        Assert.False(SubtitleDownloadGateway.ReleaseEpisodeMatches(
            "Breaking.Bad.S03E01.1080p.BluRay.x265-RARBG_forced", 3, 2));
        Assert.False(SubtitleDownloadGateway.ReleaseEpisodeMatches("1920x1080 Breaking Bad", 3, 1));
        Assert.False(SubtitleDownloadGateway.ReleaseEpisodeMatches("Breaking Bad Complete Season 3", 3, 1));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit", true)]
    [InlineData(HttpStatusCode.Unauthorized, "authentication", false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "provider_unavailable", true)]
    public async Task Subsource_RecordsARefusalAsAHealthEvent(
        HttpStatusCode status,
        string family,
        bool transient)
    {
        var provider = SubtitleDownloadCatalog.Find("subsource")!;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetEncryptedSetting(provider.SettingKey("apikey"))).ReturnsAsync("fixture-key");
        var recorded = new List<ProviderOperationalResult>();
        var health = new Mock<IProviderHealthService>();
        health
            .Setup(h => h.RecordAsync(It.IsAny<ProviderOperationalResult>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderOperationalResult, CancellationToken>((result, _) => recorded.Add(result))
            .Returns(Task.CompletedTask);
        using var handler = new Handler(_ => new HttpResponseMessage(status));

        var hit = await Gateway(handler, settings, health: health.Object)
            .DownloadAsync(provider, EpisodeQuery, CancellationToken.None);

        Assert.Null(hit);
        var single = Assert.Single(recorded);
        Assert.Equal("subsource", single.Provider);
        Assert.Equal("subtitle-download", single.Operation);
        Assert.Equal("failure", single.Outcome);
        Assert.Equal(family, single.ErrorFamily);
        Assert.Equal(transient, single.IsTransient);
    }

    [Fact]
    public async Task Subsource_RecordsNothingForAMiss()
    {
        var provider = SubtitleDownloadCatalog.Find("subsource")!;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetEncryptedSetting(provider.SettingKey("apikey"))).ReturnsAsync("fixture-key");
        var health = new Mock<IProviderHealthService>(MockBehavior.Strict);
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var hit = await Gateway(handler, settings, health: health.Object)
            .DownloadAsync(provider, EpisodeQuery, CancellationToken.None);

        Assert.Null(hit);
        Assert.Empty(health.Invocations);
    }

    private static SubtitleDownloadGateway Gateway(
        HttpMessageHandler handler,
        Mock<ISettingService> settings,
        SubtitleProviderPacer? pacer = null,
        IProviderHealthService? health = null) =>
        new(Factory(handler), settings.Object, NullLogger<SubtitleDownloadGateway>.Instance,
            pacer ?? new SubtitleProviderPacer(new InstantClock()), health);

    private static IHttpClientFactory Factory(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("subtitle-download")).Returns(() => new HttpClient(handler, false));
        return factory.Object;
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    /// <summary>
    /// A clock that fires every timer immediately, so the minimum interval between two calls costs
    /// a test nothing. The date stays fixed, which keeps the daily counters on one day.
    /// </summary>
    private sealed class InstantClock : TimeProvider
    {
        private readonly DateTimeOffset _now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now.UtcTicks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime > TimeSpan.Zero)
            {
                callback(state);
            }

            return new NoopTimer();
        }

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A clock the test moves by hand, for the windows a pacer is supposed to sit out.
    /// </summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now.UtcTicks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, _now + (dueTime > TimeSpan.Zero ? dueTime : TimeSpan.Zero));
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            _now += delta;
            foreach (var timer in _timers.ToArray())
            {
                timer.FireIfDue(_now);
            }
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, DateTimeOffset dueAt) : ITimer
        {
            private DateTimeOffset _dueAt = dueAt;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _dueAt = DateTimeOffset.MaxValue;
                return true;
            }

            public void FireIfDue(DateTimeOffset now)
            {
                if (_disposed || now < _dueAt)
                {
                    return;
                }

                _disposed = true;
                callback(state);
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                _disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
