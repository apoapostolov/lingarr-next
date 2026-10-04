using Lingarr.Core;
using Lingarr.Server.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services;

public class LingarrApiService : ILingarrApiService
{
    private const string ForkOwner = "apoapostolov";
    private const string ForkRepository = "lingarr-next";
    private const string GitHubApiVersion = "2022-11-28";
    private const string CacheKeyLatestVersion = "ForkRepository_LatestVersion";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LingarrApiService> _logger;
    private readonly IMemoryCache _cache;

    public LingarrApiService(
        IHttpClientFactory httpClientFactory,
        ILogger<LingarrApiService> logger,
        IMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cache = cache;
    }

    public async Task<string?> GetLatestVersion()
    {
        // Check cache first
        if (_cache.TryGetValue(CacheKeyLatestVersion, out string? cachedVersion))
        {
            _logger.LogDebug("Returning cached version information from Lingarr Next API");
            return cachedVersion;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", $"{LingarrVersion.Name}/{LingarrVersion.Number}");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", GitHubApiVersion);

            var releaseVersion = await GetLatestReleaseVersion(httpClient);
            var latestVersion = releaseVersion ?? await GetLatestTagVersion(httpClient);

            if (latestVersion is null)
            {
                _logger.LogWarning(
                    "No semantic version release or tag was found in {Owner}/{Repository}",
                    ForkOwner,
                    ForkRepository);
                return null;
            }

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromHours(24));
            _cache.Set(CacheKeyLatestVersion, latestVersion, cacheOptions);

            _logger.LogInformation(
                "Retrieved latest version {Version} from {Owner}/{Repository}",
                latestVersion,
                ForkOwner,
                ForkRepository);
            return latestVersion;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to fetch the latest version from {Owner}/{Repository}",
                ForkOwner,
                ForkRepository);
            return null;
        }
    }

    private static async Task<string?> GetLatestReleaseVersion(HttpClient httpClient)
    {
        var response = await httpClient.GetAsync(
            $"https://api.github.com/repos/{ForkOwner}/{ForkRepository}/releases/latest");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
        return NormalizeSemanticVersion(release?.TagName);
    }

    private static async Task<string?> GetLatestTagVersion(HttpClient httpClient)
    {
        var response = await httpClient.GetAsync(
            $"https://api.github.com/repos/{ForkOwner}/{ForkRepository}/tags?per_page=100");
        response.EnsureSuccessStatusCode();

        var tags = await response.Content.ReadFromJsonAsync<List<GitHubTag>>() ?? [];

        return tags
            .Select(tag => NormalizeSemanticVersion(tag.Name))
            .Where(version => version is not null)
            .Select(version => new
            {
                Text = version!,
                Parsed = Version.Parse(version!)
            })
            .OrderByDescending(version => version.Parsed)
            .Select(version => version.Text)
            .FirstOrDefault();
    }

    private static string? NormalizeSemanticVersion(string? value)
    {
        var normalized = value?.Trim().TrimStart('v');
        return Version.TryParse(normalized, out _) ? normalized : null;
    }

    private sealed class GitHubRelease
    {
        [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
        public string? TagName { get; init; }
    }

    private sealed class GitHubTag
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; init; }
    }
}
