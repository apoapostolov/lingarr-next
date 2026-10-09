using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class ZaiPluginManifest : IPluginManifest
{
    public string Provider => "zai";
    public string DisplayName => "Z.ai Coding Plan (GLM)";
    public string Description =>
        "GLM models through the Z.ai Coding Plan API.";
    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;
    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.Zai.ApiKey,
            Label = "Coding Plan API Key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Z.ai Coding Plan key; stored encrypted."
        },
        new()
        {
            Key = SettingKeys.Translation.Zai.Model,
            Label = "AI Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/zai/models",
        },
        new()
        {
            Key = SettingKeys.Translation.Zai.Endpoint,
            Label = "Coding Plan API Base URL",
            Type = PluginSettingType.Url,
            Required = false,
            Default = "https://api.z.ai/api/coding/paas/v4",
            Description =
                "Requires the Z.ai Coding Plan endpoint, not the general API."
        }
    ];
}
