using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.Plex;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services.Integration.Plex;

public sealed class PlexAuthService : IPlexAuthService
{
    private const string CachePrefix = "plex-pin:";
    private readonly ISettingService _settings;
    private readonly IPlexClient _plex;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PlexAuthService> _logger;

    private sealed record PendingPin(string Code, string ClientId);

    public PlexAuthService(
        ISettingService settings,
        IPlexClient plex,
        IMemoryCache cache,
        ILogger<PlexAuthService> logger)
    {
        _settings = settings;
        _plex = plex;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PlexStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await PlexCredentials.ResolveAsync(_settings);
        var username = await _settings.GetSetting(SettingKeys.MediaServers.PlexUsername);
        var serverName = await _settings.GetSetting(SettingKeys.MediaServers.PlexServerName);
        var authMethod = await _settings.GetSetting(SettingKeys.MediaServers.PlexAuthMethod);
        var language = await _settings.GetSetting(SettingKeys.MediaServers.PlexDefaultSubtitleLanguage);
        var selected = await _settings.GetSetting(SettingKeys.MediaServers.PlexSetSelectedSubtitle);
        var connected = !string.IsNullOrWhiteSpace(credentials.Token);
        return new PlexStatusResponse
        {
            Connected = connected,
            Username = string.IsNullOrWhiteSpace(username) ? null : username,
            ServerName = string.IsNullOrWhiteSpace(serverName) ? null : serverName,
            ServerUrl = credentials.Url,
            AuthMethod = string.IsNullOrWhiteSpace(authMethod) ? null : authMethod,
            Source = credentials.Source,
            SetSelectedSubtitle = string.Equals(selected, "true", StringComparison.OrdinalIgnoreCase),
            DefaultSubtitleLanguage = string.IsNullOrWhiteSpace(language) ? null : language,
            NeedsServer = connected && string.IsNullOrWhiteSpace(credentials.Url)
        };
    }

    public async Task<PlexPinResponse> StartPinAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await PlexCredentials.ResolveAsync(_settings);
        var pin = await _plex.CreatePinAsync(credentials.ClientId, cancellationToken);
        _cache.Set(
            CachePrefix + pin.Id,
            new PendingPin(pin.Code, credentials.ClientId),
            TimeSpan.FromMinutes(30));
        return new PlexPinResponse
        {
            PinId = pin.Id,
            Code = pin.Code,
            AuthUrl = PlexMatcher.BuildAuthUrl(credentials.ClientId, pin.Code)
        };
    }

    public async Task<PlexPollResponse> PollPinAsync(long pinId, CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue(CachePrefix + pinId, out PendingPin? pending) || pending == null)
        {
            return new PlexPollResponse
            {
                Status = "expired",
                Message = "Plex sign-in expired. Start again."
            };
        }

        string? token;
        try
        {
            token = await _plex.PollPinAsync(pinId, pending.ClientId, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _cache.Remove(CachePrefix + pinId);
            return new PlexPollResponse
            {
                Status = "expired",
                Message = "Plex sign-in expired. Start again."
            };
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new PlexPollResponse { Status = "pending" };
        }

        var account = await _plex.GetUserAsync(token, pending.ClientId, cancellationToken);
        if (account == null)
        {
            _logger.LogWarning("Plex rejected the sign-in token for pin {PinId}.", pinId);
            return new PlexPollResponse
            {
                Status = "invalid",
                Message = "Plex rejected the sign-in token."
            };
        }

        await _settings.SetEncryptedSetting(SettingKeys.MediaServers.PlexToken, token);
        await _settings.SetSetting(SettingKeys.MediaServers.PlexAuthMethod, "oauth");
        await _settings.UpsertSetting(SettingKeys.MediaServers.PlexIgnoreEnvironment, "false");
        await _settings.SetSetting(SettingKeys.MediaServers.PlexUsername, account.Username ?? "");
        await _settings.SetSetting(SettingKeys.MediaServers.PlexUrl, "");
        await _settings.SetSetting(SettingKeys.MediaServers.PlexServerName, "");
        await _settings.SetSetting(SettingKeys.MediaServers.PlexServerMachineId, "");
        _cache.Remove(CachePrefix + pinId);
        return new PlexPollResponse
        {
            Status = "connected",
            Username = account.Username
        };
    }

    public async Task<IReadOnlyList<PlexServerResponse>> GetServersAsync(
        CancellationToken cancellationToken = default)
    {
        var credentials = await PlexCredentials.ResolveAsync(_settings);
        if (string.IsNullOrWhiteSpace(credentials.Token))
        {
            return [];
        }

        var discovered = await _plex.GetOwnedServersAsync(
            credentials.Token,
            credentials.ClientId,
            cancellationToken);
        var servers = new List<PlexServerResponse>();
        foreach (var server in discovered)
        {
            var reachable = new List<PlexReachableConnection>();
            foreach (var connection in server.Connections)
            {
                var started = DateTime.UtcNow;
                var probe = await _plex.ProbeAsync(
                    connection.Uri,
                    credentials.Token,
                    credentials.ClientId,
                    cancellationToken);
                if (!probe.Ok)
                {
                    continue;
                }

                reachable.Add(new PlexReachableConnection(
                    connection.Uri,
                    connection.Local,
                    connection.Relay,
                    (int)(DateTime.UtcNow - started).TotalMilliseconds));
            }

            var ranked = PlexMatcher.Rank(reachable);
            if (ranked.Count == 0)
            {
                continue;
            }

            servers.Add(new PlexServerResponse
            {
                Name = server.Name,
                MachineIdentifier = server.MachineIdentifier,
                Connections = ranked.Select(connection => new PlexConnectionResponse
                {
                    Uri = connection.Uri,
                    Local = connection.Local,
                    Relay = connection.Relay,
                    LatencyMs = connection.LatencyMs
                }).ToList()
            });
        }

        return servers;
    }

    public async Task<PlexStatusResponse> SelectServerAsync(
        PlexServerRequest request,
        CancellationToken cancellationToken = default)
    {
        var credentials = await PlexCredentials.ResolveAsync(_settings);
        if (string.IsNullOrWhiteSpace(credentials.Token))
        {
            throw new InvalidOperationException("Sign in to Plex before choosing a server.");
        }

        var probe = await _plex.ProbeAsync(request.Url, credentials.Token, credentials.ClientId, cancellationToken);
        if (!probe.Ok)
        {
            throw new InvalidOperationException(probe.Error ?? "Lingarr could not open that Plex address.");
        }

        await _settings.SetSetting(SettingKeys.MediaServers.PlexUrl, request.Url.TrimEnd('/'));
        await _settings.UpsertSetting(SettingKeys.MediaServers.PlexIgnoreEnvironment, "false");
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            await _settings.SetSetting(SettingKeys.MediaServers.PlexServerName, request.Name);
        }

        if (!string.IsNullOrWhiteSpace(request.MachineIdentifier))
        {
            await _settings.SetSetting(
                SettingKeys.MediaServers.PlexServerMachineId,
                request.MachineIdentifier);
        }

        return await GetStatusAsync(cancellationToken);
    }

    public async Task<PlexStatusResponse> SaveTokenAsync(
        PlexTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var credentials = await PlexCredentials.ResolveAsync(_settings);
        var probe = await _plex.ProbeAsync(request.Url, request.Token, credentials.ClientId, cancellationToken);
        if (!probe.Ok)
        {
            throw new InvalidOperationException(probe.Error ?? "Lingarr could not open that Plex address.");
        }

        await _settings.SetSetting(SettingKeys.MediaServers.PlexUrl, request.Url.TrimEnd('/'));
        await _settings.SetEncryptedSetting(SettingKeys.MediaServers.PlexToken, request.Token);
        await _settings.SetSetting(SettingKeys.MediaServers.PlexAuthMethod, "token");
        await _settings.UpsertSetting(SettingKeys.MediaServers.PlexIgnoreEnvironment, "false");
        var account = await _plex.GetUserAsync(request.Token, credentials.ClientId, cancellationToken);
        await _settings.SetSetting(SettingKeys.MediaServers.PlexUsername, account?.Username ?? "");
        if (account == null)
        {
            await _settings.SetSetting(SettingKeys.MediaServers.PlexServerName, "");
            await _settings.SetSetting(SettingKeys.MediaServers.PlexServerMachineId, "");
        }

        return await GetStatusAsync(cancellationToken);
    }

    public async Task<PlexTestResponse> TestAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await PlexCredentials.ResolveAsync(_settings);
        if (string.IsNullOrWhiteSpace(credentials.Url) || string.IsNullOrWhiteSpace(credentials.Token))
        {
            return new PlexTestResponse
            {
                Ok = false,
                Message = "Enter the Plex address and token."
            };
        }

        var probe = await _plex.ProbeAsync(
            credentials.Url,
            credentials.Token,
            credentials.ClientId,
            cancellationToken);
        return new PlexTestResponse
        {
            Ok = probe.Ok,
            Message = probe.Ok ? "Plex connection succeeded." : probe.Error
        };
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _settings.SetSetting(SettingKeys.MediaServers.PlexToken, "");
        await _settings.SetSetting(SettingKeys.MediaServers.PlexUsername, "");
        await _settings.SetSetting(SettingKeys.MediaServers.PlexServerName, "");
        await _settings.SetSetting(SettingKeys.MediaServers.PlexServerMachineId, "");
        await _settings.SetSetting(SettingKeys.MediaServers.PlexAuthMethod, "");
        await _settings.UpsertSetting(SettingKeys.MediaServers.PlexIgnoreEnvironment, "true");
    }
}
