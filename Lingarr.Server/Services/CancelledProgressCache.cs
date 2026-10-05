using System.Text.RegularExpressions;

namespace Lingarr.Server.Services;

/// <summary>
/// Decides whether saved lines from a cancelled translation are worth continuing.
/// </summary>
public static partial class CancelledProgressCache
{
    public const int DefaultThreshold = 90;

    public static bool IsEnabled(string? raw) =>
        !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);

    public static int ThresholdOrDefault(string? raw)
    {
        if (!int.TryParse(raw, out var value))
        {
            return DefaultThreshold;
        }

        return Math.Clamp(value, 0, 100);
    }

    public static bool ShouldContinue(int? qualityScore, int threshold) =>
        qualityScore.HasValue && qualityScore.Value >= threshold;

    public static int? ProgressPercent(int translatedPositions, int totalLines)
    {
        if (translatedPositions <= 0 || totalLines <= 0)
        {
            return translatedPositions <= 0 ? 0 : null;
        }

        var percent = (int)Math.Round(100d * translatedPositions / totalLines);
        if (percent == 0)
        {
            percent = 1;
        }

        return Math.Clamp(percent, 1, 100);
    }

    public static bool SourceStillMatches(
        string? savedSource,
        IReadOnlyList<string> rawLines,
        IReadOnlyList<string> plainLines)
    {
        if (string.IsNullOrWhiteSpace(savedSource))
        {
            return false;
        }

        var saved = Normalize(savedSource);
        return saved == Normalize(string.Join(" ", rawLines))
            || saved == Normalize(string.Join(" ", plainLines))
            || saved == Normalize(string.Join("\n", rawLines))
            || saved == Normalize(string.Join("\n", plainLines));
    }

    public static string Normalize(string value) =>
        WhitespaceRegex().Replace(value.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
