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
            subtitles.Add(Line(index, @"{\an5}Episode title card"));
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

    [Fact]
    public void InspectSource_StyledDialogue_IsAllowed()
    {
        // Real dialogue with an ASS override on every line is not a typesetting track.
        var subtitles = Enumerable.Range(0, 40)
            .Select(index => Line(index, $@"{{\an5}}We should leave before the rain starts {index}"))
            .ToList();

        var verdict = _guard.InspectSource(subtitles);

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void InspectSource_StackedCueTrack_IsRefused()
    {
        // A converted ASS track turns every style into its own line on the same start time.
        var subtitles = new List<SubtitleItem>();
        for (var index = 0; index < 30; index++)
        {
            var card = Line(index * 2, @"{\an5}{\b1}Episode title card");
            card.StartTime = index * 1000;
            card.EndTime = (index * 1000) + 900;
            subtitles.Add(card);

            var dialogue = Line((index * 2) + 1, $"Dialogue line number {index} with actual words in it");
            dialogue.StartTime = index * 1000;
            dialogue.EndTime = (index * 1000) + 900;
            subtitles.Add(dialogue);
        }

        var verdict = _guard.InspectSource(subtitles);

        Assert.True(verdict.Blocked);
        Assert.Contains("same moment", verdict.Reason);
    }

    [Fact]
    public void InspectSource_RepeatedDialogueWithItsOwnTimings_IsAllowed()
    {
        // Chants and rituals repeat real dialogue, but every line keeps its own moment.
        var subtitles = new List<SubtitleItem>();
        var position = 0;
        for (var repeat = 0; repeat < 6; repeat++)
        {
            subtitles.Add(Line(position++, "I wish to save the world."));
            subtitles.Add(Line(position++, "My name is not one of them."));
            subtitles.Add(Line(position++, "I am not one of them."));
            subtitles.Add(Line(position++, "We should leave before the rain starts."));
        }

        for (var index = 0; index < 40; index++)
        {
            subtitles.Add(Line(position++, $"Line {index} carries its own sentence about the harbour"));
        }

        var verdict = _guard.InspectSource(subtitles);

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void InspectSource_StyledDialogueWithHtmlWrappers_IsAllowed()
    {
        // SubtitleHtml.Strip may be off, so a dialogue file can reach the guard with every line
        // wrapped in font tags. Wrappers are styling, not evidence of a typesetting track.
        var subtitles = Enumerable.Range(0, 40)
            .Select(index => Line(index,
                $@"<font face=""Trebuchet MS"" size=""24""><b><font color=""#ffffff"">" +
                $@"We should leave before the rain starts line {index}</font></b></font>"))
            .ToList();

        var verdict = _guard.InspectSource(subtitles);

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void InspectSource_AHandfulOfRepeatsInDialogue_IsAllowed()
    {
        // 100 lines, 15 pairs and 70 unique lines: 15 lines repeat an earlier line, not 30.
        var subtitles = new List<SubtitleItem>();
        var position = 0;
        for (var pair = 0; pair < 15; pair++)
        {
            var text = $@"{{\an5}}Leave before the rain starts, take the back road {pair}";
            subtitles.Add(Line(position++, text));
            subtitles.Add(Line(position++, text));
        }

        for (var index = 0; index < 70; index++)
        {
            subtitles.Add(Line(position++, $@"{{\an5}}Line {index} carries its own sentence about the harbour"));
        }

        var verdict = _guard.InspectSource(subtitles);

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void InspectSource_MarkupHeavyUniqueDialogue_IsAllowed()
    {
        var subtitles = Enumerable.Range(0, 40)
            .Select(index => Line(index,
                $@"{{\an8}}{{\b1}}{{\c&H00FFFFFF&}}The ferry leaves at six, meet me at the pier {index}"))
            .ToList();

        var verdict = _guard.InspectSource(subtitles);

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void InspectSource_StackedHtmlStyledTrack_IsRefused()
    {
        // With HTML stripping off, a signs track arrives wrapped in font tags and carries no ASS
        // override at all. The shared start times identify it.
        var subtitles = Enumerable.Range(0, 40)
            .Select(index =>
            {
                var item = Line(index, @"<font face=""Impact"" size=""20""><b>Episode title card</b></font>");
                item.StartTime = (index / 2) * 1000;
                item.EndTime = ((index / 2) * 1000) + 900;
                return item;
            })
            .ToList();

        var verdict = _guard.InspectSource(subtitles);

        Assert.True(verdict.Blocked);
    }

    private static SubtitleItem Line(int index, string text) => new()
    {
        Position = index + 1,
        StartTime = index * 1000,
        EndTime = (index * 1000) + 900,
        Lines = [text],
        PlaintextLines = [text]
    };
}
