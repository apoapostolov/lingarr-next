using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Integration.Bazarr;

public class BazarrService : IBazarrService
{
    private const int Polls = 15;
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);

    private readonly ISettingService _settings;
    private readonly IHttpClientFactory _http;
    private readonly ISubtitleService _subtitles;
    private readonly LingarrDbContext _db;
    private readonly ILogger<BazarrService> _logger;

    public BazarrService(
        ISettingService settings,
        IHttpClientFactory http,
        ISubtitleService subtitles,
        LingarrDbContext db,
        ILogger<BazarrService> logger)
    {
        _settings = settings;
        _http = http;
        _subtitles = subtitles;
        _db = db;
        _logger = logger;
    }

    public async Task<bool> IsEnabled()
    {
        if (!string.Equals(await _settings.GetSetting(SettingKeys.Integration.BazarrEnabled), "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var (url, key) = await Credentials();
        return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key);
    }

    public async Task<(bool Ok, string Message)> Test(CancellationToken cancellationToken)
    {
        var (url, key) = await Credentials();
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            return (false, "Enter the Bazarr address and API key.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Api(url, "/api/system/status"));
            request.Headers.TryAddWithoutValidation("X-API-KEY", key);
            using var response = await Client().SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? (true, "Bazarr connection succeeded.")
                : (false, $"Bazarr connection failed. The server returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogInformation(exception, "Bazarr test failed for {Url}.", url);
            return (false, "Bazarr connection failed.");
        }
    }

    public async Task<bool> TryEnsureSource(
        IMedia media,
        MediaType mediaType,
        IReadOnlySet<string> sourceLanguages,
        CancellationToken cancellationToken)
    {
        if (!await IsEnabled() || media.Path == null || media.FileName == null || sourceLanguages.Count == 0)
        {
            return false;
        }

        var minimum = await MinimumScore();
        var (url, key) = await Credentials();
        try
        {
            var downloaded = mediaType switch
            {
                MediaType.Movie when media is Movie movie =>
                    await DownloadMovie(url, key, movie.RadarrId, sourceLanguages, minimum, cancellationToken),
                MediaType.Episode =>
                    await DownloadEpisode(url, key, media.Id, sourceLanguages, minimum, cancellationToken),
                _ => false
            };
            if (!downloaded)
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogInformation(exception, "Bazarr could not search subtitles for {File}.", media.FileName);
            return false;
        }

        for (var attempt = 0; attempt < Polls; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(PollDelay, cancellationToken);
            var found = await _subtitles.GetSubtitles(media.Path, media.FileName);
            if (_subtitles.SelectSourceSubtitle(found, sourceLanguages.ToHashSet(), "false") != null)
            {
                _logger.LogInformation("Bazarr saved a source subtitle for {File}.", media.FileName);
                return true;
            }
        }

        _logger.LogInformation("Bazarr accepted the download for {File}, but the subtitle is not on disk yet.", media.FileName);
        return false;
    }

    private async Task<bool> DownloadMovie(
        string url,
        string key,
        int radarrId,
        IReadOnlySet<string> sourceLanguages,
        int minimum,
        CancellationToken cancellationToken)
    {
        var results = await Get<BazarrList>(url, key, $"/api/providers/movies?radarrid={radarrId}", cancellationToken);
        var best = BazarrSubtitleChoice.PickBest(Read(results), sourceLanguages, minimum);
        if (best == null)
        {
            _logger.LogInformation("Bazarr has no source subtitle for Radarr movie {Id} at score {Minimum} or higher.", radarrId, minimum);
            return false;
        }

        await Post(url, key, "/api/providers/movies", new
        {
            radarrid = radarrId,
            hi = Flag(best.HearingImpaired),
            forced = Flag(best.Forced),
            original_format = Flag(best.OriginalFormat),
            provider = best.Provider,
            subtitle = best.SubtitleId
        }, cancellationToken);
        _logger.LogInformation(
            "Asked Bazarr to download {Language} from {Provider} at score {Score} for Radarr movie {Id}.",
            best.Language, best.Provider, best.Score, radarrId);
        return true;
    }

    private async Task<bool> DownloadEpisode(
        string url,
        string key,
        int episodeId,
        IReadOnlySet<string> sourceLanguages,
        int minimum,
        CancellationToken cancellationToken)
    {
        var ids = await _db.Episodes
            .Where(episode => episode.Id == episodeId)
            .Select(episode => new { episode.SonarrId, SeriesId = episode.Season.Show.SonarrId })
            .FirstOrDefaultAsync(cancellationToken);
        if (ids == null)
        {
            return false;
        }

        var results = await Get<BazarrList>(url, key, $"/api/providers/episodes?episodeid={ids.SonarrId}", cancellationToken);
        var best = BazarrSubtitleChoice.PickBest(Read(results), sourceLanguages, minimum);
        if (best == null)
        {
            _logger.LogInformation(
                "Bazarr has no source subtitle for Sonarr episode {Id} at score {Minimum} or higher.",
                ids.SonarrId, minimum);
            return false;
        }

        await Post(url, key, "/api/providers/episodes", new
        {
            seriesid = ids.SeriesId,
            episodeid = ids.SonarrId,
            hi = Flag(best.HearingImpaired),
            forced = Flag(best.Forced),
            original_format = Flag(best.OriginalFormat),
            provider = best.Provider,
            subtitle = best.SubtitleId
        }, cancellationToken);
        _logger.LogInformation(
            "Asked Bazarr to download {Language} from {Provider} at score {Score} for Sonarr episode {Id}.",
            best.Language, best.Provider, best.Score, ids.SonarrId);
        return true;
    }

    private async Task<T?> Get<T>(string url, string key, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api(url, path));
        request.Headers.TryAddWithoutValidation("X-API-KEY", key);
        using var response = await Client().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Bazarr {Path} answered {Status}.", path, (int)response.StatusCode);
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private async Task Post(string url, string key, string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Api(url, path));
        request.Headers.TryAddWithoutValidation("X-API-KEY", key);
        request.Content = JsonContent.Create(body);
        using var response = await Client().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Bazarr download answered {(int)response.StatusCode}.");
        }
    }

    private async Task<(string Url, string Key)> Credentials()
    {
        var url = (await _settings.GetSetting(SettingKeys.Integration.BazarrUrl) ?? "").Trim().TrimEnd('/');
        var key = await _settings.GetEncryptedSetting(SettingKeys.Integration.BazarrApiKey) ?? "";
        return (url, key);
    }

    private async Task<int> MinimumScore()
    {
        var raw = await _settings.GetSetting(SettingKeys.Integration.BazarrMinimumScore);
        return int.TryParse(raw, out var score) ? Math.Clamp(score, 0, 100) : 70;
    }

    private HttpClient Client() => _http.CreateClient("bazarr");

    private static string Api(string url, string path) => url + path;

    private static string Flag(bool value) => value ? "True" : "False";

    private static IEnumerable<BazarrCandidate> Read(BazarrList? list)
    {
        if (list?.Data == null)
        {
            yield break;
        }

        foreach (var item in list.Data)
        {
            var score = item.Score > 0 ? item.Score : item.OriginalScore;
            yield return new BazarrCandidate(
                item.Language ?? "",
                score,
                IsTrue(item.Forced),
                IsTrue(item.HearingImpaired),
                item.Provider ?? "",
                item.Subtitle ?? "",
                !IsFalse(item.OriginalFormat));
        }
    }

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(string? value) =>
        string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    private sealed class BazarrList
    {
        [JsonPropertyName("data")]
        public List<BazarrHit>? Data { get; set; }
    }

    private sealed class BazarrHit
    {
        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("orig_score")]
        public int OriginalScore { get; set; }

        [JsonPropertyName("forced")]
        public string? Forced { get; set; }

        [JsonPropertyName("hearing_impaired")]
        public string? HearingImpaired { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("subtitle")]
        public string? Subtitle { get; set; }

        [JsonPropertyName("original_format")]
        public string? OriginalFormat { get; set; }
    }
}
