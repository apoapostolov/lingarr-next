using Lingarr.Contracts.Plugins;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Plugins;

namespace Lingarr.Server.Services.SubtitleDownload;

public sealed class SubtitlePlacementService : ISubtitlePlacementService
{
    private readonly ISubtitleService _subtitles;
    private readonly MediaLibraryRefreshService _refresh;
    private readonly IPlexSubtitleSelector _plex;
    private readonly PluginSignals _signals;
    private readonly ILogger<SubtitlePlacementService> _logger;

    public SubtitlePlacementService(
        ISubtitleService subtitles,
        MediaLibraryRefreshService refresh,
        IPlexSubtitleSelector plex,
        PluginSignals signals,
        ILogger<SubtitlePlacementService> logger)
    {
        _subtitles = subtitles;
        _refresh = refresh;
        _plex = plex;
        _signals = signals;
        _logger = logger;
    }

    public async Task<string?> PlaceAsync(
        IMedia media,
        SubtitleDownloadHit hit,
        string language,
        IReadOnlyList<Subtitles> existing,
        CancellationToken cancellationToken)
    {
        if (media.Path == null
            || media.FileName == null
            || hit.Content.Length == 0
            || SubtitleBytes.IsHtml(hit.Content)
            || SubtitleBytes.MismatchesLanguage(hit.Content, language))
        {
            if (hit.Content.Length > 0 && SubtitleBytes.IsHtml(hit.Content))
            {
                _logger.LogInformation(
                    "{Provider} returned a web page instead of a {Language} subtitle for {Title}.",
                    hit.Provider,
                    language,
                    media.Title);
            }
            else if (hit.Content.Length > 0 && SubtitleBytes.MismatchesLanguage(hit.Content, language))
            {
                _logger.LogInformation(
                    "{Provider} returned a subtitle that is not {Language} for {Title}.",
                    hit.Provider,
                    language,
                    media.Title);
            }

            return null;
        }

        Directory.CreateDirectory(media.Path);
        var original = Path.Combine(media.Path, media.FileName + ".srt");
        var outputPath = _subtitles.CreateFilePath(original, language, "");
        if (!string.IsNullOrWhiteSpace(hit.Extension))
        {
            outputPath = Path.ChangeExtension(outputPath, hit.Extension);
        }

        await File.WriteAllBytesAsync(outputPath, hit.Content, cancellationToken);
        _logger.LogInformation(
            "Placed a {Language} subtitle from {Provider} at {Path}.",
            language,
            hit.Provider,
            outputPath);
        return outputPath;
    }

    public async Task NotifyAsync(
        IMedia media,
        MediaType mediaType,
        string subtitlePath,
        string language,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(media.Path))
        {
            await _refresh.RefreshFoldersAsync([media.Path], cancellationToken);
        }

        var request = new TranslationRequest
        {
            MediaId = media.Id,
            Title = media.Title,
            SourceLanguage = language,
            TargetLanguage = language,
            SubtitleToTranslate = subtitlePath,
            TranslatedSubtitle = subtitlePath,
            MediaType = mediaType,
            Status = TranslationStatus.Completed
        };

        try
        {
            await _plex.ApplyTranslatedSubtitleAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Plex subtitle update failed for downloaded {Language} subtitle on {Title}.",
                language,
                media.Title);
        }

        await _signals.SelectOnServersAsync(
            new MediaItemRef
            {
                Title = media.Title,
                Path = media.Path,
                FileName = media.FileName,
                SubtitlePath = subtitlePath,
                Language = language
            },
            cancellationToken);

        await _signals.NotifyAsync(
            new PluginNotice
            {
                Succeeded = true,
                Title = media.Title,
                Detail = $"Downloaded {language} subtitle.",
                Path = subtitlePath
            },
            cancellationToken);
    }
}
