namespace Lingarr.Server.Models.Api;

public sealed class PlexStatusResponse
{
    public bool Connected { get; set; }
    public string? Username { get; set; }
    public string? ServerName { get; set; }
    public string? ServerUrl { get; set; }
    public string? AuthMethod { get; set; }
    public string Source { get; set; } = "setting";
    public bool SetSelectedSubtitle { get; set; }
    public string? DefaultSubtitleLanguage { get; set; }
    public bool NeedsServer { get; set; }
}

public sealed class PlexPinResponse
{
    public long PinId { get; set; }
    public required string Code { get; set; }
    public required string AuthUrl { get; set; }
}

public sealed class PlexPollResponse
{
    public required string Status { get; set; }
    public string? Message { get; set; }
    public string? Username { get; set; }
}

public sealed class PlexServerResponse
{
    public required string Name { get; set; }
    public required string MachineIdentifier { get; set; }
    public required List<PlexConnectionResponse> Connections { get; set; }
}

public sealed class PlexConnectionResponse
{
    public required string Uri { get; set; }
    public bool Local { get; set; }
    public bool Relay { get; set; }
    public int LatencyMs { get; set; }
}

public sealed class PlexServerRequest
{
    public string? MachineIdentifier { get; set; }
    public string? Name { get; set; }
    public required string Url { get; set; }
}

public sealed class PlexTokenRequest
{
    public required string Url { get; set; }
    public required string Token { get; set; }
}

public sealed class PlexTestResponse
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
}

public sealed class PlexLibrariesResponse
{
    public required List<PlexLibraryResponse> Libraries { get; set; }
    public bool Running { get; set; }
}

public sealed class PlexLibraryResponse
{
    public required string Key { get; set; }
    public required string Type { get; set; }
    public required string Title { get; set; }
    public bool Scheduled { get; set; }
}
