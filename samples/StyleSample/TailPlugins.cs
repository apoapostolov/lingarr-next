using System.Text.RegularExpressions;
using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Plugin.StyleSample;

public static class ShelfNotes
{
    public static string? LastFile { get; set; }

    public static string? LastWarning { get; set; }
}

public sealed class HearingFilterManifest : IPluginManifest
{
    public string Provider => "hearing-filter";
    public string DisplayName => "Hearing filter";
    public string? Description => "Drops a cue line that is only a bracketed sound, such as [music].";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("hearing-filter")]
public sealed class HearingFilter : IContentFilter
{
    private static readonly Regex CueLine = new(
        @"^(\[[^\]\n]{1,40}\]|\([^)\n]{1,40}\))(\s+(\[[^\]\n]{1,40}\]|\([^)\n]{1,40}\)))*$",
        RegexOptions.Compiled);

    public string Provider => "hearing-filter";

    public Task<string> FilterAsync(string subtitleText, CancellationToken cancellationToken) =>
        Task.FromResult(Apply(subtitleText));

    public static string Apply(string subtitleText)
    {
        var lines = subtitleText.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (CueLine.IsMatch(lines[index].Trim()))
            {
                lines[index] = string.Empty;
            }
        }

        return string.Join("\n", lines);
    }
}

public sealed class CastGlossaryManifest : IPluginManifest
{
    public string Provider => "cast-glossary";
    public string DisplayName => "Cast glossary";
    public string? Description => "Hands a few character names to post-processors.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("cast-glossary")]
public sealed class CastGlossary : IGlossary
{
    public string Provider => "cast-glossary";

    public Task<IReadOnlyList<string>> TermsAsync(string language, CancellationToken cancellationToken) =>
        Task.FromResult(For(language));

    public static IReadOnlyList<string> For(string language) => language.ToLowerInvariant() switch
    {
        "fr" => ["Amélie", "Nino"],
        "ja" => ["Chihiro", "Haku"],
        _ => []
    };
}

public sealed class TrailingLineManifest : IPluginManifest
{
    public string Provider => "trailing-line";
    public string DisplayName => "Trailing line";
    public string? Description => "Adds a final newline to the finished subtitle when one is missing.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("trailing-line")]
public sealed class TrailingLine : IFileTool
{
    public string Provider => "trailing-line";

    public async Task RunAsync(string targetPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
        {
            return;
        }

        var text = await File.ReadAllTextAsync(targetPath, cancellationToken);
        if (text.Length == 0 || text.EndsWith('\n'))
        {
            return;
        }

        await File.AppendAllTextAsync(targetPath, "\n", cancellationToken);
    }
}

public sealed class ShelfLogManifest : IPluginManifest
{
    public string Provider => "shelf-log";
    public string DisplayName => "Shelf log";
    public string? Description => "Keeps the latest warning in memory.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("shelf-log")]
public sealed class ShelfLog : ILogSink
{
    public string Provider => "shelf-log";

    public void Write(string level, string category, string message)
    {
        if (string.Equals(level, "Warning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(level, "Error", StringComparison.OrdinalIgnoreCase))
        {
            ShelfNotes.LastWarning = message;
        }
    }
}

public sealed class ShelfHealthManifest : IPluginManifest
{
    public string Provider => "shelf-health";
    public string DisplayName => "Shelf health";
    public string? Description => "Answers one sentence about the shelf.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("shelf-health")]
public sealed class ShelfHealth : IPluginHealthCheck
{
    public string Provider => "shelf-health";

    public Task<string> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(string.IsNullOrEmpty(ShelfNotes.LastWarning)
            ? "Shelf is ready."
            : "Shelf saw a warning.");
}

public sealed class ShelfWidgetManifest : IPluginManifest
{
    public string Provider => "shelf-widget";
    public string DisplayName => "Shelf widget";
    public string? Description => "Shows the last file Lingarr started to look at.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("shelf-widget")]
public sealed class ShelfWidget : IDashboardWidget
{
    public string Provider => "shelf-widget";

    public string Title => "Shelf";

    public Task<string> TextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(string.IsNullOrEmpty(ShelfNotes.LastFile)
            ? "Shelf has not seen a file yet."
            : $"Shelf last saw {ShelfNotes.LastFile}.");
}

public sealed class ShelfWatchManifest : IPluginManifest
{
    public string Provider => "shelf-watch";
    public string DisplayName => "Shelf watch";
    public string? Description => "Remembers the file name when Lingarr starts work on a title.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("shelf-watch")]
public sealed class ShelfWatch : IMediaEventSink
{
    public string Provider => "shelf-watch";

    public Task OnDiscoveredAsync(string? directory, string? fileName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            ShelfNotes.LastFile = fileName;
        }

        return Task.CompletedTask;
    }
}
