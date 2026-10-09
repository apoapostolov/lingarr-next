using System.Text.RegularExpressions;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

public static partial class SubtitleHtml
{
    public static bool Enabled(string? value) =>
        !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    public static string Strip(string? line)
    {
        if (string.IsNullOrEmpty(line) || !line.Contains('<'))
        {
            return line ?? string.Empty;
        }

        var cleaned = Tag().Replace(line, string.Empty);
        return Space().Replace(cleaned, " ").Trim();
    }

    public static void Strip(SubtitleItem item)
    {
        item.Lines = item.Lines.Select(Strip).ToList();
        item.PlaintextLines = item.PlaintextLines.Select(Strip).ToList();
    }

    public static void StripTranslated(SubtitleItem item)
    {
        item.TranslatedLines = item.TranslatedLines.Select(Strip).ToList();
    }

    [GeneratedRegex(@"<\s*/?\s*[^>\s/]+[^>]*>", RegexOptions.Compiled)]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.Compiled)]
    private static partial Regex Space();
}
