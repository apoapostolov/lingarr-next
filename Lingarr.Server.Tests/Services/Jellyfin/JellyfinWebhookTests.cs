using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Integration.Jellyfin;
using Lingarr.Server.Services.Integration.Plex;
using Xunit;

namespace Lingarr.Server.Tests.Services.Jellyfin;

public class JellyfinWebhookTests
{
    [Fact]
    public void Read_AcceptsAnAddedMovieFromTheWebhookPlugin()
    {
        var decision = JellyfinWebhookReader.Read("""
            {
              "NotificationType": "ItemAdded",
              "Name": "Ready Player One",
              "ItemType": "Movie",
              "Year": 2018,
              "ItemId": "abc",
              "Provider_tmdb": "333339",
              "Provider_imdb": "tt1677720",
              "Provider_tvdb": ""
            }
            """);

        var added = Assert.IsType<PlexWebhookDecision.Added>(decision);
        Assert.Equal("movie", added.Item.Kind);
        Assert.Equal("Ready Player One", added.Item.Title);
        Assert.Equal(2018, added.Item.Year);
        Assert.Equal("abc", added.Item.RatingKey);
        Assert.Null(added.Item.SeasonNumber);
        Assert.Contains("tmdb://333339", added.Item.Guids);
        Assert.Equal(["{tmdb-333339}", "{imdb-tt1677720}"], PlexMovieTags.FromGuids(added.Item.Guids));
    }

    [Fact]
    public void Read_AcceptsAnAddedEpisodeWithSeriesIds()
    {
        var decision = JellyfinWebhookReader.Read("""
            {
              "Event": "item.added",
              "Item": {
                "Name": "The Fiery Cross",
                "Type": "Episode",
                "SeriesName": "Outlander",
                "ParentIndexNumber": 1,
                "IndexNumber": 2,
                "Id": "episode-1",
                "ProviderIds": { "Tmdb": "99" },
                "Series": { "ProviderIds": { "Tvdb": "429874", "Imdb": "tt3006802" } }
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
    public void Read_IgnoresPlaybackAndOtherItemTypes()
    {
        Assert.IsType<PlexWebhookDecision.Ignored>(JellyfinWebhookReader.Read("""
            {"NotificationType":"PlaybackStart","ItemType":"Movie","Name":"Example"}
            """));
        Assert.IsType<PlexWebhookDecision.Ignored>(JellyfinWebhookReader.Read("""
            {"NotificationType":"ItemAdded","ItemType":"Series","Name":"Outlander"}
            """));
    }

    [Fact]
    public void Read_RejectsTextThatIsNotJson()
    {
        Assert.IsType<PlexWebhookDecision.Unreadable>(JellyfinWebhookReader.Read("not-json"));
    }
}
