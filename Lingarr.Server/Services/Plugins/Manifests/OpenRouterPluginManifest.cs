using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class OpenRouterPluginManifest : IPluginManifest
{
    public string Provider => "openrouter";
    public string DisplayName => "OpenRouter";
    public string Description =>
        "Access multiple providers through the OpenRouter API.";
    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;
    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.OpenRouter.ApiKey,
            Label = "API Key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Stored encrypted."
        },
        new()
        {
            Key = SettingKeys.Translation.OpenRouter.Model,
            Label = "AI Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/openrouter/models",
            Description = "Free model routing is available for bulk translation."
        },
        new()
        {
            Key = SettingKeys.Translation.OpenRouter.Endpoint,
            Label = "API Endpoint",
            Type = PluginSettingType.Url,
            Required = false,
            Default = "https://openrouter.ai/api/v1/",
            Description = "Set a compatible proxy URL if required."
        }
    ];
}
