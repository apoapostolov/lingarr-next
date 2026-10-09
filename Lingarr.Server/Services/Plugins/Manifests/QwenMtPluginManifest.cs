using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;
using Lingarr.Server.Services.Translation;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class QwenMtPluginManifest : IPluginManifest
{
    public string Provider => "qwen-mt";
    public string DisplayName => "Qwen Translation";
    public string Description =>
        "Purpose-built subtitle translation with explicit language codes; prompt profiles do not apply.";
    public bool HasRequestTemplate => false;
    public bool SupportsInstructionProfiles => false;
    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.QwenMt.ApiKey,
            Label = "Model Studio API Key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Shared with Qwen General AI; stored encrypted."
        },
        new()
        {
            Key = SettingKeys.Translation.QwenMt.Model,
            Label = "Translation Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/qwen-mt/models",
            Description = "Flash prioritizes throughput; Plus targets higher translation quality."
        },
        new()
        {
            Key = SettingKeys.Translation.QwenMt.Endpoint,
            Label = "OpenAI-Compatible API Base URL",
            Type = PluginSettingType.Url,
            Required = true,
            Default = QwenService.DefaultEndpoint,
            Description = "Shared with Qwen General AI; must match the API key's region."
        }
    ];
}
