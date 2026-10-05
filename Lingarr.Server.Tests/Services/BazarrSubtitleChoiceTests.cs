using System.Collections.Generic;
using Lingarr.Server.Services.Integration.Bazarr;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class BazarrSubtitleChoiceTests
{
    private static readonly HashSet<string> English = new() { "en" };

    [Fact]
    public void PickBest_UsesTheHighestScoredSourceLanguage()
    {
        var best = BazarrSubtitleChoice.PickBest(
        [
            Hit("bg", 99, "other", "1"),
            Hit("en", 81, "low", "2"),
            Hit("en", 94, "best", "3"),
            Hit("en", 96, "forced-one", "4", forced: true)
        ],
            English,
            70);

        Assert.NotNull(best);
        Assert.Equal("3", best!.SubtitleId);
        Assert.Equal(94, best.Score);
    }

    [Fact]
    public void PickBest_SkipsAScoreBelowTheMinimum()
    {
        var best = BazarrSubtitleChoice.PickBest(
            [Hit("en", 60, "weak", "9")],
            English,
            70);

        Assert.Null(best);
    }

    [Fact]
    public void PickBest_PrefersADialogueSubtitleWhenScoresTie()
    {
        var best = BazarrSubtitleChoice.PickBest(
        [
            Hit("en", 90, "hi", "1", hearingImpaired: true),
            Hit("en", 90, "dialogue", "2")
        ],
            English,
            70);

        Assert.Equal("2", best!.SubtitleId);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ExtractFirst_DefaultsOn(string? value, bool extractFirst)
    {
        Assert.Equal(extractFirst, BazarrSourceOrder.ExtractFirst(value));
        Assert.True(BazarrSourceOrder.SourceIncludesEnglish(new HashSet<string> { "en" }));
        Assert.False(BazarrSourceOrder.SourceIncludesEnglish(new HashSet<string> { "bg" }));
    }

    [Fact]
    public void LanguageMatches_AcceptsEnglishVariants()
    {
        Assert.True(BazarrSubtitleChoice.LanguageMatches("en", English));
        Assert.True(BazarrSubtitleChoice.LanguageMatches("eng", English));
        Assert.False(BazarrSubtitleChoice.LanguageMatches("bg", English));
    }

    private static BazarrCandidate Hit(
        string language,
        int score,
        string provider,
        string id,
        bool forced = false,
        bool hearingImpaired = false) =>
        new(language, score, forced, hearingImpaired, provider, id, true);
}
