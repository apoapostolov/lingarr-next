using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleNamingTests
{
    [Theory]
    [InlineData("eng", "en")]
    [InlineData("bul", "bg")]
    [InlineData("EN", "en")]
    [InlineData("bulgarian", "bg")]
    [InlineData("de", null)]
    public void NormalizeLanguage_MapsKnownAliases(string input, string? expected)
    {
        Assert.Equal(expected, SubtitleNaming.NormalizeLanguage(input));
    }

    [Fact]
    public void BuildDestinationFileName_TagsOcrSubtitles()
    {
        Assert.Equal(
            "Movie.en.ocr.srt",
            SubtitleNaming.BuildDestinationFileName("Movie", "en", SubtitleCaption.Ocr, ".srt"));
        Assert.Equal(SubtitleCaption.Ocr, SubtitleNaming.ParseSidecar("Movie.en.ocr.srt").Caption);
        Assert.Equal("en", SubtitleNaming.ParseSidecar("Movie.en.ocr.srt").Language);
    }

    [Fact]
    public void ExtractEpisodeKey_ReturnsSingleEpisode()
    {
        Assert.Equal("S02E03", SubtitleNaming.ExtractEpisodeKey("Show - S02E03 - Title WEBDL-1080p"));
    }

    [Fact]
    public void ExtractEpisodeKey_RejectsPackedRanges()
    {
        Assert.Null(SubtitleNaming.ExtractEpisodeKey("Death Parade - S01E01-02-03-04-05-06-07-08-09-10-11-12 - Pack"));
    }

    [Fact]
    public void PlanRename_MatchesReleaseNameDrift()
    {
        var plan = SubtitleNaming.PlanRename(
            "/media/media/movies/A Spark Story (2021) {tmdb-852724}",
            "A Spark Story (2021) {tmdb-852724} [WEBDL-1080p][EAC3 5.1][h264]-KOGi",
            ["A Spark Story (2021) WEBRip-1080p.eng.srt"],
            "en");

        Assert.NotNull(plan);
        Assert.Equal("en", plan!.Language);
        Assert.EndsWith(
            "A Spark Story (2021) {tmdb-852724} [WEBDL-1080p][EAC3 5.1][h264]-KOGi.en.srt",
            plan.DestinationPath);
    }

    [Fact]
    public void PlanRename_KeepsCaptionAndNormalizesBulgarian()
    {
        var plan = SubtitleNaming.PlanRename(
            "/data",
            "Arrival (2016) {tmdb-329865} [WEBDL-1080p][EAC3 5.1][h264]-BLOOM",
            ["Arrival (2016) Bluray-1080p.bul.srt"],
            "bg");

        Assert.NotNull(plan);
        Assert.EndsWith(
            "Arrival (2016) {tmdb-329865} [WEBDL-1080p][EAC3 5.1][h264]-BLOOM.bg.srt",
            plan!.DestinationPath);
    }

    [Fact]
    public void PlanRename_DoesNotCrossEpisodes()
    {
        var plan = SubtitleNaming.PlanRename(
            "/data",
            "Show - S02E03 - Title WEBDL-1080p",
            ["Show - S02E04 - Other WEBDL-1080p.en.srt"],
            "en");

        Assert.Null(plan);
    }

    [Fact]
    public void PlanRename_SkipsWhenAlreadyStandard()
    {
        var fileName = "Mad Men - S01E01 - Smoke Gets in Your Eyes Bluray-720p";
        var plan = SubtitleNaming.PlanRename(
            "/data",
            fileName,
            [$"{fileName}.en.srt", $"{fileName}.bg.srt"],
            "en");

        Assert.Null(plan);
    }

    [Fact]
    public void ScoreMatch_AcceptsApostropheDifference()
    {
        var parsed = SubtitleNaming.ParseSidecar("Assassin's Creed (2016) Bluray-1080p.bg.srt");
        var score = SubtitleNaming.ScoreMatch(
            "Assassins Creed (2016) {tmdb-121856} [Bluray-1080p][EAC3 5.1][x264]-halloween",
            parsed);

        Assert.True(score >= SubtitleNaming.MinimumAcceptScore, $"score was {score}");
    }
}
