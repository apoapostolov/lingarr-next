namespace Lingarr.Server.Services.Integration.Bazarr;

public sealed record BazarrCandidate(
    string Language,
    int Score,
    bool Forced,
    bool HearingImpaired,
    string Provider,
    string SubtitleId,
    bool OriginalFormat);

public static class BazarrSubtitleChoice
{
    public static BazarrCandidate? PickBest(
        IEnumerable<BazarrCandidate> candidates,
        IReadOnlySet<string> sourceLanguages,
        int minimumScore)
    {
        return candidates
            .Where(candidate => !candidate.Forced)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Provider))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SubtitleId))
            .Where(candidate => LanguageMatches(candidate.Language, sourceLanguages))
            .Where(candidate => candidate.Score >= minimumScore)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.HearingImpaired)
            .FirstOrDefault();
    }

    public static bool LanguageMatches(string? language, IReadOnlySet<string> sourceLanguages)
    {
        if (string.IsNullOrWhiteSpace(language) || sourceLanguages.Count == 0)
        {
            return false;
        }

        var token = language.Split(':')[0].Trim().ToLowerInvariant();
        foreach (var code in sourceLanguages)
        {
            var wanted = code.Trim().ToLowerInvariant();
            if (wanted.Length == 0)
            {
                continue;
            }

            if (token == wanted || token.StartsWith(wanted, StringComparison.Ordinal) ||
                wanted.StartsWith(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
