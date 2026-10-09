using Lingarr.Contracts.Plugins;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.Plugins;

public sealed class PluginToolRunner
{
    private readonly IEnumerable<IExtractTool> _extractTools;
    private readonly IEnumerable<ISubtitleSource> _sources;
    private readonly ISettingService _settings;
    private readonly ILogger<PluginToolRunner> _logger;

    public PluginToolRunner(
        IEnumerable<IExtractTool> extractTools,
        IEnumerable<ISubtitleSource> sources,
        ISettingService settings,
        ILogger<PluginToolRunner> logger)
    {
        _extractTools = extractTools;
        _sources = sources;
        _settings = settings;
        _logger = logger;
    }

    public async Task<bool> TryExtractAsync(
        string directory,
        string mediaFileName,
        CancellationToken cancellationToken)
    {
        foreach (var tool in await Ready(_extractTools, tool => tool.Provider, cancellationToken))
        {
            try
            {
                var wrote = await tool.Item.TryExtractAsync(new ExtractToolRequest
                {
                    Directory = directory,
                    MediaFileName = mediaFileName,
                    Codec = "fixture"
                }, cancellationToken);
                if (wrote)
                {
                    _logger.LogInformation(
                        "{Provider} extracted a subtitle for {File}.",
                        tool.Item.Provider,
                        mediaFileName);
                    return true;
                }
            }
            catch (Exception exception)
            {
                if (await Failed(tool.Item.Provider, tool.Policy, exception, mediaFileName))
                {
                    throw;
                }

                if (tool.Policy == PluginCatalog.PolicyStop)
                {
                    return false;
                }
            }
        }

        return false;
    }

    public async Task<bool> TrySupplySourceAsync(
        string directory,
        string mediaFileName,
        string language,
        CancellationToken cancellationToken)
    {
        foreach (var source in await Ready(_sources, item => item.Provider, cancellationToken))
        {
            try
            {
                var wrote = await source.Item.TrySupplyAsync(new SubtitleSourceRequest
                {
                    Directory = directory,
                    MediaFileName = mediaFileName,
                    Language = language
                }, cancellationToken);
                if (wrote)
                {
                    _logger.LogInformation(
                        "{Provider} supplied a source subtitle for {File}.",
                        source.Item.Provider,
                        mediaFileName);
                    return true;
                }
            }
            catch (Exception exception)
            {
                if (await Failed(source.Item.Provider, source.Policy, exception, mediaFileName))
                {
                    throw;
                }

                if (source.Policy == PluginCatalog.PolicyStop)
                {
                    return false;
                }
            }
        }

        return false;
    }

    private async Task<List<OrderedTool<T>>> Ready<T>(
        IEnumerable<T> items,
        Func<T, string> provider,
        CancellationToken cancellationToken)
    {
        var ready = new List<OrderedTool<T>>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = provider(item);
            if (!PluginCatalog.IsEnabled(await _settings.GetSetting(PluginCatalog.EnabledKey(id))))
            {
                continue;
            }

            PluginCatalog.TryNormalizePolicy(
                await _settings.GetSetting(PluginCatalog.PolicyKey(id)),
                out var policy);
            ready.Add(new OrderedTool<T>(
                item,
                PluginCatalog.ParseOrder(await _settings.GetSetting(PluginCatalog.OrderKey(id))),
                policy));
        }

        return ready.OrderBy(item => item.Order).ToList();
    }

    private Task<bool> Failed(string provider, string policy, Exception exception, string file)
    {
        _logger.LogWarning(exception, "{Provider} failed for {File}.", provider, file);
        return Task.FromResult(policy == PluginCatalog.PolicyFail);
    }

    private sealed record OrderedTool<T>(T Item, int Order, string Policy);
}
