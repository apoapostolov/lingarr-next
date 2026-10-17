using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Interfaces.Services;

public interface IPlexClient
{
    Task<PlexPin> CreatePinAsync(string clientId, CancellationToken cancellationToken);

    Task<string?> PollPinAsync(long pinId, string clientId, CancellationToken cancellationToken);

    Task<PlexAccount?> GetUserAsync(string token, string clientId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlexDiscoveredServer>> GetOwnedServersAsync(
        string token,
        string clientId,
        CancellationToken cancellationToken);

    Task<PlexProbe> ProbeAsync(
        string url,
        string token,
        string clientId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SearchRatingKeysAsync(
        string baseUrl,
        string token,
        string clientId,
        string query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlexLibrarySection>> GetLibrariesAsync(
        string baseUrl,
        string token,
        string clientId,
        CancellationToken cancellationToken);

    Task<string> GetLibraryPageAsync(
        string baseUrl,
        string token,
        string clientId,
        string sectionKey,
        int metadataType,
        int start,
        int size,
        CancellationToken cancellationToken);

    Task<string> GetMetadataAsync(
        string baseUrl,
        string token,
        string clientId,
        string ratingKey,
        CancellationToken cancellationToken);

    Task RefreshMetadataAsync(
        string baseUrl,
        string token,
        string clientId,
        string ratingKey,
        CancellationToken cancellationToken);

    Task RefreshSectionAsync(
        string baseUrl,
        string token,
        string clientId,
        string sectionId,
        CancellationToken cancellationToken);

    Task RefreshSectionPathAsync(
        string baseUrl,
        string token,
        string clientId,
        string sectionId,
        string directory,
        CancellationToken cancellationToken);

    Task SetSelectedSubtitleAsync(
        string baseUrl,
        string token,
        string clientId,
        long partId,
        long streamId,
        bool allParts,
        CancellationToken cancellationToken);

    Task UploadSubtitleAsync(
        string baseUrl,
        string token,
        string clientId,
        string ratingKey,
        string fileName,
        string format,
        string language,
        byte[] content,
        CancellationToken cancellationToken);
}
