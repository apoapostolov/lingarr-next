using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;
using Lingarr.Server.Services.Translation;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class MistralPluginManifest : IPluginManifest
{
    public string Provider => "mistral";
    public string DisplayName => "Mistral AI";
    public string Description =>
        "Mistral API with model discovery, instruction profiles, and fallback support.";
    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;
    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.Mistral.ApiKey,
            Label = "API Key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Stored encrypted."
        },
        new()
        {
            Key = SettingKeys.Translation.Mistral.Model,
            Label = "AI Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/mistral/models",
        },
        new()
        {
            Key = SettingKeys.Translation.Mistral.Endpoint,
            Label = "OpenAI-Compatible API Base URL",
            Type = PluginSettingType.Url,
            Required = true,
            Default = MistralService.DefaultEndpoint,
            Description = "Set a compatible proxy URL if required."
        }
    ];
}
