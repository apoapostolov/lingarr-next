using Lingarr.Contracts.Plugins;
using Lingarr.Contracts.Settings;

namespace Lingarr.Plugin.StyleSample;

[PluginProvider(StyleSampleManifest.Id)]
public sealed class StyleSampleActions : IPluginActionHandler
{
    private readonly ISettingsAccess _settings;

    public StyleSampleActions(ISettingsAccess settings)
    {
        _settings = settings;
    }

    public string Provider => StyleSampleManifest.Id;

    public async Task<string> ExecuteAsync(string actionId, CancellationToken cancellationToken)
    {
        if (!string.Equals(actionId, "test", StringComparison.OrdinalIgnoreCase))
        {
            return "Style sample does not have that action.";
        }

        var enabled = await _settings.GetSettingAsync(StyleSampleManifest.EnabledKey);
        return string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)
            ? "Style sample test succeeded."
            : "Style sample is off.";
    }
}
