namespace Lingarr.Server.Models.Plex;

public sealed record PlexPin(long Id, string Code);

public sealed record PlexAccount(string? Username, string? Email);

public sealed record PlexProbe(bool Ok, int? StatusCode, string? Error);

public sealed record PlexConnection(
    string Uri,
    bool Local,
    bool Relay);

public sealed record PlexDiscoveredServer(
    string Name,
    string MachineIdentifier,
    IReadOnlyList<PlexConnection> Connections);

public sealed record PlexReachableConnection(
    string Uri,
    bool Local,
    bool Relay,
    int LatencyMs);

public sealed record PlexReachableServer(
    string Name,
    string MachineIdentifier,
    IReadOnlyList<PlexReachableConnection> Connections);

public sealed class PlexSubtitleStream
{
    public long Id { get; init; }
    public string? LanguageCode { get; init; }
    public string? LanguageTag { get; init; }
    public string? Title { get; init; }
    public string? DisplayTitle { get; init; }
    public string? ExtendedDisplayTitle { get; init; }
    public bool Forced { get; init; }
    public bool Selected { get; init; }
}

public sealed class PlexPartMatch
{
    public required string RatingKey { get; init; }
    public required long PartId { get; init; }
    public required int PartCount { get; init; }
    public string? LibrarySectionId { get; init; }
    public string? PlexDirectory { get; init; }
    public required IReadOnlyList<PlexSubtitleStream> Subtitles { get; init; }
    public string MatchReason { get; init; } = "path";
}

public sealed record PlexLibrarySection(string Key, string Type, string Title, bool Refreshing = false);

public sealed record ResolvedPlexCredentials(
    string? Url,
    string? Token,
    string Source,
    string ClientId);
