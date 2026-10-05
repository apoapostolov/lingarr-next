using Lingarr.Server.Services.Integration.Jellyfin;
using Lingarr.Server.Services.Integration.Plex;
using Xunit;

namespace Lingarr.Server.Tests.Services.Emby;

public class EmbyWebhookTests
{
    [Fact]
    public void Read_AcceptsALibraryNewMovie()
    {
        var decision = JellyfinWebhookReader.Read("""
            {
              "Event": "library.new",
              "Item": {
                "Name": "Ready Player One",
                "Id": "764327",
                "ProductionYear": 2018,
                "Type": "Movie",
                "ProviderIds": { "Imdb": "tt1677720", "Tmdb": "333339" }
              }
            }
            """);

        var added = Assert.IsType<PlexWebhookDecision.Added>(decision);
        Assert.Equal("movie", added.Item.Kind);
        Assert.Equal("Ready Player One", added.Item.Title);
        Assert.Equal(2018, added.Item.Year);
        Assert.Equal("764327", added.Item.RatingKey);
        Assert.Null(added.Item.SeasonNumber);
        Assert.Equal(["{imdb-tt1677720}", "{tmdb-333339}"], PlexMovieTags.FromGuids(added.Item.Guids));
    }

    [Fact]
    public void Read_AcceptsALibraryNewEpisode()
    {
        var decision = JellyfinWebhookReader.Read("""
            {
              "Event": "library.new",
              "Item": {
                "Name": "The Fiery Cross",
                "Type": "Episode",
                "SeriesName": "Outlander",
                "ParentIndexNumber": 1,
                "IndexNumber": 2,
                "Id": "episode-1",
                "ProviderIds": [],
                "SeriesProviderIds": { "Tvdb": "429874", "Imdb": "tt3006802" }
              }
            }
            """);

        var added = Assert.IsType<PlexWebhookDecision.Added>(decision);
        Assert.Equal("episode", added.Item.Kind);
        Assert.Equal("Outlander", added.Item.ShowTitle);
        Assert.Equal(1, added.Item.SeasonNumber);
        Assert.Equal(2, added.Item.EpisodeNumber);
        Assert.Contains("tvdb://429874", added.Item.Guids);
        Assert.Contains("imdb://tt3006802", added.Item.Guids);
    }

    [Fact]
    public void Read_IgnoresPlaybackAndDeletedItems()
    {
        Assert.IsType<PlexWebhookDecision.Ignored>(JellyfinWebhookReader.Read("""
            {"Event":"playback.start","Item":{"Type":"Movie","Name":"Example"}}
            """));
        Assert.IsType<PlexWebhookDecision.Ignored>(JellyfinWebhookReader.Read("""
            {"Event":"library.deleted","Item":{"Type":"Movie","Name":"Example","Id":"1"}}
            """));
    }
}
