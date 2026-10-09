using Hangfire;
using Lingarr.Contracts.Plugins;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Plugins;

namespace Lingarr.Server.Jobs;

public class PluginTaskJob
{
    private readonly IEnumerable<IPluginTask> _tasks;
    private readonly ISettingService _settings;
    private readonly ILogger<PluginTaskJob> _logger;

    public PluginTaskJob(
        IEnumerable<IPluginTask> tasks,
        ISettingService settings,
        ILogger<PluginTaskJob> logger)
    {
        _tasks = tasks;
        _settings = settings;
        _logger = logger;
    }

    [Queue("default")]
    public async Task Execute(string provider)
    {
        var task = _tasks.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase));
        if (task == null)
        {
            return;
        }

        if (!PluginCatalog.IsEnabled(await _settings.GetSetting(PluginCatalog.EnabledKey(provider))))
        {
            return;
        }

        _logger.LogInformation("Running plugin task {Name}.", task.DisplayName);
        await task.ExecuteAsync(CancellationToken.None);
    }
}
