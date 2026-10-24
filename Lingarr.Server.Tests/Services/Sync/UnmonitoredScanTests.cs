using System.Collections.Generic;
using System.Linq;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Services.Sync;
using Xunit;

namespace Lingarr.Server.Tests.Services.Sync;

public class UnmonitoredScanTests
{
    [Fact]
    public void MoviesToSync_WhenExcludeOff_KeepsEveryMovie()
    {
        var movies = new List<RadarrMovie> { Movie(1, true), Movie(2, false) };

        var selected = UnmonitoredScan.MoviesToSync(movies, excludeUnmonitored: false);

        Assert.Equal(new[] { 1, 2 }, selected.Select(movie => movie.Id));
    }

    [Fact]
    public void MoviesToSync_WhenExcludeOn_KeepsMonitoredMovies()
    {
        var movies = new List<RadarrMovie> { Movie(1, true), Movie(2, false) };

        var selected = UnmonitoredScan.MoviesToSync(movies, excludeUnmonitored: true);

        Assert.Equal(new[] { 1 }, selected.Select(movie => movie.Id));
    }

    [Fact]
    public void ShowsToSync_WhenExcludeOn_KeepsMonitoredShows()
    {
        var shows = new List<SonarrShow> { Show(1, true), Show(2, false) };

        var selected = UnmonitoredScan.ShowsToSync(shows, excludeUnmonitored: true);

        Assert.Equal(new[] { 1 }, selected.Select(show => show.Id));
    }

    [Fact]
    public void EpisodesToImport_WhenExcludeOn_KeepsMonitoredFiles()
    {
        var episodes = new List<SonarrEpisode>
        {
            Episode(1, monitored: true, hasFile: true),
            Episode(2, monitored: false, hasFile: true),
            Episode(3, monitored: true, hasFile: false)
        };

        var selected = UnmonitoredScan.EpisodesToImport(episodes, excludeUnmonitored: true);

        Assert.Equal(new[] { 1 }, selected.Select(episode => episode.Id));
    }

    [Fact]
    public void EpisodesToImport_WhenExcludeOff_KeepsEveryFile()
    {
        var episodes = new List<SonarrEpisode>
        {
            Episode(1, monitored: true, hasFile: true),
            Episode(2, monitored: false, hasFile: true),
            Episode(3, monitored: true, hasFile: false)
        };

        var selected = UnmonitoredScan.EpisodesToImport(episodes, excludeUnmonitored: false);

        Assert.Equal(new[] { 1, 2 }, selected.Select(episode => episode.Id));
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

    private static SonarrShow Show(int id, bool monitored) => new()
    {
        Id = id,
        Title = $"Show {id}",
        Path = $"/tv/{id}",
        Added = "2026-01-01",
        SeasonFolder = true,
        Monitored = monitored
    };

    private static SonarrEpisode Episode(int id, bool monitored, bool hasFile) => new()
    {
        Id = id,
        EpisodeNumber = id,
        Title = $"Episode {id}",
        SeasonNumber = 1,
        HasFile = hasFile,
        Monitored = monitored
    };
}
