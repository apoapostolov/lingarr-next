using System.Collections.Generic;
using System.IO;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class EmbeddedSubtitleExtractorTests
{
    [Fact]
    public void SelectTextTrack_PicksBulgarianDialogue()
    {
        var streams = new List<EmbeddedSubtitleStream>
        {
            new(2, "subrip", "eng", "English"),
            new(3, "ass", "bul", "Signs"),
            new(4, "ass", "bul", "Dialogue")
        };

        var plan = EmbeddedSubtitleExtractor.SelectTextTrack(streams, "bg", "Movie");

        Assert.NotNull(plan);
        Assert.Equal(4, plan!.StreamIndex);
        Assert.Equal("Movie.bg.srt", plan.DestinationFileName);
    }

    [Fact]
    public void SelectTextTracks_PlansSourceAndTarget()
    {
        var streams = new List<EmbeddedSubtitleStream>
        {
            new(2, "subrip", "eng", "English"),
            new(3, "subrip", "bul", "Bulgarian")
        };

        var plans = EmbeddedSubtitleExtractor.SelectTextTracks(streams, ["en", "bg"], "Movie");

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, plan => plan.DestinationFileName == "Movie.en.srt" && plan.StreamIndex == 2);
        Assert.Contains(plans, plan => plan.DestinationFileName == "Movie.bg.srt" && plan.StreamIndex == 3);
    }

    [Fact]
    public void SelectTextTrack_SkipsEnglishWhenWantedIsBulgarian()
    {
        var streams = new List<EmbeddedSubtitleStream>
        {
            new(2, "subrip", "eng", "English")
        };

        Assert.Null(EmbeddedSubtitleExtractor.SelectTextTrack(streams, "bg", "Movie"));
    }

    [Fact]
    public void SelectEnglishTextTrack_PrefersDialogueOverSigns()
    {
        var streams = new List<EmbeddedSubtitleStream>
        {
            new(3, "ass", "eng", "Signs"),
            new(4, "ass", "eng", "Dialogue")
        };

        var plan = EmbeddedSubtitleExtractor.SelectEnglishTextTrack(streams, "BLUE LOCK - S01E14");

        Assert.NotNull(plan);
        Assert.Equal(4, plan!.StreamIndex);
        Assert.Equal("BLUE LOCK - S01E14.en.srt", plan.DestinationFileName);
    }

    [Fact]
    public void SelectEnglishTextTrack_PrefersNonSdhWhenAvailable()
    {
        var streams = new List<EmbeddedSubtitleStream>
        {
            new(2, "subrip", "eng", "SDH"),
            new(3, "subrip", "eng", "English")
        };

        var plan = EmbeddedSubtitleExtractor.SelectEnglishTextTrack(streams, "Show - S01E01");

        Assert.Equal(3, plan!.StreamIndex);
    }

    [Fact]
    public void SelectEnglishTextTrack_IgnoresImageOnly()
    {
        var streams = new List<EmbeddedSubtitleStream>
        {
            new(2, "hdmv_pgs_subtitle", "eng", null)
        };

        Assert.Null(EmbeddedSubtitleExtractor.SelectEnglishTextTrack(streams, "Avatar"));
        Assert.True(EmbeddedSubtitleExtractor.HasOnlyImageSubtitles(streams));
        var plan = EmbeddedSubtitleExtractor.SelectEnglishSourceTrack(streams, "Avatar");
        Assert.Equal(EmbeddedSubtitleKind.Image, plan!.Kind);
        Assert.Equal("Avatar.en.ocr.srt", plan.DestinationFileName);
    }

    [Fact]
    public void SelectEnglishSourceTrack_PrefersTextOverPgs()
    {
        var streams = new List<EmbeddedSubtitleStream>
        {
            new(2, "hdmv_pgs_subtitle", "eng", null),
            new(3, "subrip", "eng", "English")
        };

        var plan = EmbeddedSubtitleExtractor.SelectEnglishSourceTrack(streams, "Movie");

        Assert.Equal(EmbeddedSubtitleKind.Text, plan!.Kind);
        Assert.Equal(3, plan.StreamIndex);
    }

    [Fact]
    public void SelectEnglishSourceTrack_SkipsPgsWhenThatFormatIsOff()
    {
        var streams = new List<EmbeddedSubtitleStream>
        {
            new(2, "hdmv_pgs_subtitle", "eng", null),
            new(3, "dvd_subtitle", "eng", null)
        };
        var policy = NonTextSubtitlePolicy.All with { Pgs = false };

        var plan = EmbeddedSubtitleExtractor.SelectEnglishSourceTrack(streams, "Movie", policy);

        Assert.Equal(EmbeddedSubtitleKind.Image, plan!.Kind);
        Assert.Equal(3, plan.StreamIndex);
    }

    [Fact]
    public void SelectEnglishSourceTrack_SkipsCaptionsWhenTheToolIsOff()
    {
        var streams = new List<EmbeddedSubtitleStream>
        {
            new(1, "eia_608", null, null)
        };
        var policy = NonTextSubtitlePolicy.All with { CaptionExtract = false };

        Assert.Null(EmbeddedSubtitleExtractor.SelectEnglishSourceTrack(streams, "Broadcast", policy));
    }

    [Fact]
    public void SelectEnglishSourceTrack_UsesCaptionsWhenThereIsNoTextOrPicture()
    {
        var streams = new List<EmbeddedSubtitleStream>
        {
            new(1, "eia_608", null, null)
        };

        var plan = EmbeddedSubtitleExtractor.SelectEnglishSourceTrack(streams, "Broadcast");

        Assert.Equal(EmbeddedSubtitleKind.Caption, plan!.Kind);
        Assert.Equal("Broadcast.en.srt", plan.DestinationFileName);
    }

    [Fact]
    public void BuildImageCopyCommand_ForcesMatroskaForTheMksExtension()
    {
        var command = EmbeddedSubtitleExtractor.BuildImageCopyCommand("/media/movie.mkv", 2, "/tmp/track.mks");

        Assert.Equal("ffmpeg", command.FileName);
        Assert.Contains("-f", command.ArgumentList);
        Assert.Contains("matroska", command.ArgumentList);
    }

    [Fact]
    public void BuildSeconvCommand_OcrsTheCopiedTrack()
    {
        var command = EmbeddedSubtitleExtractor.BuildSeconvCommand("/tmp/track.mks", "/media/movie", "Movie.en.srt");

        Assert.Equal("seconv", command.FileName);
        Assert.Contains("--ocr-engine:tesseract", command.ArgumentList);
        Assert.Contains("--ocr-language:eng", command.ArgumentList);
        Assert.Contains("--output-filename:Movie.en.srt", command.ArgumentList);
    }

    [Fact]
    public void ParseProbeJson_ReadsStreams()
    {
        const string json = """
            {"streams":[{"index":2,"codec_name":"subrip","tags":{"language":"eng","title":"English"}}]}
            """;

        var streams = EmbeddedSubtitleExtractor.ParseProbeJson(json);

        Assert.Single(streams);
        Assert.Equal(2, streams[0].Index);
        Assert.Equal("eng", streams[0].Language);
    }

    [Fact]
    public void CanReuseSidecar_RejectsAnEmptyFile()
    {
        var folder = Directory.CreateTempSubdirectory("lingarr-empty-extract");
        try
        {
            var empty = Path.Combine(folder.FullName, "Movie.en.srt");
            File.WriteAllBytes(empty, []);

            Assert.False(EmbeddedSubtitleExtractor.CanReuseSidecar(empty, "en"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void CanReuseSidecar_AcceptsARealSubtitle()
    {
        var folder = Directory.CreateTempSubdirectory("lingarr-real-extract");
        try
        {
            var path = Path.Combine(folder.FullName, "Movie.en.srt");
            File.WriteAllText(path, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

            Assert.True(EmbeddedSubtitleExtractor.CanReuseSidecar(path, "en"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void AcceptExtractedSidecar_DeletesAnEmptyResult()
    {
        var folder = Directory.CreateTempSubdirectory("lingarr-empty-extract-result");
        try
        {
            var path = Path.Combine(folder.FullName, "Movie.en.srt");
            File.WriteAllBytes(path, []);

            Assert.False(EmbeddedSubtitleExtractor.AcceptExtractedSidecar(path, "en"));
            Assert.False(File.Exists(path));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
