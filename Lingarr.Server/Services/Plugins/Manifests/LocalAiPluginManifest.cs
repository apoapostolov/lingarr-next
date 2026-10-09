using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class LocalAiPluginManifest : IPluginManifest
{
    public string Provider => "localai";

    public string DisplayName => "LocalAI / Ollama";

    public string? Description =>
        "Connect to an OpenAI-compatible or Ollama-compatible local deployment.";

    public bool HasRequestTemplate => true;
    public bool SupportsInstructionProfiles => true;

    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.LocalAi.Endpoint,
            Label = "Address",
            Type = PluginSettingType.Url,
            Required = true,
            Default = "http://ollama:11434/v1/chat/completions",
            Description = "Use the deployment's full chat/completions or generate endpoint URL."
        },
        new()
        {
            Key = SettingKeys.Translation.LocalAi.Model,
            Label = "AI Model",
            Type = PluginSettingType.Text,
            Required = true,
            Default = "aya-expanse",
            Description = "Model name configured in the deployment."
        },
        new()
        {
            Key = SettingKeys.Translation.LocalAi.ApiKey,
            Label = "API key (optional)",
            Type = PluginSettingType.Secret,
            Required = false,
            Description = "Optional bearer token for deployments that require authentication; stored encrypted."
        }
    ];
}
