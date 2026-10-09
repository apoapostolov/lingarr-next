using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class XaiOAuthPluginManifest : IPluginManifest
{
    public string Provider => "xai-oauth";
    public string DisplayName => "xAI SuperGrok / Premium+";
    public string Description =>
        "Experimental xAI account connection using device authorization. Access and quotas are controlled by xAI.";
    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;
    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.XaiOAuth.Connection,
            Label = "xAI Account",
            Type = PluginSettingType.OAuth,
            Required = true,
            Description = "Credentials are stored encrypted on the Lingarr server."
        },
        new()
        {
            Key = SettingKeys.Translation.XaiOAuth.Model,
            Label = "Grok Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/xai-oauth/models"
        }
    ];
}
