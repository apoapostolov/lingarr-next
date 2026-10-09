namespace Lingarr.Contracts.Plugins;

/// <summary>
/// A settings card the host draws on a tab. The plugin does not ship its own page.
/// </summary>
public sealed class PluginPanelContribution
{
    public required string Id { get; init; }
    public required string Section { get; init; }
    public required string TabId { get; init; }
    public required string TabLabel { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<PluginSettingField> Fields { get; init; } = [];
    public IReadOnlyList<PluginActionDefinition> Actions { get; init; } = [];
}

public sealed class PluginActionDefinition
{
    public required string Id { get; init; }
    public required string Label { get; init; }
}
