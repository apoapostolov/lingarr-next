using System.Text;

namespace Lingarr.Server.Services.SubtitleDownload;

internal static class SubtitleBytes
{
    private static readonly string[] FrenchMarkers =
    [
        " c'est ", " j'ai ", " une ", " les ", " des ", " nous ", " vous ",
        " avec ", " dans ", " pour ", " pas ", " qui ", " que ", " je ",
        " tu ", " elle ", " est ", " rien ", " madame ", " bien ", " veut ",
        " quand "
    ];

    private static readonly string[] EnglishMarkers =
    [
        " the ", " and ", " you ", " that ", " it's ", " don't ", " i'm ",
        " not ", " this ", " what ", " have ", " with ", " from ", " they "
    ];

    public static bool IsUsableSidecar(string? path, string language)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return true;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 32)
            {
                return false;
            }

            return !IsHtml(bytes) && !MismatchesLanguage(bytes, language);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static bool IsHtml(byte[] content)
    {
        if (content.Length < 6)
        {
            return false;
        }

        var length = Math.Min(content.Length, 512);
        var text = Encoding.UTF8.GetString(content, 0, length).TrimStart();
        if (text.StartsWith('\uFEFF'))
        {
            text = text.TrimStart('\uFEFF').TrimStart();
        }

        return text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<head>", StringComparison.OrdinalIgnoreCase);
    }

    public static bool MismatchesLanguage(byte[] content, string language)
    {
        if (content.Length < 32 || string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        var code = language.Trim().ToLowerInvariant();
        if (code is "fr" or "fra" or "fre" or "french")
        {
            return false;
        }

        var padded = " " + Normalize(Sample(content)) + " ";
        var letters = 0;
        var cyrillic = 0;
        var nonLatin = 0;
        foreach (var ch in padded)
        {
            if (!char.IsLetter(ch))
            {
                continue;
            }

            letters++;
            if (IsCyrillic(ch))
            {
                cyrillic++;
            }

            if (!IsLatinLetter(ch))
            {
                nonLatin++;
            }
        }

        if (letters < 40)
        {
            return false;
        }

        if (code is "bg" or "bul" or "bulgarian" or "ru" or "rus" or "uk" or "ukr" or "mk" or "sr" or "srp")
        {
            return cyrillic * 20 < letters;
        }

        if (code is "en" or "eng" or "english")
        {
            if (nonLatin * 5 > letters)
            {
                return true;
            }

            var french = CountMarkers(padded, FrenchMarkers);
            var english = CountMarkers(padded, EnglishMarkers);
            return french >= 6 && french > english * 2;
        }

        return false;
    }

    private static string Sample(byte[] content)
    {
        var length = Math.Min(content.Length, 4096);
        var utf = Encoding.UTF8.GetString(content, 0, length);
        return utf.Contains('\uFFFD')
            ? Encoding.Latin1.GetString(content, 0, length)
            : utf;
    }

    private static string Normalize(string text)
    {
        var chars = text.ToLowerInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is '\'' or '\u2019')
            {
                continue;
            }

            if (!char.IsLetter(chars[i]))
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }

    private static int CountMarkers(string padded, string[] markers)
    {
        var total = 0;
        foreach (var marker in markers)
        {
            var start = 0;
            while (true)
            {
                var at = padded.IndexOf(marker, start, StringComparison.Ordinal);
                if (at < 0)
                {
                    break;
                }

                total++;
                start = at + marker.Length;
            }
        }

        return total;
    }

    private static bool IsCyrillic(char ch) =>
        ch is (>= '\u0400' and <= '\u04FF') or (>= '\u0500' and <= '\u052F');

    private static bool IsLatinLetter(char ch) => ch <= '\u024F';
}
