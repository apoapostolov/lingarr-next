using System.Text.Json;
using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Integration.Plex;

namespace Lingarr.Server.Services.Integration.Jellyfin;

public static class JellyfinWebhookReader
{
    public static PlexWebhookDecision Read(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return PlexWebhookDecision.Unreadable.Instance;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return PlexWebhookDecision.Unreadable.Instance;
            }

            var item = Object(root, "Item") ?? root;
            var notification = FirstString(root, "NotificationType", "Event")
                ?? FirstString(item, "NotificationType", "Event");
            if (!IsItemAdded(notification))
            {
                return PlexWebhookDecision.Ignored.Instance;
            }

            var type = FirstString(item, "ItemType", "Type") ?? FirstString(root, "ItemType", "Type");
            var kind = type?.ToLowerInvariant() switch
            {
                "movie" => "movie",
                "episode" => "episode",
                _ => null
            };
            if (kind == null)
            {
                return PlexWebhookDecision.Ignored.Instance;
            }

            var title = FirstString(item, "Name", "Title") ?? FirstString(root, "Name", "Title") ?? string.Empty;
            var showTitle = FirstString(item, "SeriesName") ?? FirstString(root, "SeriesName");
            var added = new PlexAddedMovie
            {
                Kind = kind,
                Title = title,
                ShowTitle = showTitle,
                Year = FirstInt(item, "Year", "ProductionYear") ?? FirstInt(root, "Year", "ProductionYear"),
                SeasonNumber = FirstInt(item, "SeasonNumber", "ParentIndexNumber")
                    ?? FirstInt(root, "SeasonNumber", "ParentIndexNumber"),
                EpisodeNumber = FirstInt(item, "EpisodeNumber", "IndexNumber")
                    ?? FirstInt(root, "EpisodeNumber", "IndexNumber"),
                RatingKey = FirstString(item, "ItemId", "Id") ?? FirstString(root, "ItemId", "Id"),
                Guids = Guids(root, item).ToList()
            };
            if (kind == "movie")
            {
                added.SeasonNumber = null;
                added.EpisodeNumber = null;
            }

            return new PlexWebhookDecision.Added(added);
        }
    }

    private static bool IsItemAdded(string? notification) =>
        string.Equals(notification, "ItemAdded", StringComparison.OrdinalIgnoreCase)
        || string.Equals(notification, "item.added", StringComparison.OrdinalIgnoreCase)
        || string.Equals(notification, "library.new", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Guids(JsonElement root, JsonElement item)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in new[] { item, root })
        {
            foreach (var guid in ProviderGuids(element))
            {
                if (seen.Add(guid))
                {
                    yield return guid;
                }
            }

            if (Object(element, "ProviderIds") is { } providers)
            {
                foreach (var guid in ProviderObject(providers))
                {
                    if (seen.Add(guid))
                    {
                        yield return guid;
                    }
                }
            }

            if (Object(element, "SeriesProviderIds") is { } seriesIds)
            {
                foreach (var guid in ProviderObject(seriesIds))
                {
                    if (seen.Add(guid))
                    {
                        yield return guid;
                    }
                }
            }

            if (Object(element, "Series") is { } series && Object(series, "ProviderIds") is { } seriesProviders)
            {
                foreach (var guid in ProviderObject(seriesProviders))
                {
                    if (seen.Add(guid))
                    {
                        yield return guid;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ProviderGuids(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            string? kind = null;
            if (name.EndsWith("_tmdb", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Tmdb", StringComparison.OrdinalIgnoreCase))
            {
                kind = "tmdb";
            }
            else if (name.EndsWith("_imdb", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Imdb", StringComparison.OrdinalIgnoreCase))
            {
                kind = "imdb";
            }
            else if (name.EndsWith("_tvdb", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Tvdb", StringComparison.OrdinalIgnoreCase))
            {
                kind = "tvdb";
            }

            if (kind == null)
            {
                continue;
            }

            var value = StringValue(property.Value);
            if (!Usable(value))
            {
                continue;
            }

            yield return kind + "://" + value;
        }
    }

    private static IEnumerable<string> ProviderObject(JsonElement providers)
    {
        foreach (var property in providers.EnumerateObject())
        {
            var kind = property.Name.ToLowerInvariant() switch
            {
                "tmdb" => "tmdb",
                "imdb" => "imdb",
                "tvdb" => "tvdb",
                _ => null
            };
            var value = StringValue(property.Value);
            if (kind == null || !Usable(value))
            {
                continue;
            }

            yield return kind + "://" + value;
        }
    }

    private static bool Usable(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains("{{", StringComparison.Ordinal);

    private static JsonElement? Object(JsonElement element, string name)
    {
        var property = Find(element, name);
        return property?.ValueKind == JsonValueKind.Object ? property : null;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = StringValue(Find(element, name));
            if (Usable(value))
            {
                return value;
            }
        }

        return null;
    }

    private static int? FirstInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var property = Find(element, name);
            if (property == null)
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number))
            {
                return number;
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && int.TryParse(property.Value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static JsonElement? Find(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? StringValue(JsonElement? property)
    {
        if (property == null)
        {
            return null;
        }

        return property.Value.ValueKind switch
        {
            JsonValueKind.String => property.Value.GetString(),
            JsonValueKind.Number => property.Value.GetRawText(),
            _ => null
        };
    }
}
