namespace Lingarr.Contracts.Plugins;

public sealed class WebhookInboxDecision
{
    public bool Ignore { get; init; }
    public string Kind { get; init; } = "movie";
    public string Title { get; init; } = "";
    public string? ShowTitle { get; init; }
    public int? Year { get; init; }
    public int? SeasonNumber { get; init; }
    public int? EpisodeNumber { get; init; }
    public string? Tmdb { get; init; }
    public string? Imdb { get; init; }
    public string? ItemId { get; init; }
}

public interface IWebhookInbox
{
    string Provider { get; }

    WebhookInboxDecision Read(string json);
}

public sealed class PluginNotice
{
    public required bool Succeeded { get; init; }
    public required string Title { get; init; }
    public string? Detail { get; init; }
    public string? Path { get; init; }
}

public interface IPluginNotifier
{
    string Provider { get; }

    Task NotifyAsync(PluginNotice notice, CancellationToken cancellationToken);
}

public sealed class MediaItemRef
{
    public required string Title { get; init; }
    public string? Path { get; init; }
    public string? FileName { get; init; }
    public int? Year { get; init; }
    public string? SubtitlePath { get; init; }
    public string? Language { get; init; }
}

public interface IMediaServerPlugin
{
    string Provider { get; }

    Task RefreshItemAsync(MediaItemRef item, CancellationToken cancellationToken);

    Task SelectSubtitleAsync(MediaItemRef item, CancellationToken cancellationToken);
}

public interface IPluginTask
{
    string Provider { get; }

    string DisplayName { get; }

    string Cron { get; }

    Task ExecuteAsync(CancellationToken cancellationToken);
}

public interface IMediaAction
{
    string Provider { get; }

    string ActionId { get; }

    string Label { get; }

    Task<string> RunAsync(MediaItemRef item, CancellationToken cancellationToken);
}
