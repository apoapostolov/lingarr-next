using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class OpenCodeGoPluginManifest : IPluginManifest
{
    public string Provider => "opencode-go";
    public string DisplayName => "OpenCode Go";
    public string Description =>
        "OpenCode Go models through the OpenAI-compatible API.";
    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;
    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.OpenCodeGo.ApiKey,
            Label = "API Key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Stored encrypted."
        },
        new()
        {
            Key = SettingKeys.Translation.OpenCodeGo.Model,
            Label = "AI Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/opencode-go/models",
        },
        new()
        {
            Key = SettingKeys.Translation.OpenCodeGo.Endpoint,
            Label = "API Base URL",
            Type = PluginSettingType.Url,
            Required = false,
            Default = "https://opencode.ai/zen/go/v1",
            Description = "Set a different API URL only when required by OpenCode."
        }
    ];
}
