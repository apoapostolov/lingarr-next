using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;
using Lingarr.Server.Services.Translation;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class QwenPluginManifest : IPluginManifest
{
    public string Provider => "qwen";
    public string DisplayName => "Qwen General AI";
    public string Description =>
        "General-purpose Qwen chat models with instruction profile support.";
    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;
    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.Qwen.ApiKey,
            Label = "Model Studio API Key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Shared with Qwen Translation; stored encrypted."
        },
        new()
        {
            Key = SettingKeys.Translation.Qwen.Model,
            Label = "AI Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/qwen/models",
        },
        new()
        {
            Key = SettingKeys.Translation.Qwen.Endpoint,
            Label = "OpenAI-Compatible API Base URL",
            Type = PluginSettingType.Url,
            Required = true,
            Default = QwenService.DefaultEndpoint,
            Description =
                "Use the API host issued with the Model Studio key. The key and endpoint must match regions."
        }
    ];
}
