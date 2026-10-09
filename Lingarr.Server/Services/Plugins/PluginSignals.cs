using Lingarr.Contracts.Plugins;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.Plugins;

public sealed class PluginSignals
{
    private readonly IEnumerable<IPluginNotifier> _notifiers;
    private readonly IEnumerable<IMediaServerPlugin> _servers;
    private readonly IEnumerable<IMediaAction> _actions;
    private readonly IEnumerable<IMediaEventSink> _events;
    private readonly IEnumerable<IPluginHealthCheck> _health;
    private readonly IEnumerable<IDashboardWidget> _widgets;
    private readonly ISettingService _settings;
    private readonly ILogger<PluginSignals> _logger;

    public PluginSignals(
        IEnumerable<IPluginNotifier> notifiers,
        IEnumerable<IMediaServerPlugin> servers,
        IEnumerable<IMediaAction> actions,
        IEnumerable<IMediaEventSink> events,
        IEnumerable<IPluginHealthCheck> health,
        IEnumerable<IDashboardWidget> widgets,
        ISettingService settings,
        ILogger<PluginSignals> logger)
    {
        _notifiers = notifiers;
        _servers = servers;
        _actions = actions;
        _events = events;
        _health = health;
        _widgets = widgets;
        _settings = settings;
        _logger = logger;
    }

    public async Task NotifyAsync(PluginNotice notice, CancellationToken cancellationToken)
    {
        foreach (var notifier in _notifiers)
        {
            if (!await Enabled(notifier.Provider))
            {
                continue;
            }

            try
            {
                await notifier.NotifyAsync(notice, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not send a notice.", notifier.Provider);
            }
        }
    }

    public async Task SelectOnServersAsync(MediaItemRef item, CancellationToken cancellationToken)
    {
        foreach (var server in _servers)
        {
            if (!await Enabled(server.Provider))
            {
                continue;
            }

            try
            {
                await server.RefreshItemAsync(item, cancellationToken);
                await server.SelectSubtitleAsync(item, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "{Provider} could not select a subtitle for {Title}.",
                    server.Provider,
                    item.Title);
            }
        }
    }

    public async Task<string?> RunMediaActionAsync(
        string provider,
        string actionId,
        MediaItemRef item,
        CancellationToken cancellationToken)
    {
        var action = _actions.FirstOrDefault(candidate =>
            string.Equals(candidate.Provider, provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (action == null || !await Enabled(action.Provider))
        {
            return null;
        }

        return await action.RunAsync(item, cancellationToken);
    }

    public async Task<string?> ProbeAsync(string provider, CancellationToken cancellationToken)
    {
        var check = _health.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase));
        if (check == null)
        {
            return null;
        }

        if (!await Enabled(check.Provider))
        {
            return "";
        }

        try
        {
            return await check.ProbeAsync(cancellationToken) ?? "";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "{Provider} could not be reached.", check.Provider);
            return $"{check.Provider} could not be reached.";
        }
    }

    public async Task<IReadOnlyList<PluginWidgetText>> WidgetsAsync(CancellationToken cancellationToken)
    {
        var widgets = new List<PluginWidgetText>();
        foreach (var widget in _widgets)
        {
            if (!await Enabled(widget.Provider))
            {
                continue;
            }

            try
            {
                var text = await widget.TextAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(widget.Title) && !string.IsNullOrWhiteSpace(text))
                {
                    widgets.Add(new PluginWidgetText(widget.Title, text));
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not render its widget.", widget.Provider);
            }
        }

        return widgets;
    }

    public async Task OnDiscoveredAsync(string? directory, string? fileName, CancellationToken cancellationToken)
    {
        foreach (var sink in _events)
        {
            if (!await Enabled(sink.Provider))
            {
                continue;
            }

            try
            {
                await sink.OnDiscoveredAsync(directory, fileName, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not record {File}.", sink.Provider, fileName);
            }
        }
    }

    private async Task<bool> Enabled(string provider) =>
        PluginCatalog.IsEnabled(await _settings.GetSetting(PluginCatalog.EnabledKey(provider)));
}

public sealed record PluginWidgetText(string Title, string Text);
