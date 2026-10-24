using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Integrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class SyncMovieJobUnmonitoredTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _dbContext;

    public SyncMovieJobUnmonitoredTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new LingarrDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Execute_WhenExcludeOn_SyncsMonitoredMoviesAndRemovesAgainstEveryRadarrId()
    {
        var movies = new List<RadarrMovie> { Movie(1, true), Movie(2, false) };
        var radarr = new Mock<IRadarrService>();
        radarr.Setup(service => service.GetMovies()).ReturnsAsync(movies);
        var movieSync = new Mock<IMovieSyncService>();
        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(SettingKeys.Integration.RadarrDefaultInclude))
            .ReturnsAsync("true");
        settings.Setup(service => service.GetSetting(SettingKeys.Integration.RadarrExcludeUnmonitored))
            .ReturnsAsync("true");
        var schedule = new Mock<IScheduleService>();
        var job = new SyncMovieJob(
            radarr.Object,
            NullLogger<SyncMovieJob>.Instance,
            schedule.Object,
            movieSync.Object,
            settings.Object,
            _dbContext);

        await job.Execute();

        movieSync.Verify(
            service => service.SyncMovies(
                It.Is<List<RadarrMovie>>(list => list.Count == 1 && list[0].Id == 1),
                true),
            Times.Once);
        movieSync.Verify(
            service => service.RemoveNonExistentMovies(
                It.Is<IEnumerable<int>>(ids => ids.OrderBy(id => id).SequenceEqual(new[] { 1, 2 }))),
            Times.Once);
    }

    private static RadarrMovie Movie(int id, bool monitored) => new()
    {
        Id = id,
        Title = $"Movie {id}",
        Path = $"/movies/{id}",
        RootFolderPath = "/movies",
        Added = "2026-01-01",
        HasFile = true,
        Monitored = monitored
    };
}
