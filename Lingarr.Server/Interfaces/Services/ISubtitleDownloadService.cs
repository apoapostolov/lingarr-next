using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.SubtitleDownload;

namespace Lingarr.Server.Interfaces.Services;

public interface ISubtitleDownloadGateway
{
    Task<SubtitleDownloadHit?> DownloadAsync(
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken);
}

public interface ISubtitleDownloadService
{
    Task<bool> IsEnabledAsync();

    Task<SubtitleDownloadHit?> TryDownloadAsync(
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> DownloadLanguagesAsync(
        IMedia media,
        MediaType mediaType,
        IReadOnlyCollection<string> languages,
        IReadOnlyList<Subtitles> existing,
        bool overwrite,
        CancellationToken cancellationToken);
}

public interface ISubtitlePlacementService
{
    Task<string?> PlaceAsync(
        IMedia media,
        SubtitleDownloadHit hit,
        string language,
        IReadOnlyList<Subtitles> existing,
        CancellationToken cancellationToken);

    Task NotifyAsync(
        IMedia media,
        MediaType mediaType,
        string subtitlePath,
        string language,
        CancellationToken cancellationToken);
}

public interface IForceTranslationService
{
    Task<ForceTranslateResult> ForceAsync(
        int mediaId,
        MediaType mediaType,
        bool overwrite,
        CancellationToken cancellationToken);

    Task<ForceTranslateResult> DownloadAsync(
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken);
}
