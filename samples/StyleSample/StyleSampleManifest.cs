using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Plugin.StyleSample;

public sealed class StyleSampleManifest : IPluginManifest
{
    public const string Id = "style-sample";
    public const string EnabledKey = "style_sample_enabled";

    public string Provider => Id;
    public string DisplayName => "Style sample";
    public string? Description =>
        "Sample settings tab. The switch is stored by Lingarr and is not a translator.";

    public IReadOnlyList<PluginSettingField> Settings { get; } = [];

    public IReadOnlyList<PluginPanelContribution> Panels { get; } =
    [
        new()
        {
            Id = "style",
            Section = "plugins",
            TabId = "style",
            TabLabel = "Style",
            Title = "Style sample",
            Description = "Turn the switch on, then test it. The switch stays set after a restart.",
            Fields =
            [
                new()
                {
                    Key = EnabledKey,
                    Label = "Improve style",
                    Type = PluginSettingType.Toggle,
                    Default = "false",
                    Description = "The Test button reads this switch."
                }
            ],
            Actions = [new() { Id = "test", Label = "Test" }]
        }
    ];
}
