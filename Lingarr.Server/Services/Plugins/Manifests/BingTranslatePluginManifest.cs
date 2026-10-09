using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class BingTranslatePluginManifest : IPluginManifest
{
    public string Provider => "bing";
    public string DisplayName => "Bing Translate";
    public string? Description =>
        "Uses the GTranslate library; availability depends on the upstream service.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}
