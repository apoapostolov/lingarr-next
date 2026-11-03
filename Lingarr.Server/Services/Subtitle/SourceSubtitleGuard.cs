using System.Text.RegularExpressions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Guards the two ways an unusable translation used to reach the library: a source file that is a
/// converted typesetting track (ASS override blocks and vector drawing commands in a .srt), and a
/// translation that failed because the source file held another language than its name promised.
/// Both checks are language-agnostic, so they work for every source language pair.
/// </summary>
public partial class SourceSubtitleGuard : ISourceSubtitleGuard
{
    /// <summary>Share of lines that carry formatting or a drawing instead of speech.</summary>
    public const double NonDialogueLineRatio = 0.15;

    /// <summary>
    /// Share of lines that begin at the same moment as another line. A subtitle holds one line per
    /// moment; a converted ASS track turns every style and position into its own line.
    /// </summary>
    public const double StackedCueRatio = 0.50;

    /// <summary>Smaller files are not judged, so a short clip is never refused.</summary>
    public const int MinimumLinesToJudge = 20;

    /// <summary>Share of lines that come back identical to their source before the file is refused.</summary>
    public const double UnchangedLineRatio = 0.40;

    /// <summary>Shorter candidates say nothing about the language, so the ratio needs enough of them.</summary>
    public const int MinimumLinesForUnchangedCheck = 50;

    /// <summary>Lines shorter than this are names, numbers and interjections; a copy proves nothing.</summary>
    private const int UnchangedCandidateLength = 8;

    public SubtitleSourceVerdict InspectSource(IReadOnlyList<SubtitleItem> subtitles)
    {
        var counts = Measure(subtitles);
        if (counts.Lines < MinimumLinesToJudge)
        {
            return SubtitleSourceVerdict.Allowed;
        }

        // A drawing or a font wrapper on its own: signs, karaoke and typeset effects, no dialogue.
        var nonDialogue = (double)counts.NonDialogueLines / counts.Lines;
        if (nonDialogue >= NonDialogueLineRatio)
        {
            return SubtitleSourceVerdict.Refuse(
                $"The source subtitle looks like a typesetting or drawing track, not dialogue: " +
                $"{counts.NonDialogueLines} of {counts.Lines} lines carry a drawing or formatting instead of speech " +
                $"({nonDialogue:P0}). Lingarr did not translate it. Add a dialogue subtitle for this media, " +
                "or turn off the source subtitle guard to translate it anyway.");
        }

        // One cue per moment is what a subtitle is. A converted event dump stacks several lines on
        // the same start time, one per style or position. Styling alone is never evidence, and
        // repeated dialogue that keeps its own timings is left alone.
        var stacked = (double)counts.StackedLines / counts.Lines;
        if (stacked >= StackedCueRatio)
        {
            return SubtitleSourceVerdict.Refuse(
                $"The source subtitle looks like a converted typesetting track: {counts.StackedLines} of " +
                $"{counts.Lines} lines start at the same moment as another line ({stacked:P0}). " +
                "Lingarr did not translate it. Add a dialogue subtitle for this media, or turn off the " +
                "source subtitle guard to translate it anyway.");
        }

        return SubtitleSourceVerdict.Allowed;
    }

    public SubtitleSourceVerdict InspectTranslation(
        IReadOnlyList<SubtitleItem> subtitles,
        string sourceLanguage)
    {
        var candidates = 0;
        var unchanged = 0;
        foreach (var subtitle in subtitles)
        {
            var source = string.Join(" ", subtitle.PlaintextLines ?? subtitle.Lines ?? []);
            var target = string.Join(" ", subtitle.TranslatedLines ?? []);
            var sourcePlain = source.Trim();
            if (sourcePlain.Length < UnchangedCandidateLength || !LetterRegex().IsMatch(sourcePlain))
            {
                continue;
            }

            // Sound cues and credits are left untranslated on purpose by the non-dialogue
            // classifier, so a copy there is expected and says nothing about the language.
            if (CueRegex().IsMatch(sourcePlain))
            {
                continue;
            }

            candidates++;
            if (Normalized(sourcePlain) == Normalized(target))
            {
                unchanged++;
            }
        }

        if (candidates < MinimumLinesForUnchangedCheck)
        {
            return SubtitleSourceVerdict.Allowed;
        }

        var ratio = (double)unchanged / candidates;
        if (ratio < UnchangedLineRatio)
        {
            return SubtitleSourceVerdict.Allowed;
        }

        return SubtitleSourceVerdict.Refuse(
            $"The translation came back unchanged on {unchanged} of {candidates} usable lines ({ratio:P0}), " +
            $"so this subtitle is probably not written in {sourceLanguage}. " +
            "The translated file was not written. Check the source subtitle's language, or turn off the " +
            "source subtitle guard to translate it anyway.");
    }

    /// <summary>
    /// Collects the file-level evidence the checks use: how many lines hold no readable dialogue,
    /// and how many share a start time with another line. Text is read from the raw lines, because
    /// a parser can already have cleaned the plaintext.
    /// </summary>
    private static FileMeasurements Measure(IReadOnlyList<SubtitleItem> subtitles)
    {
        var lines = 0;
        var nonDialogue = 0;
        var starts = new Dictionary<int, int>();

        foreach (var subtitle in subtitles)
        {
            var raw = subtitle.Lines is { Count: > 0 } ? subtitle.Lines : subtitle.PlaintextLines;
            var line = string.Join(" ", raw ?? []).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            lines++;
            starts[subtitle.StartTime] = starts.TryGetValue(subtitle.StartTime, out var count) ? count + 1 : 1;

            var text = AssOverrideRegex().Replace(HtmlTagRegex().Replace(line, string.Empty), string.Empty).Trim();
            if (text.Length == 0 || LooksLikeDrawing(text))
            {
                nonDialogue++;
            }
        }

        var stacked = starts.Values.Where(count => count > 1).Sum(count => count);
        return new FileMeasurements(lines, nonDialogue, stacked);
    }

    private readonly record struct FileMeasurements(
        int Lines,
        int NonDialogueLines,
        int StackedLines);

    private static bool LooksLikeDrawing(string value)
    {
        var numbers = NumberRegex().Matches(value).Count;
        var letters = LetterRegex().Matches(value).Count;
        return numbers >= 3 && letters <= 4;
    }

    private static string Normalized(string value) =>
        WhitespaceRegex().Replace(value.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\{\\[^}]*\}", RegexOptions.Compiled)]
    private static partial Regex AssOverrideRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\p{L}", RegexOptions.Compiled)]
    private static partial Regex LetterRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    /// <summary>A whole line inside brackets or parentheses, such as "[door slams]" or "(sighs)".</summary>
    [GeneratedRegex(@"^[\[\(].*[\]\)]$", RegexOptions.Compiled)]
    private static partial Regex CueRegex();
}
