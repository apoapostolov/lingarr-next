using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class AnthropicPluginManifest : IPluginManifest
{
    public string Provider => "anthropic";

    public string DisplayName => "Anthropic";

    public string? Description =>
        "Anthropic API with model discovery and configurable request templates.";

    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;

    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.Anthropic.ApiKey,
            Label = "API key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Stored encrypted.",
            MinLength = 1,
            ValidationErrorMessage = "Value must not be empty"
        },
        new()
        {
            Key = SettingKeys.Translation.Anthropic.Version,
            Label = "Version",
            Type = PluginSettingType.Text,
            Required = true,
            Default = "2023-06-01",
            Description = "Value sent in the `anthropic-version` request header.",
            MinLength = 1,
            ValidationErrorMessage = "Value must not be empty"
        },
        new()
        {
            Key = SettingKeys.Translation.Anthropic.Model,
            Label = "AI Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/anthropic/models"
        }
    ];
}
