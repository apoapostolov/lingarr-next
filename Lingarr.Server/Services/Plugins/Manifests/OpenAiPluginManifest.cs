using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class OpenAiPluginManifest : IPluginManifest
{
    public string Provider => "openai";

    public string DisplayName => "OpenAI";

    public string? Description =>
        "OpenAI chat models with live model discovery.";

    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;

    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.OpenAi.ApiKey,
            Label = "API key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Stored encrypted.",
            MinLength = 1,
            ValidationErrorMessage = "Value must not be empty"
        },
        new()
        {
            Key = SettingKeys.Translation.OpenAi.Model,
            Label = "AI Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/openai/models"
        }
    ];
}
