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
    public const double NonDialogueLineRatio = 0.25;

    /// <summary>Share of lines carrying an ASS override block, paired with <see cref="RepeatedLineRatio"/>.</summary>
    public const double OverrideLineRatio = 0.25;

    /// <summary>Share of lines whose text repeats an earlier line, as in a signs track.</summary>
    public const double RepeatedLineRatio = 0.30;

    /// <summary>Smaller files are not judged on markup, so a short clip is never refused.</summary>
    public const int MinimumLinesForMarkupCheck = 20;

    /// <summary>Share of lines that come back identical to their source before the file is refused.</summary>
    public const double UnchangedLineRatio = 0.40;

    /// <summary>Shorter candidates say nothing about the language, so the ratio needs enough of them.</summary>
    public const int MinimumLinesForUnchangedCheck = 50;

    /// <summary>Lines shorter than this are names, numbers and interjections; a copy proves nothing.</summary>
    private const int UnchangedCandidateLength = 8;

    public SubtitleSourceVerdict InspectSource(IReadOnlyList<SubtitleItem> subtitles)
    {
        var lines = TextLines(subtitles);
        if (lines.Count < MinimumLinesForMarkupCheck)
        {
            return SubtitleSourceVerdict.Allowed;
        }

        var counts = Measure(lines);
        var total = lines.Count;

        // A drawing or a font wrapper on its own: signs, karaoke and typeset effects, no dialogue.
        var nonDialogue = (double)counts.NonDialogueLines / total;
        if (nonDialogue >= NonDialogueLineRatio)
        {
            return SubtitleSourceVerdict.Refuse(
                $"The source subtitle looks like a typesetting or drawing track, not dialogue: " +
                $"{counts.NonDialogueLines} of {total} lines carry a drawing or formatting instead of speech " +
                $"({nonDialogue:P0}). Lingarr did not translate it. Add a dialogue subtitle for this media, " +
                "or turn off the source subtitle guard to translate it anyway.");
        }

        // A signs track repeats the same on-screen text over and over with per-line styling.
        // Styling alone is not evidence: dialogue wrapped in font tags or ASS overrides is
        // ordinary dialogue, which is why only repetition decides this one.
        var overrideLines = (double)counts.OverrideLines / total;
        var repeatedLines = (double)counts.RepeatedLines / total;
        if (overrideLines >= OverrideLineRatio && repeatedLines >= RepeatedLineRatio)
        {
            return SubtitleSourceVerdict.Refuse(
                $"The source subtitle repeats itself instead of holding dialogue: {counts.RepeatedLines} of {total} " +
                $"lines repeat an earlier line ({repeatedLines:P0}) and {counts.OverrideLines} lines carry ASS styling. " +
                "Lingarr did not translate it. Add a dialogue subtitle for this media, or turn off the source " +
                "subtitle guard to translate it anyway.");
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

    private static List<string> TextLines(IReadOnlyList<SubtitleItem> subtitles)
    {
        var lines = new List<string>(subtitles.Count);
        foreach (var subtitle in subtitles)
        {
            // Raw lines first: a parser can already have removed the override blocks from the
            // plaintext, and those blocks are the evidence this check looks for.
            var raw = subtitle.Lines is { Count: > 0 } ? subtitle.Lines : subtitle.PlaintextLines;
            var text = string.Join(" ", raw ?? []).Trim();
            if (text.Length > 0)
            {
                lines.Add(text);
            }
        }

        return lines;
    }

    /// <summary>
    /// Collects the file-level evidence the checks use. A line counts as non-dialogue only when
    /// nothing readable is left once its markup is removed, so styled dialogue stays dialogue.
    /// </summary>
    private static FileMeasurements Measure(IReadOnlyList<string> lines)
    {
        var nonDialogue = 0;
        var overrideLines = 0;
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            if (AssOverrideRegex().IsMatch(line))
            {
                overrideLines++;
            }

            var text = AssOverrideRegex().Replace(HtmlTagRegex().Replace(line, string.Empty), string.Empty).Trim();
            if (text.Length == 0 || LooksLikeDrawing(text))
            {
                nonDialogue++;
                continue;
            }

            var key = Normalized(text);
            occurrences[key] = occurrences.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        // A phrase repeated twice contributes one line that repeats an earlier line, not two.
        var repeated = occurrences.Values.Sum(count => count - 1);
        return new FileMeasurements(nonDialogue, overrideLines, repeated);
    }

    private readonly record struct FileMeasurements(
        int NonDialogueLines,
        int OverrideLines,
        int RepeatedLines);

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
