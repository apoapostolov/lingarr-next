using System.Text.Json;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Services;

namespace Lingarr.Server.Services.Integration.Plex;

public static class PlexMatcher
{
    public static string BuildAuthUrl(string clientId, string code)
    {
        return "https://app.plex.tv/auth#?"
            + "clientID=" + Uri.EscapeDataString(clientId)
            + "&code=" + Uri.EscapeDataString(code)
            + "&context[device][product]=Lingarr";
    }

    public static IReadOnlyList<PlexReachableConnection> Rank(
        IEnumerable<PlexReachableConnection> connections)
    {
        return connections
            .OrderByDescending(connection => connection.Local && !connection.Relay)
            .ThenBy(connection => connection.Relay)
            .ThenBy(connection => connection.LatencyMs)
            .ToList();
    }

    public static IReadOnlyList<string> DirectoryCandidates(string path)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(path);
        Add(MediaLibraryRefreshService.ToWindowsMediaPath(path));
        return candidates.ToList();

        void Add(string? value)
        {
            var normalized = NormalizeDirectory(value);
            if (normalized != null)
            {
                candidates.Add(normalized);
            }
        }
    }

    public static PlexPartMatch? MatchMetadata(
        string metadataJson,
        IReadOnlyCollection<string> directoryCandidates,
        string fileStem)
    {
        using var document = JsonDocument.Parse(metadataJson);
        PlexPartMatch? best = null;
        var bestLength = -1;

        foreach (var metadata in EnumerateMetadata(document.RootElement))
        {
            var ratingKey = ReadString(metadata, "ratingKey");
            if (string.IsNullOrWhiteSpace(ratingKey) || !metadata.TryGetProperty("Media", out var mediaList))
            {
                continue;
            }

            var partCount = CountParts(mediaList);
            var sectionId = ReadString(metadata, "librarySectionID");
            if (string.IsNullOrWhiteSpace(sectionId)
                && document.RootElement.TryGetProperty("MediaContainer", out var container))
            {
                sectionId = ReadString(container, "librarySectionID");
            }

            foreach (var media in Enumerate(mediaList))
            {
                if (!media.TryGetProperty("Part", out var parts))
                {
                    continue;
                }

                foreach (var part in Enumerate(parts))
                {
                    var file = ReadString(part, "file");
                    var partId = ReadLong(part, "id");
                    if (string.IsNullOrWhiteSpace(file) || partId == null)
                    {
                        continue;
                    }

                    if (!StemEquals(file, fileStem))
                    {
                        continue;
                    }

                    var directory = DirectoryOf(file);
                    var normalized = NormalizeDirectory(directory);
                    if (normalized == null
                        || !directoryCandidates.Any(candidate =>
                            string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (normalized.Length <= bestLength)
                    {
                        continue;
                    }

                    bestLength = normalized.Length;
                    best = new PlexPartMatch
                    {
                        RatingKey = ratingKey,
                        PartId = partId.Value,
                        PartCount = partCount,
                        LibrarySectionId = sectionId,
                        PlexDirectory = directory,
                        Subtitles = ReadSubtitles(part)
                    };
                }
            }
        }

        return best;
    }

    public sealed class PlexPageScan
    {
        public PlexPartMatch? PathMatch { get; init; }
        public PlexPartMatch? GuidMatch { get; init; }
        public PlexPartMatch? NameMatch { get; init; }
        public int ItemCount { get; init; }
        public int? TotalSize { get; init; }
    }

    public static PlexPageScan MatchPage(
        string pageJson,
        PlexLookup lookup,
        IReadOnlyCollection<string> directoryCandidates)
    {
        using var document = JsonDocument.Parse(pageJson);
        var root = document.RootElement;
        if (root.TryGetProperty("MediaContainer", out var container))
        {
            root = container;
        }

        PlexPartMatch? path = null;
        PlexPartMatch? guid = null;
        PlexPartMatch? name = null;
        var count = 0;
        foreach (var metadata in EnumerateMetadata(document.RootElement))
        {
            count++;
            var ratingKey = ReadString(metadata, "ratingKey");
            if (string.IsNullOrWhiteSpace(ratingKey) || !metadata.TryGetProperty("Media", out var mediaList))
            {
                continue;
            }

            var built = MatchMetadata(
                metadata.GetRawText(),
                directoryCandidates,
                lookup.FileStem);
            if (built != null)
            {
                path = built;
                break;
            }

            if (!IdentityMatches(metadata, lookup))
            {
                continue;
            }

            var reason = GuidMatches(metadata, lookup) ? "id" : "name";
            var part = FirstUsablePart(mediaList, lookup.FileStem);
            if (part == null)
            {
                continue;
            }

            var match = new PlexPartMatch
            {
                RatingKey = ratingKey,
                PartId = part.Value.PartId,
                PartCount = CountParts(mediaList),
                LibrarySectionId = ReadString(metadata, "librarySectionID"),
                PlexDirectory = part.Value.Directory,
                Subtitles = part.Value.Subtitles,
                MatchReason = reason
            };
            if (reason == "id")
            {
                guid = match;
                break;
            }

            name ??= match;
        }

        return new PlexPageScan
        {
            PathMatch = path,
            GuidMatch = guid,
            NameMatch = name,
            ItemCount = count,
            TotalSize = ReadLong(root, "totalSize") is long total ? (int)total : null
        };
    }

    private static bool IdentityMatches(JsonElement metadata, PlexLookup lookup) =>
        lookup.MatchesItem(
            ReadString(metadata, "title"),
            ReadString(metadata, "originalTitle"),
            ReadString(metadata, "slug"),
            ReadGuids(metadata));

    private static bool GuidMatches(JsonElement metadata, PlexLookup lookup) =>
        ReadGuids(metadata).Any(guid =>
            lookup.ExternalIds.Contains(guid, StringComparer.OrdinalIgnoreCase));

    private static IEnumerable<string> ReadGuids(JsonElement metadata)
    {
        if (!metadata.TryGetProperty("Guid", out var guids))
        {
            yield break;
        }

        foreach (var guid in Enumerate(guids))
        {
            var id = ReadString(guid, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                yield return id;
            }
        }
    }

    private static (long PartId, string? Directory, IReadOnlyList<PlexSubtitleStream> Subtitles)? FirstPart(
        JsonElement mediaList)
    {
        foreach (var media in Enumerate(mediaList))
        {
            if (!media.TryGetProperty("Part", out var parts))
            {
                continue;
            }

            foreach (var part in Enumerate(parts))
            {
                var partId = ReadLong(part, "id");
                if (partId == null)
                {
                    continue;
                }

                var file = ReadString(part, "file");
                return (partId.Value, DirectoryOf(file ?? ""), ReadSubtitles(part));
            }
        }

        return null;
    }

    private static (long PartId, string? Directory, IReadOnlyList<PlexSubtitleStream> Subtitles)? FirstUsablePart(
        JsonElement mediaList,
        string fileStem)
    {
        (long PartId, string? Directory, IReadOnlyList<PlexSubtitleStream> Subtitles)? only = null;
        var count = 0;
        foreach (var media in Enumerate(mediaList))
        {
            if (!media.TryGetProperty("Part", out var parts))
            {
                continue;
            }

            foreach (var part in Enumerate(parts))
            {
                var partId = ReadLong(part, "id");
                var file = ReadString(part, "file");
                if (partId == null)
                {
                    continue;
                }

                count++;
                var candidate = (
                    partId.Value,
                    DirectoryOf(file ?? ""),
                    (IReadOnlyList<PlexSubtitleStream>)ReadSubtitles(part));
                if (!string.IsNullOrWhiteSpace(file) && StemEquals(file, fileStem))
                {
                    return candidate;
                }

                only ??= candidate;
            }
        }

        return count == 1 ? only : null;
    }

    public static (IReadOnlyList<string> RatingKeys, int ItemCount, int? TotalSize) ReadLibraryPage(string pageJson)
    {
        using var document = JsonDocument.Parse(pageJson);
        var root = document.RootElement;
        if (root.TryGetProperty("MediaContainer", out var container))
        {
            root = container;
        }

        var keys = new List<string>();
        foreach (var metadata in EnumerateMetadata(document.RootElement))
        {
            var ratingKey = ReadString(metadata, "ratingKey");
            if (!string.IsNullOrWhiteSpace(ratingKey))
            {
                keys.Add(ratingKey);
            }
        }

        return (keys, keys.Count, ReadLong(root, "totalSize") is long total ? (int)total : null);
    }

    public static PlexPartMatch? PrimaryPart(string metadataJson)
    {
        using var document = JsonDocument.Parse(metadataJson);
        foreach (var metadata in EnumerateMetadata(document.RootElement))
        {
            var ratingKey = ReadString(metadata, "ratingKey");
            if (string.IsNullOrWhiteSpace(ratingKey) || !metadata.TryGetProperty("Media", out var mediaList))
            {
                continue;
            }

            var part = FirstPart(mediaList);
            if (part == null)
            {
                continue;
            }

            var sectionId = ReadString(metadata, "librarySectionID");
            if (string.IsNullOrWhiteSpace(sectionId)
                && document.RootElement.TryGetProperty("MediaContainer", out var container))
            {
                sectionId = ReadString(container, "librarySectionID");
            }

            return new PlexPartMatch
            {
                RatingKey = ratingKey,
                PartId = part.Value.PartId,
                PartCount = CountParts(mediaList),
                LibrarySectionId = sectionId,
                PlexDirectory = part.Value.Directory,
                Subtitles = part.Value.Subtitles
            };
        }

        return null;
    }

    public static PlexSubtitleStream? PickSubtitle(
        IReadOnlyList<PlexSubtitleStream> streams,
        string targetLanguage)
    {
        var matches = streams.Where(stream => StreamMatchesLanguage(stream, targetLanguage)).ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        var already = matches.FirstOrDefault(stream => stream.Selected);
        if (already != null)
        {
            return already;
        }

        var nonForced = matches.Where(stream => !stream.Forced).ToList();
        var pool = nonForced.Count > 0 ? nonForced : matches;
        var external = pool.Where(IsExternal).ToList();
        var ranked = external.Count > 0 ? external : pool;
        return ranked.OrderByDescending(stream => stream.Id).First();
    }

    public static PlexSubtitleStream? PickNewSubtitle(
        IReadOnlyList<PlexSubtitleStream> before,
        IReadOnlyList<PlexSubtitleStream> after,
        string targetLanguage,
        string? preferredTitle = null)
    {
        var known = before.Select(stream => stream.Id).ToHashSet();
        var added = after.Where(stream => !known.Contains(stream.Id)).ToList();
        var matches = added.Where(stream => StreamMatchesLanguage(stream, targetLanguage)).ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        var nonForced = matches.Where(stream => !stream.Forced).ToList();
        var pool = nonForced.Count > 0 ? nonForced : matches;
        if (!string.IsNullOrWhiteSpace(preferredTitle))
        {
            var named = pool.FirstOrDefault(stream =>
                string.Equals(stream.Title, preferredTitle, StringComparison.OrdinalIgnoreCase));
            if (named != null)
            {
                return named;
            }
        }

        var external = pool.Where(IsExternal).ToList();
        var ranked = external.Count > 0 ? external : pool;
        return ranked.OrderByDescending(stream => stream.Id).First();
    }

    public static IReadOnlyList<string> CollectRatingKeys(string searchJson, int limit = 20)
    {
        using var document = JsonDocument.Parse(searchJson);
        var keys = new List<string>();
        Collect(document.RootElement, keys, limit);
        return keys;
    }

    private static void Collect(JsonElement element, List<string> keys, int limit)
    {
        if (keys.Count >= limit)
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("ratingKey", out var ratingKey))
            {
                var value = ReadScalar(ratingKey);
                if (!string.IsNullOrWhiteSpace(value) && !keys.Contains(value))
                {
                    keys.Add(value);
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                Collect(property.Value, keys, limit);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, keys, limit);
            }
        }
    }

    private static bool StreamMatchesLanguage(PlexSubtitleStream stream, string targetLanguage)
    {
        return PlexLanguage.Same(stream.LanguageTag, targetLanguage)
            || PlexLanguage.Same(stream.LanguageCode, targetLanguage);
    }

    private static bool IsExternal(PlexSubtitleStream stream)
    {
        var label = stream.ExtendedDisplayTitle ?? stream.DisplayTitle ?? "";
        return label.Contains("External", StringComparison.OrdinalIgnoreCase);
    }

    private static List<PlexSubtitleStream> ReadSubtitles(JsonElement part)
    {
        var streams = new List<PlexSubtitleStream>();
        if (!part.TryGetProperty("Stream", out var streamList))
        {
            return streams;
        }

        foreach (var stream in Enumerate(streamList))
        {
            if (ReadLong(stream, "streamType") != 3)
            {
                continue;
            }

            var id = ReadLong(stream, "id");
            if (id == null)
            {
                continue;
            }

            streams.Add(new PlexSubtitleStream
            {
                Id = id.Value,
                LanguageCode = ReadString(stream, "languageCode"),
                LanguageTag = ReadString(stream, "languageTag"),
                Title = ReadString(stream, "title"),
                DisplayTitle = ReadString(stream, "displayTitle"),
                ExtendedDisplayTitle = ReadString(stream, "extendedDisplayTitle"),
                Forced = ReadFlag(stream, "forced"),
                Selected = ReadFlag(stream, "selected")
            });
        }

        return streams;
    }

    private static int CountParts(JsonElement mediaList)
    {
        var count = 0;
        foreach (var media in Enumerate(mediaList))
        {
            if (!media.TryGetProperty("Part", out var parts))
            {
                continue;
            }

            count += Enumerate(parts).Count();
        }

        return count;
    }

    private static IEnumerable<JsonElement> EnumerateMetadata(JsonElement root)
    {
        if (root.TryGetProperty("MediaContainer", out var container))
        {
            root = container;
        }

        if (root.TryGetProperty("Metadata", out var metadata))
        {
            return Enumerate(metadata);
        }

        return root.ValueKind == JsonValueKind.Object ? [root] : [];
    }

    private static IEnumerable<JsonElement> Enumerate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().ToList();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            return [element];
        }

        return [];
    }

    private static bool StemEquals(string file, string fileStem)
    {
        var name = file.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        var dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            name = name[..dot];
        }

        return string.Equals(name, fileStem, StringComparison.OrdinalIgnoreCase);
    }

    public static string? DirectoryOf(string file)
    {
        var index = Math.Max(file.LastIndexOf('/'), file.LastIndexOf('\\'));
        return index > 0 ? file[..index] : null;
    }

    public static string? NormalizeDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return ReadScalar(value);
    }

    private static long? ReadLong(JsonElement element, string name)
    {
        var text = ReadString(element, name);
        return long.TryParse(text, out var parsed) ? parsed : null;
    }

    private static bool ReadFlag(JsonElement element, string name)
    {
        var text = ReadString(element, name);
        return text is "1" or "true";
    }

    private static string? ReadScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
