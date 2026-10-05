using System.Collections.Generic;
using Lingarr.Core.Entities;
using Lingarr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class CancelledProgressCacheTests
{
    private readonly TranslationQualityService _quality = new(
        null!,
        NullLogger<TranslationQualityService>.Instance);

    [Fact]
    public void ScorePartial_KeepsACleanFragmentWithAGap()
    {
        var score = _quality.ScorePartial(
        [
            Line(1, 1, "Hello there friend", "Hi there friend"),
            Line(2, 8, "See you soon", "See you later")
        ],
            "en");

        Assert.NotNull(score);
        Assert.True(score >= 90, $"Expected at least 90, got {score}.");
    }

    [Fact]
    public void ScorePartial_UsesTheNewestLineAtAPosition()
    {
        var score = _quality.ScorePartial(
        [
            Line(1, 1, "Hello there friend", ""),
            Line(4, 1, "Hello there friend", "Hi there friend")
        ],
            "en");

        Assert.NotNull(score);
        Assert.True(score >= 90, $"Expected the newer line to score at least 90, got {score}.");
    }

    [Fact]
    public void ScorePartial_RejectsAMostlyEmptyPartial()
    {
        var score = _quality.ScorePartial(
        [
            Line(1, 1, "Hello there friend", ""),
            Line(2, 2, "See you soon", "See you later")
        ],
            "en");

        Assert.NotNull(score);
        Assert.True(score < 90, $"Expected a dirty partial to stay under 90, got {score}.");
    }

    [Theory]
    [InlineData(90, 90, true)]
    [InlineData(89, 90, false)]
    [InlineData(null, 90, false)]
    public void ShouldContinue_UsesTheThreshold(int? score, int threshold, bool expected)
    {
        Assert.Equal(expected, CancelledProgressCache.ShouldContinue(score, threshold));
    }

    [Fact]
    public void SourceStillMatches_AcceptsTheStoredPlainLine()
    {
        Assert.True(CancelledProgressCache.SourceStillMatches(
            "Hello there",
            new[] { "Hello", "there" },
            new[] { "Hello", "there" }));
    }

    [Fact]
    public void SourceStillMatches_RejectsAChangedLine()
    {
        Assert.False(CancelledProgressCache.SourceStillMatches(
            "Hello there",
            new[] { "Goodbye", "there" },
            new[] { "Goodbye", "there" }));
    }

    [Fact]
    public void ProgressPercent_IsTranslatedOverTotal()
    {
        Assert.Equal(40, CancelledProgressCache.ProgressPercent(2, 5));
        Assert.Equal(1, CancelledProgressCache.ProgressPercent(1, 5000));
        Assert.Equal(0, CancelledProgressCache.ProgressPercent(0, 5));
        Assert.Null(CancelledProgressCache.ProgressPercent(2, 0));
    }

    [Fact]
    public void ThresholdOrDefault_IsNinety()
    {
        Assert.Equal(90, CancelledProgressCache.ThresholdOrDefault(null));
        Assert.Equal(90, CancelledProgressCache.ThresholdOrDefault("nope"));
        Assert.Equal(95, CancelledProgressCache.ThresholdOrDefault("95"));
        Assert.True(CancelledProgressCache.IsEnabled(null));
        Assert.True(CancelledProgressCache.IsEnabled("true"));
        Assert.False(CancelledProgressCache.IsEnabled("false"));
    }

    private static TranslationRequestLine Line(int id, int position, string source, string target) =>
        new()
        {
            Id = id,
            Position = position,
            Source = source,
            Target = target,
            TranslationRequestId = 1
        };
}
