using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class DeepLPluginManifest : IPluginManifest
{
    public string Provider => "deepl";

    public string DisplayName => "DeepL";

    public string? Description =>
        "DeepL translation API. Free and Pro keys are supported.";

    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.DeepL.DeeplApiKey,
            Label = "API key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Stored encrypted.",
            MinLength = 1,
            ValidationErrorMessage = "Value must not be empty"
        }
    ];
}
