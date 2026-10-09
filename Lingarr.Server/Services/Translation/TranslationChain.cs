using System.Text.Json;
using System.Text.Json.Serialization;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// One step in the ordered translation fallback chain (primary = index 0).
/// </summary>
public sealed class TranslationChainEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = SettingKeys.Translation.DefaultServiceType;

    /// <summary>Optional model id for multi-model AI providers. Null/empty for NMT scrapers.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("systemPromptProfileId")]
    public int? SystemPromptProfileId { get; set; }

    [JsonPropertyName("contextPromptProfileId")]
    public int? ContextPromptProfileId { get; set; }

    [JsonIgnore]
    public string? ResolvedSystemPrompt { get; set; }

    [JsonIgnore]
    public string? ResolvedContextPrompt { get; set; }

    [JsonIgnore]
    public int? ResolvedSystemVersionId { get; set; }

    [JsonIgnore]
    public int? ResolvedContextVersionId { get; set; }

    [JsonIgnore]
    public string? ResolvedSystemContentHash { get; set; }

    [JsonIgnore]
    public string? ResolvedContextContentHash { get; set; }

    [JsonIgnore]
    public string ProviderNormalized => Provider.Trim().ToLowerInvariant();

    [JsonIgnore]
    public string? RequestSourceLanguage { get; set; }

    [JsonIgnore]
    public string? RequestTargetLanguage { get; set; }
}

public static class TranslationChain
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void StampLanguages(IEnumerable<TranslationChainEntry> entries, string? source, string? target)
    {
        foreach (var entry in entries)
        {
            entry.RequestSourceLanguage = source;
            entry.RequestTargetLanguage = target;
        }
    }

    public static List<TranslationChainEntry> Parse(string? raw, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [new TranslationChainEntry { Provider = SettingKeys.Translation.DefaultServiceType }];
        }

        var trimmed = raw.Trim();

        if (!trimmed.StartsWith('['))
        {
            return [new TranslationChainEntry { Provider = trimmed }];
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                return [new TranslationChainEntry { Provider = SettingKeys.Translation.DefaultServiceType }];
            }

            var list = new List<TranslationChainEntry>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        list.Add(new TranslationChainEntry { Provider = s! });
                    }
                    continue;
                }

                if (el.ValueKind == JsonValueKind.Object)
                {
                    var provider = el.TryGetProperty("provider", out var p) ? p.GetString()
                        : el.TryGetProperty("service", out var s2) ? s2.GetString()
                        : el.TryGetProperty("name", out var n) ? n.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(provider))
                    {
                        continue;
                    }
                    string? model = null;
                    if (el.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
                    {
                        model = m.GetString();
                    }
                    var id = el.TryGetProperty("id", out var idElement) &&
                             idElement.ValueKind == JsonValueKind.String
                        ? idElement.GetString()
                        : null;
                    int? systemPromptProfileId = el.TryGetProperty(
                        "systemPromptProfileId", out var systemProfile) &&
                        systemProfile.TryGetInt32(out var parsedSystemProfile)
                            ? parsedSystemProfile
                            : null;
                    int? contextPromptProfileId = el.TryGetProperty(
                        "contextPromptProfileId", out var contextProfile) &&
                        contextProfile.TryGetInt32(out var parsedContextProfile)
                            ? parsedContextProfile
                            : null;
                    list.Add(new TranslationChainEntry
                    {
                        Id = string.IsNullOrWhiteSpace(id)
                            ? Guid.NewGuid().ToString("N")
                            : id!,
                        Provider = provider!,
                        Model = string.IsNullOrWhiteSpace(model) ? null : model!.Trim(),
                        SystemPromptProfileId = systemPromptProfileId,
                        ContextPromptProfileId = contextPromptProfileId
                    });
                }
            }

            if (list.Count > 0)
            {
                return list;
            }
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex,
                "service_type setting contained malformed JSON, falling back to '{Default}'.",
                SettingKeys.Translation.DefaultServiceType);
        }

        return [new TranslationChainEntry { Provider = SettingKeys.Translation.DefaultServiceType }];
    }

    public static string Normalize(string? raw, ILogger? logger = null) =>
        JsonSerializer.Serialize(Parse(raw, logger), JsonOptions);

    public static string Serialize(IReadOnlyList<TranslationChainEntry> entries) =>
        JsonSerializer.Serialize(entries, JsonOptions);

    public static bool SupportsModel(string provider) =>
        provider.Trim().ToLowerInvariant() is
            "openai" or "anthropic" or "gemini" or "deepseek" or "localai"
            or "openrouter" or "zai" or "opencode-go";
}
