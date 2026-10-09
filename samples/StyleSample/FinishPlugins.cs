using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Plugin.StyleSample;

public sealed class FilmMapManifest : IPluginManifest
{
    public string Provider => "film-map";
    public string DisplayName => "Film map";
    public string? Description => "Rewrites /server/films to /media/films after the mapping table.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("film-map")]
public sealed class FilmMap : IPathMapper
{
    public string Provider => "film-map";

    public string? Map(string path, string mediaType) =>
        path.Replace("/server/films", "/media/films", StringComparison.Ordinal);
}

public sealed class QuietRetryManifest : IPluginManifest
{
    public string Provider => "quiet-retry";
    public string DisplayName => "Quiet retry";
    public string? Description => "Stops another Bazarr search when the file name contains quiet-retry.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("quiet-retry")]
public sealed class QuietRetry : IRetryPolicy
{
    public string Provider => "quiet-retry";

    public Task<RetryAdvice?> AdviseAsync(
        string kind,
        string? fileName,
        int attempt,
        CancellationToken cancellationToken) =>
        Task.FromResult<RetryAdvice?>(
            fileName != null && fileName.Contains("quiet-retry", StringComparison.OrdinalIgnoreCase)
                ? new RetryAdvice { Retry = false }
                : null);
}

public sealed class OcrBadgeManifest : IPluginManifest
{
    public string Provider => "ocr-badge";
    public string DisplayName => "OCR badge";
    public string? Description => "Puts ocr on the translation list when the file name carries that tag.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("ocr-badge")]
public sealed class OcrBadge : IListBadge
{
    public string Provider => "ocr-badge";

    public string? Label(string? subtitlePath) =>
        subtitlePath != null && subtitlePath.Contains(".ocr.", StringComparison.OrdinalIgnoreCase)
            ? "ocr"
            : null;
}

public sealed class NamePromptManifest : IPluginManifest
{
    public string Provider => "name-prompt";
    public string DisplayName => "Name prompt";
    public string? Description => "Adds a named spelling note for French or Japanese. It never reads instruction profiles.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("name-prompt")]
public sealed class NamePrompt : IPromptContributor
{
    public string Provider => "name-prompt";

    public string Name => "Names";

    public string? Block(string? sourceLanguage, string? targetLanguage) =>
        targetLanguage?.ToLowerInvariant() switch
        {
            "fr" => "Keep Amélie and Nino spelled as written.",
            "ja" => "Keep Chihiro and Haku spelled as written.",
            _ => null
        };
}

public sealed class CountExportManifest : IPluginManifest
{
    public string Provider => "count-export";
    public string DisplayName => "Count export";
    public string? Description => "Remembers movie, episode, and subtitle-file counts. Paths stay out.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("count-export")]
public sealed class CountExport : IStatisticsExporter
{
    public string Provider => "count-export";

    public static StatisticsSnapshot? Last { get; set; }

    public Task ExportAsync(StatisticsSnapshot snapshot, CancellationToken cancellationToken)
    {
        Last = snapshot;
        return Task.CompletedTask;
    }
}

public sealed class OcrMergeManifest : IPluginManifest
{
    public string Provider => "ocr-merge";
    public string DisplayName => "OCR merge";
    public string? Description => "Keeps an OCR subtitle when the incoming file name contains .keep.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("ocr-merge")]
public sealed class OcrMerge : ISubtitleMerge
{
    public string Provider => "ocr-merge";

    public Task<SubtitleMergeChoice> ChooseAsync(
        string existingPath,
        string incomingPath,
        bool existingIsOcr,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            incomingPath.Contains(".keep.", StringComparison.OrdinalIgnoreCase)
                ? SubtitleMergeChoice.Keep
                : SubtitleMergeChoice.Default);
}

public sealed class SubCodecManifest : IPluginManifest
{
    public string Provider => "sub-codec";
    public string DisplayName => "Sub codec";
    public string? Description => "Reads a .sub file when the text already looks like SRT.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("sub-codec")]
public sealed class SubCodec : ISidecarCodec
{
    public string Provider => "sub-codec";

    public string Extension => ".sub";

    public async Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return text.Contains("-->", StringComparison.Ordinal) ? text : null;
    }

    public Task WriteAsync(string path, string srtText, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, srtText, cancellationToken);
}

public sealed class SdhPolicyManifest : IPluginManifest
{
    public string Provider => "sdh-policy";
    public string DisplayName => "SDH policy";
    public string? Description => "Marks an SDH caption ineligible as a translation source.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("sdh-policy")]
public sealed class SdhPolicy : ICaptionPolicy
{
    public string Provider => "sdh-policy";

    public Task<CaptionDecision?> DecideAsync(string path, string? caption, CancellationToken cancellationToken) =>
        Task.FromResult<CaptionDecision?>(
            string.Equals(caption, "sdh", StringComparison.OrdinalIgnoreCase)
                ? new CaptionDecision { Kind = "sdh", Eligible = false }
                : null);
}

public sealed class ShelfLibraryManifest : IPluginManifest
{
    public string Provider => "shelf-library";
    public string DisplayName => "Shelf library";
    public string? Description => "Looks up a folder by external id when the mapped folder is missing.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
}

[PluginProvider("shelf-library")]
public sealed class ShelfLibrary : ILibraryAgent
{
    public string Provider => "shelf-library";

    public static string? FixtureId { get; set; }

    public static string? FixtureDirectory { get; set; }

    public Task<LibraryHit?> FindAsync(LibraryQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(FixtureId)
            || !string.Equals(query.ExternalId, FixtureId, StringComparison.Ordinal))
        {
            return Task.FromResult<LibraryHit?>(null);
        }

        return Task.FromResult<LibraryHit?>(new LibraryHit { Directory = FixtureDirectory });
    }
}
