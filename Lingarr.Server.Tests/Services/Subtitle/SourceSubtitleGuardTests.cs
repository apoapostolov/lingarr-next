using System.Collections.Generic;
using System.Linq;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SourceSubtitleGuardTests
{
    private readonly SourceSubtitleGuard _guard = new();

    [Fact]
    public void InspectSource_ConvertedTypesettingTrack_IsRefused()
    {
        // Shape of the Bedroom failure: a signs track exported to .srt, where override blocks and
        // vector drawing commands became subtitle lines.
        var subtitles = new List<SubtitleItem>();
        for (var index = 0; index < 20; index++)
        {
            subtitles.Add(Line(index, @"{\an5}The Apothecary Diaries"));
            subtitles.Add(Line(index, @"{\an5}m -681.75 -8.06 l -683.02 -7.56 b -684.05 -8.86 -685"));
        }

        var verdict = _guard.InspectSource(subtitles);

        Assert.True(verdict.Blocked);
        Assert.Contains("typesetting", verdict.Reason);
    }

    [Fact]
    public void InspectSource_DialogueTrack_IsAllowed()
    {
        var subtitles = Enumerable.Range(0, 40)
            .Select(index => Line(index, $"Line {index}: we should leave before the rain starts."))
            .ToList();

        var verdict = _guard.InspectSource(subtitles);

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void InspectSource_ShortClipIsNotJudged_IsAllowed()
    {
        var subtitles = Enumerable.Range(0, 10)
            .Select(index => Line(index, @"{\an5}m -681.75 -8.06 l -683.02 -7.56"))
            .ToList();

        var verdict = _guard.InspectSource(subtitles);

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void InspectTranslation_UnchangedCopy_IsRefused()
    {
        // Shape of the Bedroom failure: an ".en.srt" that held Albanian, so the provider returned
        // the source text unchanged.
        var subtitles = Enumerable.Range(0, 60)
            .Select(index => new SubtitleItem
            {
                Position = index + 1,
                Lines = [$"Pas luftes per pushtet, e cila ndodhi pas vdekjes se Stalinit {index}"],
                PlaintextLines = [$"Pas luftes per pushtet, e cila ndodhi pas vdekjes se Stalinit {index}"],
                TranslatedLines = [$"Pas luftes per pushtet, e cila ndodhi pas vdekjes se Stalinit {index}"]
            })
            .ToList();

        var verdict = _guard.InspectTranslation(subtitles, "en");

        Assert.True(verdict.Blocked);
        Assert.Contains("not written in en", verdict.Reason);
    }

    [Fact]
    public void InspectTranslation_RealTranslation_IsAllowed()
    {
        var subtitles = Enumerable.Range(0, 60)
            .Select(index => new SubtitleItem
            {
                Position = index + 1,
                Lines = [$"We should leave before the rain starts tonight {index}"],
                PlaintextLines = [$"We should leave before the rain starts tonight {index}"],
                TranslatedLines = [$"Трябва да тръгнем преди дъжда довечера {index}"]
            })
            .ToList();

        var verdict = _guard.InspectTranslation(subtitles, "en");

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void InspectTranslation_ShortFileIsNotJudged_IsAllowed()
    {
        var subtitles = Enumerable.Range(0, 10)
            .Select(index => new SubtitleItem
            {
                Position = index + 1,
                Lines = [$"Short copy of the source line {index}"],
                PlaintextLines = [$"Short copy of the source line {index}"],
                TranslatedLines = [$"Short copy of the source line {index}"]
            })
            .ToList();

        var verdict = _guard.InspectTranslation(subtitles, "en");

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void InspectSource_InspectsRawLinesWhenPlaintextIsCleaned()
    {
        // A parser that strips override blocks from the plaintext must not hide a typeset track.
        var subtitles = Enumerable.Range(0, 40)
            .Select(index => new SubtitleItem
            {
                Position = index + 1,
                Lines = [@"{\an5}m -681.75 -8.06 l -683.02 -7.56 b -684.05 -8.86 -685"],
                PlaintextLines = ["Clean dialogue that the parser left here"]
            })
            .ToList();

        var verdict = _guard.InspectSource(subtitles);

        Assert.True(verdict.Blocked);
    }

    [Fact]
    public void InspectTranslation_SkippedNonDialogueLines_AreNotCountedAsUnchanged()
    {
        // The classifier leaves sound cues untranslated, which is a copy by design.
        var subtitles = Enumerable.Range(0, 60)
            .Select(index => index % 2 == 0
                ? new SubtitleItem
                {
                    Position = index + 1,
                    Lines = [$"[door slams and footsteps fade out {index}]"],
                    PlaintextLines = [$"[door slams and footsteps fade out {index}]"],
                    TranslatedLines = [$"[door slams and footsteps fade out {index}]"]
                }
                : new SubtitleItem
                {
                    Position = index + 1,
                    Lines = [$"We should leave before the rain starts tonight {index}"],
                    PlaintextLines = [$"We should leave before the rain starts tonight {index}"],
                    TranslatedLines = [$"Трябва да тръгнем преди дъжда довечера {index}"]
                })
            .ToList();

        var verdict = _guard.InspectTranslation(subtitles, "en");

        Assert.False(verdict.Blocked);
    }

    private static SubtitleItem Line(int index, string text) => new()
    {
        Position = index + 1,
        Lines = [text],
        PlaintextLines = [text]
    };
}
