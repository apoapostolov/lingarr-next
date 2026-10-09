using Lingarr.Contracts.Plugins;

namespace Lingarr.Server.Models.Api;

public sealed class PluginResponse
{
    public required string Provider { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required bool IsBuiltIn { get; init; }
    public string? SourceFile { get; init; }
    public required IReadOnlyList<PluginSettingField> Settings { get; init; }
    public required bool HasRequestTemplate { get; init; }
    public required bool SupportsInstructionProfiles { get; init; }
    public bool Enabled { get; init; } = true;
    public int Order { get; init; } = 100;
    public string FailurePolicy { get; init; } = "skip";
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<PluginPanelContribution> Panels { get; init; } = [];
}
