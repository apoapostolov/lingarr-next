using System.IO;
using Lingarr.Server.Services.SubtitleDownload;

namespace Lingarr.Server.Services;

public static class LibraryCoverage
{
    public static string Format(IEnumerable<string>? codes)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (codes != null)
        {
            foreach (var code in codes)
            {
                var two = TwoLetter(code);
                if (two != null)
                {
                    set.Add(two);
                }
            }
        }

        return string.Join(',', set);
    }

    public static bool HasFullPair(
        string? coverage,
        IEnumerable<string>? extraLanguages,
        IReadOnlySet<string> sources,
        IReadOnlySet<string> targets)
    {
        if (sources.Count == 0 || targets.Count == 0)
        {
            return false;
        }

        var have = Parse(coverage);
        if (extraLanguages != null)
        {
            foreach (var code in extraLanguages)
            {
                var two = TwoLetter(code);
                if (two != null)
                {
                    have.Add(two);
                }
            }
        }

        return sources.Any(have.Contains) && targets.All(have.Contains);
    }

    public static bool CheckedWithoutSource(string? coverage, IReadOnlySet<string> sources)
    {
        if (coverage == null || sources.Count == 0)
        {
            return false;
        }

        var have = Parse(coverage);
        return !sources.Any(have.Contains);
    }

    public static bool MatchesCurrentFile(string? subtitlePath, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(subtitlePath) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var subtitleFile = Path.GetFileName(subtitlePath);
        return subtitleFile.StartsWith(fileName + ".", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasUsablePair(
        string? directory,
        string? fileName,
        IReadOnlySet<string> sources,
        IReadOnlySet<string> targets)
    {
        if (string.IsNullOrWhiteSpace(directory)
            || string.IsNullOrWhiteSpace(fileName)
            || sources.Count == 0
            || targets.Count == 0)
        {
            return false;
        }

        return sources.Any(language => HasUsableSidecar(directory, fileName, language))
            && targets.All(language => HasUsableSidecar(directory, fileName, language));
    }

    public static bool HasUsableSidecar(string directory, string fileName, string language)
    {
        var two = TwoLetter(language) ?? language.Trim();
        foreach (var extension in new[] { ".srt", ".ass", ".ssa" })
        {
            var path = Path.Combine(directory, fileName + "." + two + extension);
            if (File.Exists(path) && SubtitleBytes.IsUsableSidecar(path, two))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> Parse(string? coverage)
    {
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(coverage))
        {
            return have;
        }

        foreach (var code in coverage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var two = TwoLetter(code);
            if (two != null)
            {
                have.Add(two);
            }
        }

        return have;
    }

    private static string? TwoLetter(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        try
        {
            return Services.Integration.Plex.PlexLanguage.TwoLetter(code);
        }
        catch (ArgumentException)
        {
            return code.Trim().Length <= 3 ? code.Trim().ToLowerInvariant() : null;
        }
    }
}
