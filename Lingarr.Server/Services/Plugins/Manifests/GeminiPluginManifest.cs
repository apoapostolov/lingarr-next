using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class GeminiPluginManifest : IPluginManifest
{
    public string Provider => "gemini";

    public string DisplayName => "Google Gemini";

    public string? Description =>
        "Google Gemini models via the Google AI Studio API.";

    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;

    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.Gemini.ApiKey,
            Label = "API key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Stored encrypted.",
            MinLength = 1,
            ValidationErrorMessage = "Value must not be empty"
        },
        new()
        {
            Key = SettingKeys.Translation.Gemini.Model,
            Label = "AI Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            OptionsEndpoint = "/api/plugin/gemini/models"
        }
    ];
}
