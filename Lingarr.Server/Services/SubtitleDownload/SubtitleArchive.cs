using System.IO.Compression;
using System.Text;

namespace Lingarr.Server.Services.SubtitleDownload;

internal static class SubtitleArchive
{
    private static readonly string[] SubtitleExtensions = [".srt", ".ass", ".ssa", ".vtt", ".sub"];

    public static byte[] Unpack(byte[] payload, string? fileName, out string extension, string? language = null)
    {
        extension = Path.GetExtension(fileName ?? "") is { Length: > 0 } named
            ? named.ToLowerInvariant()
            : ".srt";

        if (payload.Length >= 2 && payload[0] == 0x1F && payload[1] == 0x8B)
        {
            using var input = new MemoryStream(payload);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            extension = ".srt";
            return output.ToArray();
        }

        if (payload.Length >= 4 && payload[0] == (byte)'P' && payload[1] == (byte)'K')
        {
            using var input = new MemoryStream(payload);
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
            var entries = zip.Entries
                .Where(candidate => SubtitleExtensions.Contains(Path.GetExtension(candidate.Name).ToLowerInvariant()))
                .ToList();
            var pool = entries;
            if (!string.IsNullOrWhiteSpace(language))
            {
                var matched = entries
                    .Where(candidate => NameMatchesLanguage(candidate.Name, language)
                                        || NameMatchesLanguage(candidate.FullName, language))
                    .ToList();
                if (matched.Count > 0)
                {
                    pool = matched;
                }
            }

            var entry = pool
                .OrderBy(candidate => candidate.FullName.Count(ch => ch is '/' or '\\'))
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (entry != null)
            {
                using var stream = entry.Open();
                using var output = new MemoryStream();
                stream.CopyTo(output);
                extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                return output.ToArray();
            }
        }

        if (LooksLikeText(payload) && !SubtitleExtensions.Contains(extension))
        {
            extension = ".srt";
        }

        return payload;
    }

    private static bool NameMatchesLanguage(string name, string language)
    {
        var lower = name.Replace('\\', '/').ToLowerInvariant();
        var code = language.Trim().ToLowerInvariant();
        if (lower.Contains($"-{code}.")
            || lower.Contains($".{code}.")
            || lower.Contains($"_{code}.")
            || lower.Contains($"/{code}/")
            || lower.StartsWith($"{code}.")
            || lower.Equals($"{code}.srt", StringComparison.Ordinal)
            || lower.Equals($"{code}.ass", StringComparison.Ordinal))
        {
            return true;
        }

        return code switch
        {
            "en" or "eng" => lower.Contains("english") || lower.StartsWith("en.") || lower.Contains("-en.") || lower.Contains(".en."),
            "bg" or "bul" => lower.Contains("bulgarian") || lower.StartsWith("bg."),
            "de" or "deu" or "ger" => lower.Contains("german") || lower.StartsWith("de."),
            _ => false
        };
    }

    private static bool LooksLikeText(byte[] payload)
    {
        var sample = Encoding.UTF8.GetString(payload, 0, Math.Min(payload.Length, 400));
        return sample.Contains("-->") || sample.Contains("[Script Info]", StringComparison.OrdinalIgnoreCase);
    }
}
