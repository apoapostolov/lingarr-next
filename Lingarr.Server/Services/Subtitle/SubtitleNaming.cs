using System.Text.RegularExpressions;

namespace Lingarr.Server.Services.Subtitle;

public enum SubtitleCaption
{
    None,
    Sdh,
    Hi,
    Forced,
    Cc,
    Ocr
}

public sealed record ParsedSubtitleName(
    string Stem,
    string TitleKey,
    string? EpisodeKey,
    int? Year,
    string? Language,
    SubtitleCaption Caption,
    string Extension);

public sealed record SubtitleRenamePlan(
    string SourcePath,
    string DestinationPath,
    string Language,
    SubtitleCaption Caption,
    int Score);

public static class SubtitleNaming
{
    public const int MinimumAcceptScore = 50;

    private static readonly HashSet<string> CaptionTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "sdh", "cc", "forced", "hi", "ocr"
    };

    private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en",
        ["eng"] = "en",
        ["english"] = "en",
        ["bg"] = "bg",
        ["bul"] = "bg",
        ["bulgarian"] = "bg"
    };

    private static readonly HashSet<string> QualityTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "webdl", "web-dl", "webrip", "web", "bluray", "blu-ray", "hdtv", "sdtv", "dvdrip",
        "remux", "proper", "repack", "internal", "extended", "unrated", "directors",
        "720p", "1080p", "2160p", "480p", "576p", "4k", "uhd",
        "x264", "x265", "h264", "h265", "hevc", "avc", "xvid",
        "aac", "ac3", "eac3", "dts", "truehd", "flac", "mp3", "opus",
        "hdr", "sdr", "dv", "hdr10", "10bit", "8bit", "atmos",
        "multi", "dual", "complete", "limited"
    };

    private static readonly Regex EpisodeRegex = new(
        @"s(?<season>\d{1,2})e(?<episode>\d{1,3})(?:-?e?(?<episodeEnd>\d{1,3}))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YearRegex = new(@"\((?<year>19\d{2}|20\d{2})\)", RegexOptions.Compiled);
    private static readonly Regex TmdbRegex = new(@"\{tmdb-\d+\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BracketRegex = new(@"\[[^\]]*\]", RegexOptions.Compiled);
    private static readonly Regex ReleaseGroupRegex = new(@"-[a-z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? NormalizeLanguage(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return LanguageAliases.GetValueOrDefault(token.Trim());
    }

    public static string? ExtractEpisodeKey(string name)
    {
        var match = EpisodeRegex.Match(name);
        if (!match.Success)
        {
            return null;
        }

        var season = int.Parse(match.Groups["season"].Value);
        var episode = int.Parse(match.Groups["episode"].Value);
        if (match.Groups["episodeEnd"].Success && match.Groups["episodeEnd"].Length > 0)
        {
            // Multi-episode packs are not assigned to a single episode.
            return null;
        }

        return $"S{season:00}E{episode:00}";
    }

    public static ParsedSubtitleName ParseSidecar(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var parts = stem.Split('.', StringSplitOptions.RemoveEmptyEntries).Reverse().ToList();
        var caption = SubtitleCaption.None;
        string? language = null;

        foreach (var part in parts.ToList())
        {
            if (TryParseCaption(part, out var parsedCaption))
            {
                caption = parsedCaption;
                parts.Remove(part);
                continue;
            }

            var normalized = NormalizeLanguage(part);
            if (normalized != null)
            {
                language = normalized;
                parts.Remove(part);
                break;
            }
        }

        parts.Reverse();
        var title = string.Join('.', parts);
        return new ParsedSubtitleName(
            stem,
            BuildCompareKey(title),
            ExtractEpisodeKey(stem),
            ExtractYear(stem),
            language,
            caption,
            string.IsNullOrEmpty(extension) ? ".srt" : extension.ToLowerInvariant());
    }

    public static string BuildDestinationFileName(
        string mediaFileName,
        string language,
        SubtitleCaption caption,
        string extension)
    {
        var dest = $"{mediaFileName}.{language}";
        if (caption != SubtitleCaption.None)
        {
            dest += $".{CaptionToken(caption)}";
        }

        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        return dest + extension.ToLowerInvariant();
    }

    public static bool IsAlreadyStandard(string mediaFileName, string sidecarFileName)
    {
        var parsed = ParseSidecar(sidecarFileName);
        if (parsed.Language == null)
        {
            return false;
        }

        var expected = BuildDestinationFileName(
            mediaFileName,
            parsed.Language,
            parsed.Caption,
            parsed.Extension);
        return string.Equals(sidecarFileName, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static int ScoreMatch(string mediaFileName, ParsedSubtitleName sidecar)
    {
        var mediaEpisode = ExtractEpisodeKey(mediaFileName);
        if (mediaEpisode != null && sidecar.EpisodeKey != null && mediaEpisode != sidecar.EpisodeKey)
        {
            return 0;
        }

        if (mediaEpisode != null && sidecar.EpisodeKey == null && EpisodeRegex.IsMatch(sidecar.Stem))
        {
            // Packed or unparseable episode tag on the sidecar.
            return 0;
        }

        var score = 0;
        if (mediaEpisode != null && sidecar.EpisodeKey == mediaEpisode)
        {
            score += 50;
        }

        var mediaKey = BuildCompareKey(mediaFileName);
        if (mediaKey.Length == 0 || sidecar.TitleKey.Length == 0)
        {
            return score;
        }

        if (mediaKey == sidecar.TitleKey)
        {
            score += 40;
        }
        else if (mediaKey.Contains(sidecar.TitleKey, StringComparison.Ordinal) && sidecar.TitleKey.Length >= 8)
        {
            score += 25;
        }
        else if (sidecar.TitleKey.Contains(mediaKey, StringComparison.Ordinal) && mediaKey.Length >= 8)
        {
            score += 25;
        }
        else
        {
            score += (int)Math.Round(TokenOverlap(mediaKey, sidecar.TitleKey) * 20);
        }

        var mediaYear = ExtractYear(mediaFileName);
        if (mediaYear != null && mediaYear == sidecar.Year)
        {
            score += 10;
        }

        return score;
    }

    public static SubtitleRenamePlan? PlanRename(
        string directory,
        string mediaFileName,
        IReadOnlyList<string> sidecarFileNames,
        string preferredLanguage)
    {
        var preferred = NormalizeLanguage(preferredLanguage) ?? preferredLanguage;
        SubtitleRenamePlan? best = null;

        foreach (var fileName in sidecarFileNames)
        {
            var parsed = ParseSidecar(fileName);
            if (parsed.Language != preferred)
            {
                continue;
            }

            if (IsAlreadyStandard(mediaFileName, fileName))
            {
                continue;
            }

            var score = ScoreMatch(mediaFileName, parsed);
            if (score < MinimumAcceptScore)
            {
                continue;
            }

            var destination = Path.Combine(
                directory,
                BuildDestinationFileName(mediaFileName, parsed.Language, parsed.Caption, parsed.Extension));
            if (string.Equals(Path.Combine(directory, fileName), destination, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (best == null || score > best.Score)
            {
                best = new SubtitleRenamePlan(
                    Path.Combine(directory, fileName),
                    destination,
                    parsed.Language,
                    parsed.Caption,
                    score);
            }
        }

        return best;
    }

    public static string BuildCompareKey(string name)
    {
        var text = name.ToLowerInvariant();
        text = text.Replace("'", "").Replace("’", "").Replace("`", "");
        text = text.Replace('+', ' ').Replace('&', ' ');
        text = TmdbRegex.Replace(text, " ");
        text = BracketRegex.Replace(text, " ");
        text = YearRegex.Replace(text, " ");
        text = ReleaseGroupRegex.Replace(text, " ");
        text = Regex.Replace(text, @"[^a-z0-9]+", " ");
        var tokens = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !QualityTokens.Contains(token) && !IsMostlyNumeric(token))
            .ToList();
        return string.Join(' ', tokens);
    }

    private static bool TryParseCaption(string part, out SubtitleCaption caption)
    {
        caption = part.ToLowerInvariant() switch
        {
            "sdh" => SubtitleCaption.Sdh,
            "hi" => SubtitleCaption.Hi,
            "forced" => SubtitleCaption.Forced,
            "cc" => SubtitleCaption.Cc,
            "ocr" => SubtitleCaption.Ocr,
            _ => SubtitleCaption.None
        };
        return caption != SubtitleCaption.None && CaptionTokens.Contains(part);
    }

    private static string CaptionToken(SubtitleCaption caption) => caption switch
    {
        SubtitleCaption.Sdh => "sdh",
        SubtitleCaption.Hi => "hi",
        SubtitleCaption.Forced => "forced",
        SubtitleCaption.Cc => "cc",
        SubtitleCaption.Ocr => "ocr",
        _ => ""
    };

    public static bool IsOcr(string? caption) =>
        string.Equals(caption, "ocr", StringComparison.OrdinalIgnoreCase);

    public static int RemoveOcrSidecars(string directory, string mediaFileName)
    {
        if (string.IsNullOrWhiteSpace(directory)
            || string.IsNullOrWhiteSpace(mediaFileName)
            || !Directory.Exists(directory))
        {
            return 0;
        }

        var removed = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(mediaFileName + ".", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ParseSidecar(name).Caption != SubtitleCaption.Ocr)
                {
                    continue;
                }

                File.Delete(path);
                removed++;
            }
        }
        catch (IOException)
        {
            return removed;
        }

        return removed;
    }

    private static int? ExtractYear(string name)
    {
        var match = YearRegex.Match(name);
        return match.Success ? int.Parse(match.Groups["year"].Value) : null;
    }

    private static double TokenOverlap(string left, string right)
    {
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0;
        }

        var intersection = leftTokens.Intersect(rightTokens).Count();
        return (double)intersection / Math.Max(leftTokens.Count, rightTokens.Count);
    }

    private static bool IsMostlyNumeric(string token) => token.All(char.IsDigit);
}
