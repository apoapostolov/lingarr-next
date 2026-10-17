using System.Linq;
using Lingarr.Server.Services.SubtitleDownload;
using Xunit;

namespace Lingarr.Server.Tests.Services.SubtitleDownload;

public class SubtitleDownloadChainTests
{
    [Fact]
    public void Parse_Empty_ReturnsNoProviders()
    {
        Assert.Empty(SubtitleDownloadChain.Parse(null));
        Assert.Empty(SubtitleDownloadChain.Parse(""));
        Assert.Empty(SubtitleDownloadChain.Parse("[]"));
    }

    [Fact]
    public void Parse_PlainProviderId_ReturnsSingleEntry()
    {
        var chain = SubtitleDownloadChain.Parse("opensubtitlescom");
        Assert.Single(chain);
        Assert.Equal("opensubtitlescom", chain[0].Provider);
    }

    [Fact]
    public void Parse_RichObjects_PreservesOrder()
    {
        var chain = SubtitleDownloadChain.Parse(
            """[{"id":"a","provider":"subdl"},{"id":"b","provider":"gestdown"}]""");
        Assert.Equal(2, chain.Count);
        Assert.Equal("subdl", chain[0].Provider);
        Assert.Equal("gestdown", chain[1].Provider);
        Assert.Equal("a", chain[0].Id);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var raw = SubtitleDownloadChain.Serialize(
        [
            new SubtitleDownloadChainEntry { Id = "one", Provider = "assrt" }
        ]);
        var parsed = SubtitleDownloadChain.Parse(raw);
        Assert.Single(parsed);
        Assert.Equal("assrt", parsed[0].Provider);
        Assert.Equal("one", parsed[0].Id);
    }
}

public class SubtitleDownloadCatalogTests
{
    [Fact]
    public void Catalog_IncludesBazarrProviders()
    {
        Assert.Equal(47, SubtitleDownloadCatalog.All.Count);
        Assert.Equal(
            SubtitleDownloadCatalog.All.Count,
            SubtitleDownloadCatalog.All.Select(provider => provider.Id).Distinct().Count());
        Assert.NotNull(SubtitleDownloadCatalog.Find("opensubtitlescom"));
        Assert.NotNull(SubtitleDownloadCatalog.Find("gestdown"));
        Assert.Null(SubtitleDownloadCatalog.Find("addic7ed"));
        Assert.Null(SubtitleDownloadCatalog.Find("bsplayer"));
        Assert.Null(SubtitleDownloadCatalog.Find("whisperai"));
        Assert.Null(SubtitleDownloadCatalog.Find("embeddedsubtitles"));
        Assert.Contains(
            SubtitleDownloadCatalog.Find("opensubtitlescom")!.Fields,
            field => field.Key == "api_key");
        Assert.Equal("Gestdown (Addic7ed proxy)", SubtitleDownloadCatalog.Find("gestdown")!.DisplayName);
    }
}
