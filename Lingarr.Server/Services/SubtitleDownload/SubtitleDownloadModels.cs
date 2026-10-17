using Lingarr.Contracts.Plugins;
using Lingarr.Core.Enum;

namespace Lingarr.Server.Services.SubtitleDownload;

public enum SubtitleDownloadKind
{
    HtmlSearch,
    OpenSubtitlesCom,
    Subdl,
    Gestdown,
    Assrt,
    BetaSeries,
    Subsource,
    SubsRo,
    SubX,
    Titlovi,
    Napisy24,
    SubtitleCat,
    Yify,
    TvSubtitles,
    Subtis,
    RegieLive
}

public sealed class SubtitleProviderField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required PluginSettingType Type { get; init; }
    public bool Required { get; init; }
    public string? Default { get; init; }
    public string? Description { get; init; }
}

public sealed class SubtitleProviderDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public SubtitleDownloadKind Kind { get; init; }
    public bool SupportsMovies { get; init; } = true;
    public bool SupportsEpisodes { get; init; } = true;
    public IReadOnlyList<SubtitleProviderField> Fields { get; init; } = [];

    public string SettingKey(string field) =>
        Lingarr.Core.Configuration.SettingKeys.SubtitleDownload.FieldKey(Id, field);

    public IReadOnlyList<PluginSettingField> ManifestSettings =>
        Fields.Select(item => new PluginSettingField
        {
            Key = SettingKey(item.Key),
            Label = item.Label,
            Type = item.Type,
            Required = item.Required,
            Default = item.Default,
            Description = item.Description
        }).ToList();
}

public sealed class SubtitleDownloadChainEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Provider { get; set; } = "";
}

public sealed class SubtitleDownloadQuery
{
    public required MediaType MediaType { get; init; }
    public required int MediaId { get; init; }
    public required string Title { get; init; }
    public string? SeriesTitle { get; init; }
    public int? Year { get; init; }
    public int? SeasonNumber { get; init; }
    public int? EpisodeNumber { get; init; }
    public required string Language { get; init; }
    public string? FileName { get; init; }
    public string? Directory { get; init; }
    public string? VideoPath { get; init; }
}

public sealed class SubtitleDownloadHit
{
    public required string Provider { get; init; }
    public required byte[] Content { get; init; }
    public string FileName { get; init; } = "subtitle.srt";
    public string Extension { get; init; } = ".srt";
}

public sealed class ForceTranslateResult
{
    public bool SourceMissing { get; init; }
    public IReadOnlyList<int> RequestIds { get; init; } = [];
    public IReadOnlyList<string> Downloaded { get; init; } = [];
    public required string Message { get; init; }
}
