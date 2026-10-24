using System;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class MediaServiceUnmonitoredTests : IDisposable
{
    private readonly LingarrDbContext _dbContext;
    private readonly Mock<IRadarrService> _radarr = new();
    private readonly Mock<ISonarrService> _sonarr = new();
    private readonly Mock<IMovieSyncService> _movieSync = new();
    private readonly Mock<IShowSyncService> _showSync = new();
    private readonly Mock<ISettingService> _settings = new();
    private readonly MediaService _mediaService;

    public MediaServiceUnmonitoredTests()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new LingarrDbContext(options);
        _settings.Setup(service => service.GetSetting(SettingKeys.Integration.RadarrDefaultInclude))
            .ReturnsAsync("true");
        _settings.Setup(service => service.GetSetting(SettingKeys.Integration.SonarrDefaultInclude))
            .ReturnsAsync("true");
        _mediaService = new MediaService(
            _dbContext,
            new Mock<ISubtitleService>().Object,
            _sonarr.Object,
            _showSync.Object,
            _radarr.Object,
            _movieSync.Object,
            _settings.Object,
            NullLogger<MediaService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetMovieIdOrSync_WhenExcludeOn_SkipsUnmonitoredMovie()
    {
        _settings.Setup(service => service.GetSetting(SettingKeys.Integration.RadarrExcludeUnmonitored))
            .ReturnsAsync("true");
        _radarr.Setup(service => service.GetMovie(7)).ReturnsAsync(new RadarrMovie
        {
            Id = 7,
            Title = "Unmonitored",
            Path = "/movies/unmonitored",
            RootFolderPath = "/movies",
            Added = "2026-01-01",
            HasFile = true,
            Monitored = false
        });

        var id = await _mediaService.GetMovieIdOrSyncFromRadarrMovieId(7);

        Assert.Equal(0, id);
        _movieSync.Verify(
            service => service.SyncMovie(It.IsAny<RadarrMovie>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task GetEpisodeIdOrSync_WhenExcludeSeriesOn_SkipsUnmonitoredShow()
    {
        _settings.Setup(service => service.GetSetting(SettingKeys.Integration.SonarrExcludeUnmonitoredSeries))
            .ReturnsAsync("true");
        _settings.Setup(service => service.GetSetting(SettingKeys.Integration.SonarrExcludeUnmonitoredEpisodes))
            .ReturnsAsync("false");
        _sonarr.Setup(service => service.GetEpisode(15)).ReturnsAsync(new SonarrEpisode
        {
            Id = 15,
            EpisodeNumber = 1,
            Title = "Pilot",
            SeasonNumber = 1,
            HasFile = true,
            Show = new SonarrShow
            {
                Id = 3,
                Title = "Unmonitored Show",
                Path = "/tv/unmonitored",
                Added = "2026-01-01",
                SeasonFolder = true,
                Monitored = false
            }
        });

        var id = await _mediaService.GetEpisodeIdOrSyncFromSonarrEpisodeId(15);

        Assert.Equal(0, id);
        _showSync.Verify(
            service => service.SyncShow(It.IsAny<SonarrShow>(), It.IsAny<bool>(), It.IsAny<bool>()),
            Times.Never);
    }
}
