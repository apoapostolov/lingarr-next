using System;
using Lingarr.Core.Entities;
using Lingarr.Server.Services.Sync;
using Xunit;

namespace Lingarr.Server.Tests.Services.Sync;

public class MediaFileIdentityTests
{
    [Fact]
    public void Assign_ClearsCoverageWhenTheFileNameChanges()
    {
        var episode = Episode("Show - S03E01 WEBDL-1080p", "/tv/Show", "en", "hash", "stamp");

        MediaFileIdentity.Assign(episode, "Show - S03E01 WEBDL-720p", "/tv/Show");

        Assert.Equal("Show - S03E01 WEBDL-720p", episode.FileName);
        Assert.Equal("/tv/Show", episode.Path);
        Assert.Null(episode.LanguageCoverage);
        Assert.Null(episode.MediaHash);
        Assert.Null(episode.DiskStamp);
    }

    [Fact]
    public void Assign_KeepsCoverageWhenTheFileNameIsTheSame()
    {
        var movie = Movie("Movie WEBDL-1080p", "/movies/Movie", "bg,en", "hash", "stamp");

        MediaFileIdentity.Assign(movie, "Movie WEBDL-1080p", "/movies/Movie");

        Assert.Equal("bg,en", movie.LanguageCoverage);
        Assert.Equal("hash", movie.MediaHash);
        Assert.Equal("stamp", movie.DiskStamp);
    }

    [Fact]
    public void Assign_ClearsCoverageWhenTheDirectoryChanges()
    {
        var movie = Movie("Movie WEBDL-1080p", "/movies/Old", "en", "hash", "stamp");

        MediaFileIdentity.Assign(movie, "Movie WEBDL-1080p", "/movies/New");

        Assert.Equal("/movies/New", movie.Path);
        Assert.Null(movie.LanguageCoverage);
        Assert.Null(movie.MediaHash);
        Assert.Null(movie.DiskStamp);
    }

    private static Episode Episode(string fileName, string path, string coverage, string hash, string stamp) => new()
    {
        SonarrId = 1,
        EpisodeNumber = 1,
        Title = "Episode",
        FileName = fileName,
        Path = path,
        LanguageCoverage = coverage,
        MediaHash = hash,
        DiskStamp = stamp,
        Season = new Season { SeasonNumber = 1, Show = new Show { SonarrId = 1, Title = "Show", Path = path, DateAdded = DateTime.UtcNow } }
    };

    private static Movie Movie(string fileName, string path, string coverage, string hash, string stamp) => new()
    {
        RadarrId = 1,
        Title = "Movie",
        FileName = fileName,
        Path = path,
        LanguageCoverage = coverage,
        MediaHash = hash,
        DiskStamp = stamp,
        DateAdded = DateTime.UtcNow
    };
}
