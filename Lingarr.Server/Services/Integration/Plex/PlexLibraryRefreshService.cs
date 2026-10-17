using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Services.Integration.Plex;

public sealed class PlexLibraryRefreshOptions
{
    public TimeSpan Stagger { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ScanTimeout { get; init; } = TimeSpan.FromHours(3);
    public TimeSpan MinimumScanWait { get; init; } = TimeSpan.FromSeconds(15);
    public int PageSize { get; init; } = 100;
}

public sealed record PlexLibraryRefreshResult
{
    public int Libraries { get; init; }
    public int Selected { get; init; }
    public int AlreadySelected { get; init; }
    public int Missing { get; init; }
}

public sealed class PlexLibraryRefreshService
{
    private readonly ISettingService _settings;
    private readonly IPlexClient _plex;
    private readonly PlexLibraryRefreshOptions _options;
    private readonly ILogger<PlexLibraryRefreshService> _logger;

    public PlexLibraryRefreshService(
        ISettingService settings,
        IPlexClient plex,
        PlexLibraryRefreshOptions options,
        ILogger<PlexLibraryRefreshService> logger)
    {
        _settings = settings;
        _plex = plex;
        _options = options;
        _logger = logger;
    }

    public async Task<PlexLibrariesResponse> ListAsync(CancellationToken cancellationToken)
    {
        var credentials = await PlexCredentials.ResolveAsync(_settings);
        if (string.IsNullOrWhiteSpace(credentials.Url) || string.IsNullOrWhiteSpace(credentials.Token))
        {
            return new PlexLibrariesResponse { Libraries = [], Running = PlexLibraryRefreshJob.IsBusy() };
        }

        var sections = await _plex.GetLibrariesAsync(
            credentials.Url,
            credentials.Token,
            credentials.ClientId,
            cancellationToken);
        var enabled = ParseKeys(await _settings.GetSetting(SettingKeys.MediaServers.PlexLibraryRefreshKeys));
        var libraries = Order(sections)
            .Select(section => new PlexLibraryResponse
            {
                Key = section.Key,
                Type = section.Type,
                Title = section.Title,
                Scheduled = enabled.Contains(section.Key)
            })
            .ToList();
        return new PlexLibrariesResponse
        {
            Libraries = libraries,
            Running = PlexLibraryRefreshJob.IsBusy()
        };
    }

    public async Task<PlexLibraryRefreshResult> RefreshScheduledAsync(CancellationToken cancellationToken)
    {
        var credentials = await RequireCredentialsAsync();
        var sections = Order(await _plex.GetLibrariesAsync(
            credentials.Url!,
            credentials.Token!,
            credentials.ClientId,
            cancellationToken));
        var enabled = ParseKeys(await _settings.GetSetting(SettingKeys.MediaServers.PlexLibraryRefreshKeys));
        var selected = sections.Where(section => enabled.Contains(section.Key)).ToList();
        if (selected.Count == 0)
        {
            _logger.LogInformation("Plex library refresh ran with no libraries enabled.");
            return new PlexLibraryRefreshResult();
        }

        return await RefreshManyAsync(credentials, selected, cancellationToken);
    }

    public async Task<PlexLibraryRefreshResult> RefreshLibraryAsync(
        string sectionKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);
        var credentials = await RequireCredentialsAsync();
        var sections = await _plex.GetLibrariesAsync(
            credentials.Url!,
            credentials.Token!,
            credentials.ClientId,
            cancellationToken);
        var section = sections.FirstOrDefault(item =>
            string.Equals(item.Key, sectionKey, StringComparison.OrdinalIgnoreCase));
        if (section == null)
        {
            throw new InvalidOperationException("Plex has no library with that key.");
        }

        return await RefreshManyAsync(credentials, [section], cancellationToken);
    }

    internal static HashSet<string> ParseKeys(string? json)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return keys;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed == null)
            {
                return keys;
            }

            foreach (var key in parsed)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    keys.Add(key.Trim());
                }
            }
        }
        catch (JsonException)
        {
            return keys;
        }

        return keys;
    }

    internal static IReadOnlyList<PlexLibrarySection> Order(IEnumerable<PlexLibrarySection> sections)
    {
        return sections
            .OrderBy(section => TypeRank(section.Type))
            .ThenBy(section => section.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<PlexLibraryRefreshResult> RefreshManyAsync(
        ResolvedPlexCredentials credentials,
        IReadOnlyList<PlexLibrarySection> sections,
        CancellationToken cancellationToken)
    {
        var selected = 0;
        var already = 0;
        var missing = 0;
        for (var index = 0; index < sections.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = sections[index];
            var counts = await RefreshOneAsync(credentials, section, cancellationToken);
            selected += counts.Selected;
            already += counts.AlreadySelected;
            missing += counts.Missing;
            if (index < sections.Count - 1 && _options.Stagger > TimeSpan.Zero)
            {
                _logger.LogInformation(
                    "Waiting {Minutes} minutes before refreshing the next Plex library.",
                    _options.Stagger.TotalMinutes);
                await Task.Delay(_options.Stagger, cancellationToken);
            }
        }

        return new PlexLibraryRefreshResult
        {
            Libraries = sections.Count,
            Selected = selected,
            AlreadySelected = already,
            Missing = missing
        };
    }

    private async Task<PlexLibraryRefreshResult> RefreshOneAsync(
        ResolvedPlexCredentials credentials,
        PlexLibrarySection section,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Refreshing Plex library {Title} ({Key}).", section.Title, section.Key);
        await _plex.RefreshSectionAsync(
            credentials.Url!,
            credentials.Token!,
            credentials.ClientId,
            section.Key,
            cancellationToken);
        await WaitForScanAsync(credentials, section.Key, cancellationToken);

        var language = await _settings.GetSetting(SettingKeys.MediaServers.PlexDefaultSubtitleLanguage);
        var metadataType = MetadataType(section.Type);
        if (string.IsNullOrWhiteSpace(language) || metadataType == null)
        {
            return new PlexLibraryRefreshResult { Libraries = 1 };
        }

        _logger.LogInformation(
            "Plex library {Title} scan finished. Selecting {Language} subtitles.",
            section.Title,
            language);
        var counts = await SelectLibraryAsync(
            credentials,
            section.Key,
            metadataType.Value,
            language,
            cancellationToken);
        _logger.LogInformation(
            "Selected {Language} on {Selected} of {Total} items in {Title}. {Already} already selected. {Missing} had no matching sidecar.",
            language,
            counts.Selected,
            counts.Selected + counts.AlreadySelected + counts.Missing,
            section.Title,
            counts.AlreadySelected,
            counts.Missing);
        return counts with { Libraries = 1 };
    }

    private async Task WaitForScanAsync(
        ResolvedPlexCredentials credentials,
        string sectionKey,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = DateTime.UtcNow - started;
            var refreshing = await IsRefreshingAsync(credentials, sectionKey, cancellationToken);
            if (!refreshing && elapsed >= _options.MinimumScanWait)
            {
                return;
            }

            if (elapsed >= _options.ScanTimeout)
            {
                _logger.LogWarning(
                    "Plex library {Key} was still scanning after {Minutes} minutes. Lingarr will select subtitles with the streams Plex has listed now.",
                    sectionKey,
                    _options.ScanTimeout.TotalMinutes);
                return;
            }

            if (_options.PollInterval > TimeSpan.Zero)
            {
                await Task.Delay(_options.PollInterval, cancellationToken);
            }
            else if (refreshing)
            {
                return;
            }
        }
    }

    private async Task<bool> IsRefreshingAsync(
        ResolvedPlexCredentials credentials,
        string sectionKey,
        CancellationToken cancellationToken)
    {
        var sections = await _plex.GetLibrariesAsync(
            credentials.Url!,
            credentials.Token!,
            credentials.ClientId,
            cancellationToken);
        var section = sections.FirstOrDefault(item =>
            string.Equals(item.Key, sectionKey, StringComparison.OrdinalIgnoreCase));
        return section?.Refreshing == true;
    }

    private async Task<PlexLibraryRefreshResult> SelectLibraryAsync(
        ResolvedPlexCredentials credentials,
        string sectionKey,
        int metadataType,
        string language,
        CancellationToken cancellationToken)
    {
        var selected = 0;
        var already = 0;
        var missing = 0;
        var start = 0;
        var pageSize = Math.Max(1, _options.PageSize);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await _plex.GetLibraryPageAsync(
                credentials.Url!,
                credentials.Token!,
                credentials.ClientId,
                sectionKey,
                metadataType,
                start,
                pageSize,
                cancellationToken);
            var parsed = PlexMatcher.ReadLibraryPage(page);
            if (parsed.ItemCount == 0)
            {
                break;
            }

            foreach (var ratingKey in parsed.RatingKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = await SelectItemAsync(credentials, ratingKey, language, cancellationToken);
                switch (outcome)
                {
                    case SelectOutcome.Selected:
                        selected++;
                        break;
                    case SelectOutcome.AlreadySelected:
                        already++;
                        break;
                    default:
                        missing++;
                        break;
                }
            }

            start += pageSize;
            if (parsed.ItemCount < pageSize || (parsed.TotalSize is int total && start >= total))
            {
                break;
            }
        }

        return new PlexLibraryRefreshResult
        {
            Selected = selected,
            AlreadySelected = already,
            Missing = missing
        };
    }

    private async Task<SelectOutcome> SelectItemAsync(
        ResolvedPlexCredentials credentials,
        string ratingKey,
        string language,
        CancellationToken cancellationToken)
    {
        string metadata;
        try
        {
            metadata = await _plex.GetMetadataAsync(
                credentials.Url!,
                credentials.Token!,
                credentials.ClientId,
                ratingKey,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Plex metadata request failed for item {RatingKey}.", ratingKey);
            return SelectOutcome.Missing;
        }

        var part = PlexMatcher.PrimaryPart(metadata);
        if (part == null)
        {
            return SelectOutcome.Missing;
        }

        var stream = PlexMatcher.PickSubtitle(part.Subtitles, language);
        if (stream == null)
        {
            return SelectOutcome.Missing;
        }

        if (stream.Selected)
        {
            return SelectOutcome.AlreadySelected;
        }

        try
        {
            await _plex.SetSelectedSubtitleAsync(
                credentials.Url!,
                credentials.Token!,
                credentials.ClientId,
                part.PartId,
                stream.Id,
                allParts: part.PartCount <= 1,
                cancellationToken);
            return SelectOutcome.Selected;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Plex could not select subtitle {StreamId} on item {RatingKey}.",
                stream.Id,
                ratingKey);
            return SelectOutcome.Missing;
        }
    }

    private async Task<ResolvedPlexCredentials> RequireCredentialsAsync()
    {
        var credentials = await PlexCredentials.ResolveAsync(_settings);
        if (string.IsNullOrWhiteSpace(credentials.Url) || string.IsNullOrWhiteSpace(credentials.Token))
        {
            throw new InvalidOperationException("Connect Plex before refreshing a library.");
        }

        return credentials;
    }

    private static int TypeRank(string type) => type.ToLowerInvariant() switch
    {
        "movie" => 0,
        "show" => 1,
        _ => 2
    };

    private static int? MetadataType(string type) => type.ToLowerInvariant() switch
    {
        "movie" => 1,
        "show" => 4,
        _ => null
    };

    private enum SelectOutcome
    {
        Selected,
        AlreadySelected,
        Missing
    }
}
