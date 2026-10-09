using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class LibreTranslatePluginManifest : IPluginManifest
{
    public string Provider => "libretranslate";

    public string DisplayName => "LibreTranslate";

    public string? Description =>
        "Connect to a self-hosted or hosted LibreTranslate deployment.";

    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.LibreTranslate.Url,
            Label = "Address",
            Type = PluginSettingType.Url,
            Required = true,
        },
        new()
        {
            Key = SettingKeys.Translation.LibreTranslate.ApiKey,
            Label = "API key (optional)",
            Type = PluginSettingType.Secret,
            Required = false,
            Description = "Required only when configured by the deployment; stored encrypted."
        }
    ];
}
