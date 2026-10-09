using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Contracts.Settings;

namespace Lingarr.Plugin.StyleSample;

public sealed class SpanishLanguageManifest : IPluginManifest
{
    public const string Id = "spanish-language";
    public const string MarksKey = "spanish_language_marks";

    public string Provider => Id;
    public string DisplayName => "Spanish language";
    public string? Description => "Puts back leading ¿ and ¡ on finished Spanish lines.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
    public IReadOnlyList<PluginPanelContribution> Panels { get; } =
    [
        new()
        {
            Id = "spanish",
            Section = "plugins",
            TabId = "language",
            TabLabel = "Language",
            Title = "Spanish language",
            Description = "Puts ¿ and ¡ back on dialogue lines. Timestamps stay as they are.",
            Fields =
            [
                new()
                {
                    Key = MarksKey,
                    Label = "Restore Spanish marks",
                    Type = PluginSettingType.Toggle,
                    Default = "false"
                }
            ]
        }
    ];
}

[PluginProvider(SpanishLanguageManifest.Id)]
public sealed class SpanishLanguageProcessor : ISubtitlePostProcessor
{
    private readonly ISettingsAccess _settings;

    public SpanishLanguageProcessor(ISettingsAccess settings)
    {
        _settings = settings;
    }

    public string Provider => SpanishLanguageManifest.Id;
    public SubtitlePostProcessKind Kind => SubtitlePostProcessKind.Language;

    public async Task<SubtitlePostProcessResult> ProcessAsync(
        SubtitlePostProcessInput input,
        CancellationToken cancellationToken)
    {
        var enabled = await _settings.GetSettingAsync(SpanishLanguageManifest.MarksKey);
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return new SubtitlePostProcessResult();
        }

        return new SubtitlePostProcessResult { Text = Apply(input.TargetText) };
    }

    public static string Apply(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Contains("-->", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(line)
                || int.TryParse(line.Trim(), out _))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            var pad = line[..(line.Length - trimmed.Length)];
            if (trimmed.EndsWith('?') && !trimmed.Contains('¿'))
            {
                trimmed = "¿" + trimmed;
            }

            if (trimmed.EndsWith('!') && !trimmed.Contains('¡'))
            {
                trimmed = "¡" + trimmed;
            }

            lines[index] = pad + trimmed;
        }

        return string.Join('\n', lines);
    }
}
