using System.Collections.Generic;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class EmbeddedSubtitleExtractorTests
{
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
}
