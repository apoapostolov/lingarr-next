using System.IO;
using System.Text;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Integration.Plex;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexWebhookTests
{
    private const string ElixirPayload = """
        {
          "event": "library.new",
          "Metadata": {
            "type": "movie",
            "title": "Abadi Nan Jaya",
            "year": 2025,
            "ratingKey": "49051",
            "guid": "plex://movie/6677b2e1ce506be9d631836d",
            "Guid": [
              {"id": "imdb://tt32643830"},
              {"id": "tmdb://1306525"},
              {"id": "tvdb://369550"}
            ]
          }
        }
        """;

    [Fact]
    public void Read_AcceptsANewMovieAndKeepsExternalIds()
    {
        var decision = PlexWebhookReader.Read(ElixirPayload);

        var added = Assert.IsType<PlexWebhookDecision.Added>(decision);
        Assert.Equal("Abadi Nan Jaya", added.Item.Title);
        Assert.Equal(2025, added.Item.Year);
        Assert.Equal("49051", added.Item.RatingKey);
        Assert.Contains("tmdb://1306525", added.Item.Guids);
        Assert.Equal(
            ["{imdb-tt32643830}", "{tmdb-1306525}", "{tvdb-369550}"],
            PlexMovieTags.FromGuids(added.Item.Guids));
    }

    [Fact]
    public void Read_AcceptsANumericRatingKey()
    {
        var decision = PlexWebhookReader.Read("""
            {"event":"library.new","Metadata":{"type":"movie","title":"Example","ratingKey":12,"Guid":["tmdb://9"]}}
            """);

        var added = Assert.IsType<PlexWebhookDecision.Added>(decision);
        Assert.Equal("12", added.Item.RatingKey);
        Assert.Equal(["{tmdb-9}"], PlexMovieTags.FromGuids(added.Item.Guids));
    }

    [Fact]
    public void Read_AcceptsANewEpisodeAndKeepsTheShowPosition()
    {
        var decision = PlexWebhookReader.Read("""
            {
              "event":"library.new",
              "Metadata":{
                "type":"episode",
                "title":"NINE TEN",
                "grandparentTitle":"#1 HAPPY FAMILY USA",
                "parentIndex":1,
                "index":1,
                "ratingKey":"6726",
                "grandparentRatingKey":"6724",
                "Guid":[{"id":"tvdb://9565927"},{"id":"tvdb://429874/1/1"}]
              }
            }
            """);

        var added = Assert.IsType<PlexWebhookDecision.Added>(decision);
        Assert.Equal("episode", added.Item.Kind);
        Assert.Equal("#1 HAPPY FAMILY USA", added.Item.ShowTitle);
        Assert.Equal(1, added.Item.SeasonNumber);
        Assert.Equal(1, added.Item.EpisodeNumber);
        Assert.Equal("6724", added.Item.ShowRatingKey);
        Assert.Contains("tvdb://429874/1/1", added.Item.Guids);
        Assert.Contains("tvdb://9565927", added.Item.Guids);
    }

    [Fact]
    public void Read_IgnoresPlayback()
    {
        Assert.IsType<PlexWebhookDecision.Ignored>(PlexWebhookReader.Read("""
            {"event":"media.play","Metadata":{"type":"movie","title":"Example"}}
            """));
        Assert.IsType<PlexWebhookDecision.Ignored>(PlexWebhookReader.Read("""
            {"event":"media.rate","Metadata":{"type":"episode","title":"Pilot"}}
            """));
    }

    [Fact]
    public void Read_RejectsAPayloadThatIsNotJson()
    {
        Assert.IsType<PlexWebhookDecision.Unreadable>(PlexWebhookReader.Read("payload"));
    }

    [Fact]
    public async Task Process_TranslatesTheMovieMatchedByTmdbTag()
    {
        await using var database = CreateDatabase();
        var elixir = Movie(
            7,
            "The Elixir",
            "/media/media/movies/The Elixir (2025) {tmdb-1306525}",
            "The Elixir (2025) {tmdb-1306525}");
        var other = Movie(8, "Abadi Nan Jaya", "/media/media/movies/Other", "Other");
        database.Movies.AddRange(elixir, other);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        var processor = new Mock<IMediaSubtitleProcessor>();
        processor
            .Setup(service => service.ProcessMedia(It.IsAny<IMedia>(), MediaType.Movie))
            .ReturnsAsync(true);
        var radarr = new Mock<IRadarrService>();
        var job = CreateJob(database, processor, Settings(), radarr: radarr);

        await job.ProcessPlexWebhook(AddedMovie());

        processor.Verify(
            service => service.ProcessMedia(It.Is<IMedia>(media => media.Id == 7), MediaType.Movie),
            Times.Once);
        radarr.Verify(
            service => service.FindLibraryMovie(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Process_DoesNotMatchAMovieByThePlexTitleAlone()
    {
        await using var database = CreateDatabase();
        database.Movies.Add(Movie(8, "Abadi Nan Jaya", "/media/media/movies/Other", "Other"));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        var processor = new Mock<IMediaSubtitleProcessor>();
        var job = CreateJob(database, processor, Settings());

        await job.ProcessPlexWebhook(new PlexAddedMovie
        {
            Title = "Abadi Nan Jaya",
            RatingKey = "49051",
            Guids = ["tmdb://1306525"]
        });

        processor.Verify(
            service => service.ProcessMedia(It.IsAny<IMedia>(), It.IsAny<MediaType>()),
            Times.Never);
    }

    [Fact]
    public async Task Process_DoesNotTreatAShorterIdAsTheSameMovie()
    {
        await using var database = CreateDatabase();
        database.Movies.Add(Movie(
            7,
            "The Elixir",
            "/media/media/movies/The Elixir (2025) {tmdb-1306525}",
            "The Elixir"));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        var processor = new Mock<IMediaSubtitleProcessor>();
        var job = CreateJob(database, processor, Settings());

        await job.ProcessPlexWebhook(new PlexAddedMovie
        {
            Title = "The Elixir",
            Guids = ["tmdb://13"]
        });

        processor.Verify(
            service => service.ProcessMedia(It.IsAny<IMedia>(), It.IsAny<MediaType>()),
            Times.Never);
    }

    [Fact]
    public async Task Process_SkipsWhenTheSettingIsOff()
    {
        await using var database = CreateDatabase();
        database.Movies.Add(Movie(
            7,
            "The Elixir",
            "/media/media/movies/The Elixir (2025) {tmdb-1306525}",
            "The Elixir"));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        var processor = new Mock<IMediaSubtitleProcessor>();
        var job = CreateJob(database, processor, Settings("false"));

        await job.ProcessPlexWebhook(AddedMovie());

        processor.Verify(
            service => service.ProcessMedia(It.IsAny<IMedia>(), It.IsAny<MediaType>()),
            Times.Never);
    }

    [Fact]
    public async Task Process_SkipsAnEpisodeWhenThatSwitchIsOff()
    {
        await using var database = CreateDatabase();
        database.Episodes.Add(Episode(9, "Outlander", 8, 9));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        var processor = new Mock<IMediaSubtitleProcessor>();
        var job = CreateJob(database, processor, Settings(movies: "true", episodes: "false"));

        await job.ProcessPlexWebhook(new PlexAddedMovie
        {
            Kind = "episode",
            Title = "Pharos",
            ShowTitle = "Outlander",
            SeasonNumber = 8,
            EpisodeNumber = 9
        });

        processor.Verify(
            service => service.ProcessMedia(It.IsAny<IMedia>(), It.IsAny<MediaType>()),
            Times.Never);
    }

    [Fact]
    public async Task Process_ReadsIdsFromPlexWhenTheWebhookHasOnlyAPlexGuid()
    {
        await using var database = CreateDatabase();
        database.Movies.Add(Movie(
            7,
            "The Elixir",
            "/media/media/movies/The Elixir (2025) {tmdb-1306525}",
            "The Elixir"));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        var processor = new Mock<IMediaSubtitleProcessor>();
        processor
            .Setup(service => service.ProcessMedia(It.IsAny<IMedia>(), MediaType.Movie))
            .ReturnsAsync(false);
        var plex = new Mock<IPlexClient>();
        plex.Setup(client => client.GetMetadataAsync(
                "http://plex.example:32400",
                "plex-token",
                "client-id",
                "49051",
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("""
                {"MediaContainer":{"Metadata":[{"ratingKey":"49051","Guid":[{"id":"tmdb://1306525"}]}]}}
                """);
        var job = CreateJob(database, processor, Settings(), plex: plex);

        await job.ProcessPlexWebhook(new PlexAddedMovie
        {
            Title = "Abadi Nan Jaya",
            RatingKey = "49051",
            Guids = ["plex://movie/6677b2e1ce506be9d631836d"]
        });

        processor.Verify(
            service => service.ProcessMedia(It.Is<IMedia>(media => media.Id == 7), MediaType.Movie),
            Times.Once);
    }

    [Fact]
    public async Task Process_SyncsTheMovieFromRadarrWhenLingarrDoesNotHaveItYet()
    {
        await using var database = CreateDatabase();
        database.Movies.Add(Movie(42, "The Elixir", "/media/media/movies/The Elixir", "The Elixir"));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        var processor = new Mock<IMediaSubtitleProcessor>();
        processor
            .Setup(service => service.ProcessMedia(It.IsAny<IMedia>(), MediaType.Movie))
            .ReturnsAsync(true);
        var radarr = new Mock<IRadarrService>();
        radarr.Setup(service => service.FindLibraryMovie("tmdb", "1306525"))
            .ReturnsAsync(new RadarrMovie
            {
                Id = 1344,
                Title = "The Elixir",
                Path = "/movies/The Elixir",
                RootFolderPath = "/movies",
                Added = "2025-01-01",
                HasFile = true
            });
        var media = new Mock<IMediaService>();
        media.Setup(service => service.GetMovieIdOrSyncFromRadarrMovieId(1344)).ReturnsAsync(42);
        var job = CreateJob(database, processor, Settings(), radarr: radarr, media: media);

        await job.ProcessPlexWebhook(AddedMovie());

        processor.Verify(
            service => service.ProcessMedia(It.Is<IMedia>(item => item.Id == 42), MediaType.Movie),
            Times.Once);
    }

    [Fact]
    public async Task Process_TranslatesTheEpisodeMatchedByShowSeasonAndNumber()
    {
        await using var database = CreateDatabase();
        database.Episodes.Add(Episode(9, "Outlander", 8, 9));
        database.Episodes.Add(Episode(10, "House", 8, 9));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        var processor = new Mock<IMediaSubtitleProcessor>();
        processor
            .Setup(service => service.ProcessMedia(It.IsAny<IMedia>(), MediaType.Episode))
            .ReturnsAsync(true);
        var job = CreateJob(database, processor, Settings());

        await job.ProcessPlexWebhook(new PlexAddedMovie
        {
            Kind = "episode",
            Title = "Pharos",
            ShowTitle = "Outlander",
            SeasonNumber = 8,
            EpisodeNumber = 9,
            Guids = ["tvdb://9565927", "imdb://tt18566422"]
        });

        processor.Verify(
            service => service.ProcessMedia(It.Is<IMedia>(media => media.Id == 9), MediaType.Episode),
            Times.Once);
    }

    [Fact]
    public async Task Process_MatchesAnEpisodeByTheShowIdWhenThePlexTitleDiffers()
    {
        await using var database = CreateDatabase();
        var episode = Episode(11, "Different Name", 1, 1);
        episode.Season.Show.Path = "/media/media/tvshows/#1 HAPPY FAMILY USA {tvdb-429874}";
        database.Episodes.Add(episode);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        var processor = new Mock<IMediaSubtitleProcessor>();
        processor
            .Setup(service => service.ProcessMedia(It.IsAny<IMedia>(), MediaType.Episode))
            .ReturnsAsync(false);
        var plex = new Mock<IPlexClient>();
        plex.Setup(client => client.GetMetadataAsync(
                "http://plex.example:32400",
                "plex-token",
                "client-id",
                "6724",
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("""
                {"MediaContainer":{"Metadata":[{"type":"show","Guid":[{"id":"tvdb://429874"}]}]}}
                """);
        var job = CreateJob(database, processor, Settings(), plex: plex);

        await job.ProcessPlexWebhook(new PlexAddedMovie
        {
            Kind = "episode",
            Title = "NINE TEN",
            ShowTitle = "Some Other Plex Title",
            SeasonNumber = 1,
            EpisodeNumber = 1,
            ShowRatingKey = "6724",
            Guids = ["tvdb://9565927"]
        });

        processor.Verify(
            service => service.ProcessMedia(It.Is<IMedia>(media => media.Id == 11), MediaType.Episode),
            Times.Once);
    }

    [Fact]
    public async Task Process_SyncsTheEpisodeFromSonarrWhenLingarrDoesNotHaveItYet()
    {
        await using var database = CreateDatabase();
        database.Episodes.Add(Episode(42, "Outlander", 8, 9));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        var processor = new Mock<IMediaSubtitleProcessor>();
        processor
            .Setup(service => service.ProcessMedia(It.IsAny<IMedia>(), MediaType.Episode))
            .ReturnsAsync(true);
        var plex = new Mock<IPlexClient>();
        plex.Setup(client => client.GetMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "6724",
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("""
                {"MediaContainer":{"Metadata":[{"Guid":[{"id":"tvdb://429874"}]}]}}
                """);
        var sonarr = new Mock<ISonarrService>();
        sonarr.Setup(service => service.FindLibraryEpisode("tvdb", "429874", 8, 9))
            .ReturnsAsync(new SonarrEpisode
            {
                Id = 900,
                EpisodeNumber = 9,
                Title = "Pharos",
                SeasonNumber = 8,
                HasFile = true
            });
        var media = new Mock<IMediaService>();
        media.Setup(service => service.GetEpisodeIdOrSyncFromSonarrEpisodeId(900)).ReturnsAsync(42);
        var job = CreateJob(database, processor, Settings(), plex: plex, sonarr: sonarr, media: media);

        await job.ProcessPlexWebhook(new PlexAddedMovie
        {
            Kind = "episode",
            Title = "Pharos",
            ShowTitle = "A Title Lingarr Does Not Use",
            SeasonNumber = 8,
            EpisodeNumber = 9,
            ShowRatingKey = "6724"
        });

        processor.Verify(
            service => service.ProcessMedia(It.Is<IMedia>(mediaItem => mediaItem.Id == 42), MediaType.Episode),
            Times.Once);
    }

    [Fact]
    public async Task Controller_QueuesAJsonMovieAndAnEpisode()
    {
        var jobs = new Mock<IBackgroundJobClient>();
        var controller = Controller(jobs);

        var queued = await controller.PlexWebhook();
        var ignoredController = Controller(jobs);
        ignoredController.ControllerContext.HttpContext = JsonContext("""
            {"event":"library.new","Metadata":{"type":"episode","title":"Pilot"}}
            """);
        var ignored = await ignoredController.PlexWebhook();

        Assert.IsType<OkObjectResult>(queued);
        Assert.IsType<OkObjectResult>(ignored);
        jobs.Verify(client => client.Create(
            It.Is<Job>(job => job.Method.Name == "ProcessPlexWebhook"),
            It.IsAny<IState>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Controller_ReadsTheMultipartPayloadField()
    {
        var jobs = new Mock<IBackgroundJobClient>();
        var controller = Controller(jobs);
        controller.ControllerContext.HttpContext = FormContext(ElixirPayload);

        var result = await controller.PlexWebhook();

        Assert.IsType<OkObjectResult>(result);
        jobs.Verify(client => client.Create(
            It.Is<Job>(job => job.Method.Name == "ProcessPlexWebhook"),
            It.IsAny<IState>()), Times.Once);
    }

    private static WebhookController Controller(Mock<IBackgroundJobClient> jobs)
    {
        var controller = new WebhookController(
            jobs.Object,
            [],
            new Mock<ISettingService>().Object,
            NullLogger<WebhookController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = JsonContext(ElixirPayload) };
        return controller;
    }

    private static DefaultHttpContext JsonContext(string json)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        return context;
    }

    private static DefaultHttpContext FormContext(string json)
    {
        var context = new DefaultHttpContext();
        var body = "payload=" + System.Net.WebUtility.UrlEncode(json);
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        return context;
    }

    private static PlexAddedMovie AddedMovie()
    {
        var decision = PlexWebhookReader.Read(ElixirPayload);
        return Assert.IsType<PlexWebhookDecision.Added>(decision).Item;
    }

    private static WebhookJob CreateJob(
        LingarrDbContext database,
        Mock<IMediaSubtitleProcessor> processor,
        Mock<ISettingService> settings,
        Mock<IPlexClient>? plex = null,
        Mock<IRadarrService>? radarr = null,
        Mock<ISonarrService>? sonarr = null,
        Mock<IMediaService>? media = null)
    {
        return new WebhookJob(
            database,
            (media ?? new Mock<IMediaService>()).Object,
            processor.Object,
            settings.Object,
            (plex ?? new Mock<IPlexClient>()).Object,
            (radarr ?? new Mock<IRadarrService>()).Object,
            (sonarr ?? new Mock<ISonarrService>()).Object,
            NullLogger<WebhookJob>.Instance);
    }

    private static Mock<ISettingService> Settings(string movies = "true", string? episodes = null)
    {
        episodes ??= movies;
        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(It.IsAny<string>()))
            .ReturnsAsync((string key) => key switch
            {
                SettingKeys.MediaServers.PlexTranslateMoviesOnLibraryNew => movies,
                SettingKeys.MediaServers.PlexTranslateEpisodesOnLibraryNew => episodes,
                SettingKeys.MediaServers.PlexAuthMethod => "oauth",
                SettingKeys.MediaServers.PlexUrl => "http://plex.example:32400",
                SettingKeys.MediaServers.PlexIgnoreEnvironment => "false",
                SettingKeys.MediaServers.PlexClientIdentifier => "client-id",
                _ => null
            });
        settings.Setup(service => service.GetEncryptedSetting(It.IsAny<string>()))
            .ReturnsAsync("plex-token");
        return settings;
    }

    private static LingarrDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static Movie Movie(int id, string title, string path, string fileName) => new()
    {
        Id = id,
        RadarrId = id,
        Title = title,
        Path = path,
        FileName = fileName,
        DateAdded = System.DateTime.UtcNow
    };

    private static Episode Episode(int id, string showTitle, int seasonNumber, int episodeNumber)
    {
        var show = new Show
        {
            Id = id,
            SonarrId = id,
            Title = showTitle,
            Path = "/media/media/tvshows/" + showTitle,
            DateAdded = System.DateTime.UtcNow
        };
        var season = new Season
        {
            Id = id + 1000,
            SeasonNumber = seasonNumber,
            ShowId = show.Id,
            Show = show,
            Path = show.Path + "/Season " + seasonNumber
        };
        return new Episode
        {
            Id = id,
            SonarrId = id,
            EpisodeNumber = episodeNumber,
            Title = "Episode " + episodeNumber,
            FileName = showTitle + " - S" + seasonNumber.ToString("00") + "E" + episodeNumber.ToString("00"),
            Path = season.Path,
            SeasonId = season.Id,
            Season = season
        };
    }
}
