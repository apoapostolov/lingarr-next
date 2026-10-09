using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Plugin.StyleSample;

public sealed class FixtureExtractManifest : IPluginManifest
{
    public const string Id = "fixture-extract";

    public string Provider => Id;
    public string DisplayName => "Fixture extract";
    public string? Description =>
        "Copies a sibling .track file into an English subtitle with cp. Off until you enable it.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
    public IReadOnlyList<PluginPanelContribution> Panels { get; } = [];
}

[PluginProvider(FixtureExtractManifest.Id)]
public sealed class FixtureExtractTool : IExtractTool
{
    private readonly IPluginCommand _command;

    public FixtureExtractTool(IPluginCommand command)
    {
        _command = command;
    }

    public string Provider => FixtureExtractManifest.Id;
    public IReadOnlyList<string> Codecs { get; } = ["fixture"];

    public async Task<bool> TryExtractAsync(ExtractToolRequest request, CancellationToken cancellationToken)
    {
        var track = Path.Combine(request.Directory, request.MediaFileName + ".track");
        var destination = Path.Combine(request.Directory, request.MediaFileName + ".en.srt");
        if (!File.Exists(track))
        {
            return false;
        }

        var code = await _command.RunAsync(
            "/bin/cp",
            [track, destination],
            TimeSpan.FromSeconds(10),
            cancellationToken);
        return code == 0 && File.Exists(destination);
    }
}
