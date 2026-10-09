using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class YandexTranslatePluginManifest : IPluginManifest
{
    public string Provider => "yandex";

    public string DisplayName => "Yandex Translate";

    public string? Description =>
        "Uses the GTranslate library; availability depends on the upstream service.";

    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}
