using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class GoogleTranslatePluginManifest : IPluginManifest
{
    public string Provider => "google";

    public string DisplayName => "Google Translate";

    public string? Description =>
        "Uses the GTranslate library; availability depends on the upstream service.";

    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}
