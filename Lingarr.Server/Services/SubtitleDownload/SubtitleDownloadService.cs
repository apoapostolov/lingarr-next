using System.Text.RegularExpressions;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Integration.Bazarr;

namespace Lingarr.Server.Services.SubtitleDownload;

public sealed class SubtitleDownloadService : ISubtitleDownloadService
{
    private static readonly Regex YearRegex = new(@"\((\d{4})\)", RegexOptions.Compiled);
    private static readonly string[] VideoExtensions = [".mkv", ".mp4", ".avi", ".m4v", ".ts", ".mov"];

    private readonly ISettingService _settings;
    private readonly ISubtitleDownloadGateway _gateway;
    private readonly ISubtitlePlacementService _placement;
    private readonly ILogger<SubtitleDownloadService> _logger;

    public SubtitleDownloadService(
        ISettingService settings,
        ISubtitleDownloadGateway gateway,
        ISubtitlePlacementService placement,
        ILogger<SubtitleDownloadService> logger)
    {
        _settings = settings;
        _gateway = gateway;
        _placement = placement;
        _logger = logger;
    }

    public async Task<bool> IsEnabledAsync()
    {
        var raw = await _settings.GetSetting(SettingKeys.SubtitleDownload.Enabled);
        return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SubtitleDownloadHit?> TryDownloadAsync(
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await IsEnabledAsync())
        {
            return null;
        }

        var chain = SubtitleDownloadChain.Parse(
            await _settings.GetSetting(SettingKeys.SubtitleDownload.Providers));
        foreach (var entry in chain)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = SubtitleDownloadCatalog.Find(entry.Provider);
            if (provider == null)
            {
                continue;
            }

            var hit = await _gateway.DownloadAsync(provider, query, cancellationToken);
            if (hit != null)
            {
                _logger.LogInformation(
                    "{Provider} supplied a {Language} subtitle for {Title}.",
                    provider.DisplayName,
                    query.Language,
                    query.Title);
                return hit;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<string>> DownloadLanguagesAsync(
        IMedia media,
        MediaType mediaType,
        IReadOnlyCollection<string> languages,
        IReadOnlyList<Subtitles> existing,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await IsEnabledAsync() || languages.Count == 0 || media.Path == null || media.FileName == null)
        {
            return [];
        }

        var placed = new List<string>();
        if (!Directory.Exists(media.Path))
        {
            _logger.LogWarning("Subtitle downloads skipped: media directory unavailable at {Path}.", media.Path);
            return placed;
        }

        var current = existing.ToList();
        foreach (var language in languages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!overwrite && HasLanguage(current, language))
            {
                continue;
            }

            var query = BuildQuery(media, mediaType, language);
            var hit = await TryDownloadAsync(query, cancellationToken);
            if (hit == null)
            {
                continue;
            }

            string? path;
            try
            {
                path = await _placement.PlaceAsync(media, hit, language, current, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception,
                    "Subtitle downloads stopped: media directory unavailable or not writable at {Path}.", media.Path);
                break;
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            await _placement.NotifyAsync(media, mediaType, path, language, cancellationToken);
            current.Add(new Subtitles
            {
                Path = path,
                FileName = media.FileName,
                Language = language,
                Format = Path.GetExtension(path)
            });
            placed.Add(language);
        }

        return placed;
    }

    internal static SubtitleDownloadQuery BuildQuery(IMedia media, MediaType mediaType, string language)
    {
        string? series = null;
        int? season = null;
        int? episode = null;
        if (media is Episode item)
        {
            series = item.Season?.Show?.Title;
            season = item.Season?.SeasonNumber;
            episode = item.EpisodeNumber;
        }

        int? year = null;
        foreach (var text in new[] { media.Title, media.FileName, media.Path })
        {
            var match = YearRegex.Match(text ?? "");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed) && parsed is >= 1900 and <= 2100)
            {
                year = parsed;
                break;
            }
        }

        return new SubtitleDownloadQuery
        {
            MediaType = mediaType,
            MediaId = media.Id,
            Title = media.Title ?? "",
            SeriesTitle = series,
            Year = year,
            SeasonNumber = season,
            EpisodeNumber = episode,
            Language = language,
            FileName = media.FileName,
            Directory = media.Path,
            VideoPath = ResolveVideoPath(media)
        };
    }

    internal static bool HasLanguage(IEnumerable<Subtitles> subtitles, string language)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { language };
        return subtitles.Any(subtitle =>
            BazarrSubtitleChoice.LanguageMatches(subtitle.Language, wanted)
            && SubtitleBytes.IsUsableSidecar(subtitle.Path, subtitle.Language ?? language));
    }

    private static string? ResolveVideoPath(IMedia media)
    {
        if (media.Path == null || media.FileName == null)
        {
            return null;
        }

        var combined = Path.Combine(media.Path, media.FileName);
        if (File.Exists(combined))
        {
            return combined;
        }

        foreach (var extension in VideoExtensions)
        {
            var candidate = combined + extension;
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return combined;
    }
}
