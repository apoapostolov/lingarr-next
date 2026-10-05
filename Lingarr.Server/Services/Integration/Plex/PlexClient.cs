using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Services.Integration.Plex;

public sealed class PlexClient : IPlexClient
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private readonly IHttpClientFactory _httpClientFactory;

    public PlexClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PlexPin> CreatePinAsync(string clientId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://plex.tv/api/v2/pins")
        {
            Content = new StringContent("{\"strong\":true}", Encoding.UTF8, "application/json")
        };
        ApplyHeaders(request, clientId, token: null);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Plex could not start sign-in ({(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(body);
        var idValue = document.RootElement.GetProperty("id");
        var id = idValue.ValueKind == JsonValueKind.String
            ? long.Parse(idValue.GetString()!)
            : idValue.GetInt64();
        var code = document.RootElement.GetProperty("code").GetString();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new HttpRequestException("Plex did not return a sign-in code.");
        }

        return new PlexPin(id, code);
    }

    public async Task<string?> PollPinAsync(long pinId, string clientId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://plex.tv/api/v2/pins/{pinId}");
        ApplyHeaders(request, clientId, token: null);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new HttpRequestException("Plex sign-in expired.", null, response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Plex sign-in check failed ({(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("authToken", out var token) || token.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var value = token.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public async Task<PlexAccount?> GetUserAsync(
        string token,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://plex.tv/api/v2/user");
        ApplyHeaders(request, clientId, token);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Plex account lookup failed ({(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return new PlexAccount(
            ReadString(document.RootElement, "username"),
            ReadString(document.RootElement, "email"));
    }

    public async Task<IReadOnlyList<PlexDiscoveredServer>> GetOwnedServersAsync(
        string token,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://plex.tv/api/v2/resources?includeHttps=1&includeRelay=1");
        ApplyHeaders(request, clientId, token);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Plex server list failed ({(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) || body.TrimStart().StartsWith('<'))
        {
            return ParseXmlServers(body);
        }

        return ParseJsonServers(body);
    }

    public async Task<PlexProbe> ProbeAsync(
        string url,
        string token,
        string clientId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, Join(url, "/identity"));
            ApplyHeaders(request, clientId, token);
            using var response = await SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return new PlexProbe(true, (int)response.StatusCode, null);
            }

            return new PlexProbe(
                false,
                (int)response.StatusCode,
                response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Plex connection failed. The token was rejected."
                    : $"Plex connection failed. The server returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PlexProbe(false, null, "Plex connection failed.");
        }
        catch (HttpRequestException)
        {
            return new PlexProbe(false, null, "Plex connection failed. The server could not be reached.");
        }
    }

    public async Task<IReadOnlyList<string>> SearchRatingKeysAsync(
        string baseUrl,
        string token,
        string clientId,
        string query,
        CancellationToken cancellationToken)
    {
        var url = Join(baseUrl, "/hubs/search") + "?query=" + Uri.EscapeDataString(query);
        var body = await GetRequiredStringAsync(url, token, clientId, cancellationToken);
        return PlexMatcher.CollectRatingKeys(body);
    }

    public Task<string> GetMetadataAsync(
        string baseUrl,
        string token,
        string clientId,
        string ratingKey,
        CancellationToken cancellationToken)
    {
        var url = Join(baseUrl, $"/library/metadata/{Uri.EscapeDataString(ratingKey)}?includeGuids=1");
        return GetRequiredStringAsync(url, token, clientId, cancellationToken);
    }

    public async Task<IReadOnlyList<PlexLibrarySection>> GetLibrariesAsync(
        string baseUrl,
        string token,
        string clientId,
        CancellationToken cancellationToken)
    {
        var body = await GetRequiredStringAsync(
            Join(baseUrl, "/library/sections"),
            token,
            clientId,
            cancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("MediaContainer", out var container))
        {
            root = container;
        }

        if (!root.TryGetProperty("Directory", out var directories))
        {
            return [];
        }

        var sections = new List<PlexLibrarySection>();
        foreach (var directory in directories.EnumerateArray())
        {
            var key = directory.TryGetProperty("key", out var keyValue) ? keyValue.GetString() : null;
            var type = directory.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            var title = directory.TryGetProperty("title", out var titleValue) ? titleValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            sections.Add(new PlexLibrarySection(key, type, title ?? type));
        }

        return sections;
    }

    public Task<string> GetLibraryPageAsync(
        string baseUrl,
        string token,
        string clientId,
        string sectionKey,
        int metadataType,
        int start,
        int size,
        CancellationToken cancellationToken)
    {
        var url = Join(baseUrl, $"/library/sections/{Uri.EscapeDataString(sectionKey)}/all")
            + $"?type={metadataType}&includeGuids=1&X-Plex-Container-Start={start}&X-Plex-Container-Size={size}";
        return GetRequiredStringAsync(url, token, clientId, cancellationToken);
    }

    public Task RefreshMetadataAsync(
        string baseUrl,
        string token,
        string clientId,
        string ratingKey,
        CancellationToken cancellationToken)
    {
        var url = Join(baseUrl, $"/library/metadata/{Uri.EscapeDataString(ratingKey)}/refresh");
        return SendEmptyAsync(HttpMethod.Put, url, token, clientId, cancellationToken);
    }

    public Task RefreshSectionPathAsync(
        string baseUrl,
        string token,
        string clientId,
        string sectionId,
        string directory,
        CancellationToken cancellationToken)
    {
        var url = Join(baseUrl, $"/library/sections/{Uri.EscapeDataString(sectionId)}/refresh")
            + "?path=" + Uri.EscapeDataString(directory);
        return SendEmptyAsync(HttpMethod.Get, url, token, clientId, cancellationToken);
    }

    public Task SetSelectedSubtitleAsync(
        string baseUrl,
        string token,
        string clientId,
        long partId,
        long streamId,
        bool allParts,
        CancellationToken cancellationToken)
    {
        var url = Join(baseUrl, $"/library/parts/{partId}")
            + $"?subtitleStreamID={streamId}";
        if (allParts)
        {
            url += "&allParts=1";
        }

        return SendEmptyAsync(HttpMethod.Put, url, token, clientId, cancellationToken);
    }

    public async Task UploadSubtitleAsync(
        string baseUrl,
        string token,
        string clientId,
        string ratingKey,
        string fileName,
        string format,
        string language,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var query = "?title=" + Uri.EscapeDataString(fileName)
            + "&format=" + Uri.EscapeDataString(format)
            + "&codec=" + Uri.EscapeDataString(format)
            + "&language=" + Uri.EscapeDataString(language);
        var url = Join(baseUrl, $"/library/metadata/{Uri.EscapeDataString(ratingKey)}/subtitles") + query;
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(content)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        ApplyHeaders(request, clientId, token);
        request.Headers.Accept.Clear();
        request.Headers.TryAddWithoutValidation("Accept", "text/plain, */*");
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Plex subtitle upload failed ({(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }
    }

    private async Task<string> GetRequiredStringAsync(
        string url,
        string token,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, clientId, token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Plex metadata request failed ({(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task SendEmptyAsync(
        HttpMethod method,
        string url,
        string token,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        ApplyHeaders(request, clientId, token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Plex request failed ({(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        return client.SendAsync(request, cancellationToken);
    }

    private static void ApplyHeaders(HttpRequestMessage request, string clientId, string? token)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Lingarr");
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", clientId);
        request.Headers.TryAddWithoutValidation("X-Plex-Device", "Lingarr");
        request.Headers.TryAddWithoutValidation("X-Plex-Platform", "Web");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation("X-Plex-Token", token);
        }
    }

    private static string Join(string baseUrl, string path)
    {
        return baseUrl.TrimEnd('/') + path;
    }

    private static IReadOnlyList<PlexDiscoveredServer> ParseJsonServers(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var servers = new List<PlexDiscoveredServer>();
        foreach (var device in root.EnumerateArray())
        {
            if (!string.Equals(ReadString(device, "provides"), "server", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ReadBool(device, "owned"))
            {
                continue;
            }

            var name = ReadString(device, "name");
            var machineId = ReadString(device, "clientIdentifier");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(machineId))
            {
                continue;
            }

            var connections = new List<PlexConnection>();
            if (device.TryGetProperty("connections", out var connectionList)
                && connectionList.ValueKind == JsonValueKind.Array)
            {
                foreach (var connection in connectionList.EnumerateArray())
                {
                    var uri = ReadString(connection, "uri");
                    if (string.IsNullOrWhiteSpace(uri))
                    {
                        continue;
                    }

                    connections.Add(new PlexConnection(
                        uri,
                        ReadBool(connection, "local"),
                        ReadBool(connection, "relay")));
                }
            }

            servers.Add(new PlexDiscoveredServer(name, machineId, connections));
        }

        return servers;
    }

    private static IReadOnlyList<PlexDiscoveredServer> ParseXmlServers(string body)
    {
        var document = XDocument.Parse(body);
        var servers = new List<PlexDiscoveredServer>();
        foreach (var device in document.Descendants("Device"))
        {
            if (!string.Equals((string?)device.Attribute("provides"), "server", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var owned = (string?)device.Attribute("owned");
            if (owned is not ("1" or "true"))
            {
                continue;
            }

            var name = (string?)device.Attribute("name");
            var machineId = (string?)device.Attribute("clientIdentifier");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(machineId))
            {
                continue;
            }

            var connections = device.Descendants("Connection")
                .Select(connection => new PlexConnection(
                    (string?)connection.Attribute("uri") ?? "",
                    (string?)connection.Attribute("local") is "1" or "true",
                    (string?)connection.Attribute("relay") is "1" or "true"))
                .Where(connection => !string.IsNullOrWhiteSpace(connection.Uri))
                .ToList();
            servers.Add(new PlexDiscoveredServer(name, machineId, connections));
        }

        return servers;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString() is "1" or "true",
            JsonValueKind.Number => value.TryGetInt32(out var number) && number == 1,
            _ => false
        };
    }
}
