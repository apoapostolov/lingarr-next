using System.Text.Json;
using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Plugin.StyleSample;

public sealed class FixtureInboxManifest : IPluginManifest
{
    public string Provider => "fixture-inbox";
    public string DisplayName => "Fixture inbox";
    public string? Description => "Queues a movie from a small JSON body when the plugin is enabled.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("fixture-inbox")]
public sealed class FixtureInbox : IWebhookInbox
{
    public string Provider => "fixture-inbox";

    public WebhookInboxDecision Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var kind = Text(root, "event");
        if (!string.Equals(kind, "added", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookInboxDecision { Ignore = true };
        }

        return new WebhookInboxDecision
        {
            Kind = Text(root, "kind") ?? "movie",
            Title = Text(root, "title") ?? "",
            Year = Number(root, "year"),
            Tmdb = Text(root, "tmdb"),
            Imdb = Text(root, "imdb"),
            ItemId = Text(root, "id")
        };
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
}

public sealed class ShelfNoticeManifest : IPluginManifest
{
    public string Provider => "shelf-notice";
    public string DisplayName => "Shelf notice";
    public string? Description => "Hears when a translation finishes. Off until you enable it.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("shelf-notice")]
public sealed class ShelfNotice : IPluginNotifier
{
    public string Provider => "shelf-notice";

    public Task NotifyAsync(PluginNotice notice, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class ShelfServerManifest : IPluginManifest
{
    public string Provider => "shelf-server";
    public string DisplayName => "Shelf server";
    public string? Description => "Refreshes one item and selects the new subtitle. Plex stays in place.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("shelf-server")]
public sealed class ShelfServer : IMediaServerPlugin
{
    public string Provider => "shelf-server";

    public Task RefreshItemAsync(MediaItemRef item, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task SelectSubtitleAsync(MediaItemRef item, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class MorningTaskManifest : IPluginManifest
{
    public string Provider => "morning-task";
    public string DisplayName => "Morning task";
    public string? Description => "A daily job at 04:00 UTC. It appears under System → Tasks after a restart.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("morning-task")]
public sealed class MorningTask : IPluginTask
{
    public string Provider => "morning-task";
    public string DisplayName => "Morning task";
    public string Cron => "0 4 * * *";

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class MarkTitleManifest : IPluginManifest
{
    public string Provider => "mark-title";
    public string DisplayName => "Mark title";
    public string? Description => "Runs against one title and returns a sentence.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("mark-title")]
public sealed class MarkTitle : IMediaAction
{
    public string Provider => "mark-title";
    public string ActionId => "mark";
    public string Label => "Mark";

    public Task<string> RunAsync(MediaItemRef item, CancellationToken cancellationToken) =>
        Task.FromResult($"Marked {item.Title}.");
}
