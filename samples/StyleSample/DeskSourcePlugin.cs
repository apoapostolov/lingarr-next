using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Plugin.StyleSample;

public sealed class DeskSourceManifest : IPluginManifest
{
    public const string Id = "desk-source";

    public string Provider => Id;
    public string DisplayName => "Desk source";
    public string? Description =>
        "If Bazarr is off or missed, copies a sibling .desk.txt file into an English subtitle.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
    public IReadOnlyList<PluginPanelContribution> Panels { get; } = [];
}

[PluginProvider(DeskSourceManifest.Id)]
public sealed class DeskSource : ISubtitleSource
{
    public string Provider => DeskSourceManifest.Id;

    public Task<bool> TrySupplyAsync(SubtitleSourceRequest request, CancellationToken cancellationToken)
    {
        var notes = Path.Combine(request.Directory, request.MediaFileName + ".desk.txt");
        if (!File.Exists(notes))
        {
            return Task.FromResult(false);
        }

        var destination = Path.Combine(
            request.Directory,
            request.MediaFileName + "." + request.Language + ".srt");
        File.WriteAllText(destination, File.ReadAllText(notes));
        return Task.FromResult(true);
    }
}
