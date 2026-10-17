using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lingarr.Server.Services.SubtitleDownload;

public static class SubtitleDownloadChain
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static List<SubtitleDownloadChainEntry> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<SubtitleDownloadChainEntry>>(trimmed, JsonOptions);
                return (entries ?? [])
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Provider))
                    .ToList();
            }
            catch (JsonException)
            {
                return [];
            }
        }

        if (SubtitleDownloadCatalog.Find(trimmed) != null)
        {
            return [new SubtitleDownloadChainEntry { Provider = trimmed }];
        }

        return [];
    }

    public static string Serialize(IEnumerable<SubtitleDownloadChainEntry> entries) =>
        JsonSerializer.Serialize(
            entries.Select(entry => new { id = entry.Id, provider = entry.Provider }),
            JsonOptions);
}
