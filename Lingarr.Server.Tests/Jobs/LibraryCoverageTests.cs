using System;
using System.Collections.Generic;
using System.IO;
using Lingarr.Server.Services;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class LibraryCoverageTests
{
    private static readonly HashSet<string> Sources = new(StringComparer.OrdinalIgnoreCase) { "en" };
    private static readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase) { "bg" };

    [Fact]
    public void HasFullPair_AcceptsEnglishAndBulgarian()
    {
        Assert.True(LibraryCoverage.HasFullPair("bg,en", null, Sources, Targets));
        Assert.True(LibraryCoverage.HasFullPair(null, ["en", "bg"], Sources, Targets));
    }

    [Fact]
    public void HasFullPair_RejectsSourceWithoutTheTarget()
    {
        Assert.False(LibraryCoverage.HasFullPair("en", null, Sources, Targets));
    }

    [Fact]
    public void CheckedWithoutSource_SkipsAFolderThatHadNoSourceSubtitle()
    {
        Assert.True(LibraryCoverage.CheckedWithoutSource("", Sources));
        Assert.False(LibraryCoverage.CheckedWithoutSource(null, Sources));
        Assert.False(LibraryCoverage.CheckedWithoutSource("en", Sources));
    }

    [Fact]
    public void MatchesCurrentFile_AcceptsASidecarForTheCurrentStem()
    {
        Assert.True(LibraryCoverage.MatchesCurrentFile(
            @"/media/tv/Show/Show - S03E01 WEBDL-720p.en.srt",
            "Show - S03E01 WEBDL-720p"));
        Assert.True(LibraryCoverage.MatchesCurrentFile(
            "Show - S03E01 WEBDL-720p.bg.srt",
            "Show - S03E01 WEBDL-720p"));
    }

    [Fact]
    public void MatchesCurrentFile_RejectsASidecarFromAReplacedFile()
    {
        Assert.False(LibraryCoverage.MatchesCurrentFile(
            @"/media/tv/Show/Show - S03E01 WEBDL-1080p.en.srt",
            "Show - S03E01 WEBDL-720p"));
        Assert.False(LibraryCoverage.MatchesCurrentFile(
            "Show - S03E01 WEBDL-1080p.en.srt",
            null));
        Assert.False(LibraryCoverage.MatchesCurrentFile(null, "Show - S03E01 WEBDL-720p"));
    }

    [Fact]
    public void HasUsablePair_RejectsAnEmptyTargetSidecar()
    {
        var folder = Directory.CreateTempSubdirectory("lingarr-empty-bg");
        try
        {
            File.WriteAllText(
                Path.Combine(folder.FullName, "Movie.en.srt"),
                "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
            File.WriteAllBytes(Path.Combine(folder.FullName, "Movie.bg.srt"), []);

            Assert.False(LibraryCoverage.HasUsablePair(folder.FullName, "Movie", Sources, Targets));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void HasUsablePair_AcceptsRealEnglishAndBulgarianSidecars()
    {
        var folder = Directory.CreateTempSubdirectory("lingarr-real-pair");
        try
        {
            File.WriteAllText(
                Path.Combine(folder.FullName, "Movie.en.srt"),
                "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
            File.WriteAllText(
                Path.Combine(folder.FullName, "Movie.bg.srt"),
                "1\n00:00:01,000 --> 00:00:02,000\nЗдравей\n");

            Assert.True(LibraryCoverage.HasUsablePair(folder.FullName, "Movie", Sources, Targets));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
