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

    private static readonly string[] GermanMarkers =
    [
        " und ", " der ", " die ", " das ", " ich ", " nicht ", " sie ",
        " ein ", " eine ", " ist ", " mit ", " auf ", " für ", " den ",
        " dem ", " von ", " zu ", " wir "
    ];

    private static readonly string[] SpanishMarkers =
    [
        " que ", " los ", " las ", " una ", " por ", " para ", " está ",
        " como ", " pero ", " porque ", " también ", " muy ", " este ",
        " esta "
    ];

    private static readonly string[] ItalianMarkers =
    [
        " che ", " non ", " una ", " per ", " con ", " sono ", " questo ",
        " della ", " anche ", " più ", " questo ", " come "
    ];

    private static readonly string[] PortugueseMarkers =
    [
        " que ", " não ", " uma ", " para ", " com ", " está ", " por ",
        " como ", " mais ", " muito ", " você ", " não "
    ];

    private static readonly string[][] LatinForeignMarkers =
    [
        FrenchMarkers,
        GermanMarkers,
        SpanishMarkers,
        ItalianMarkers,
        PortugueseMarkers
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
        var text = Decode(content, length).TrimStart();
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
        if (code is "fr" or "fra" or "fre" or "french"
            or "de" or "deu" or "ger" or "german"
            or "es" or "spa" or "spanish"
            or "it" or "ita" or "italian"
            or "pt" or "por" or "portuguese")
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

            var english = CountMarkers(padded, EnglishMarkers);
            foreach (var markers in LatinForeignMarkers)
            {
                var foreign = CountMarkers(padded, markers);
                if (foreign >= 6 && foreign > english * 2)
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    private static string Sample(byte[] content)
    {
        var length = Math.Min(content.Length, 4096);
        return Decode(content, length);
    }

    private static string Decode(byte[] content, int length)
    {
        if (length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(content, 0, length);
        }

        if (length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(content, 0, length);
        }

        var utf = Encoding.UTF8.GetString(content, 0, length);
        if (!utf.Contains('\uFFFD'))
        {
            return utf;
        }

        if (LooksLikeUtf16Le(content, length))
        {
            return Encoding.Unicode.GetString(content, 0, length & ~1);
        }

        return Encoding.Latin1.GetString(content, 0, length);
    }

    private static bool LooksLikeUtf16Le(byte[] content, int length)
    {
        if (length < 8)
        {
            return false;
        }

        var nuls = 0;
        var limit = Math.Min(length, 64);
        for (var i = 1; i < limit; i += 2)
        {
            if (content[i] == 0)
            {
                nuls++;
            }
        }

        return nuls * 4 >= limit;
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
