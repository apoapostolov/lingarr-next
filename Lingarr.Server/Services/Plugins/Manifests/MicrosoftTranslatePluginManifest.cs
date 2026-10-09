using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class MicrosoftTranslatePluginManifest : IPluginManifest
{
    public string Provider => "microsoft";

    public string DisplayName => "Microsoft Translate";

    public string? Description =>
        "Uses the GTranslate library; availability depends on the upstream service.";

    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}
