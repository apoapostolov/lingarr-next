using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class XaiPluginManifest : IPluginManifest
{
    public string Provider => "xai";
    public string DisplayName => "xAI API";
    public string Description =>
        "Official xAI API using a developer API key.";
    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;
    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.Xai.ApiKey,
            Label = "xAI API Key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Stored encrypted."
        },
        new()
        {
            Key = SettingKeys.Translation.Xai.Model,
            Label = "Grok Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/xai/models"
        }
    ];
}
