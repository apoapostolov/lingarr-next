using Lingarr.Contracts.Plugins;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Plugins;

public sealed class PluginShelf
{
    private readonly IEnumerable<IPathMapper> _mappers;
    private readonly IEnumerable<IRetryPolicy> _retries;
    private readonly IEnumerable<IListBadge> _badges;
    private readonly IEnumerable<IPromptContributor> _prompts;
    private readonly IEnumerable<IStatisticsExporter> _exporters;
    private readonly IEnumerable<ISubtitleMerge> _merges;
    private readonly IEnumerable<ISidecarCodec> _codecs;
    private readonly IEnumerable<ICaptionPolicy> _captions;
    private readonly IEnumerable<ILibraryAgent> _agents;
    private readonly ISettingService _settings;
    private readonly ILogger<PluginShelf> _logger;

    public PluginShelf(
        IEnumerable<IPathMapper> mappers,
        IEnumerable<IRetryPolicy> retries,
        IEnumerable<IListBadge> badges,
        IEnumerable<IPromptContributor> prompts,
        IEnumerable<IStatisticsExporter> exporters,
        IEnumerable<ISubtitleMerge> merges,
        IEnumerable<ISidecarCodec> codecs,
        IEnumerable<ICaptionPolicy> captions,
        IEnumerable<ILibraryAgent> agents,
        ISettingService settings,
        ILogger<PluginShelf> logger)
    {
        _mappers = mappers;
        _retries = retries;
        _badges = badges;
        _prompts = prompts;
        _exporters = exporters;
        _merges = merges;
        _codecs = codecs;
        _captions = captions;
        _agents = agents;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> MapAsync(string path, string mediaType, CancellationToken cancellationToken = default)
    {
        foreach (var mapper in _mappers)
        {
            if (!await Enabled(mapper.Provider))
            {
                continue;
            }

            try
            {
                var mapped = mapper.Map(path, mediaType);
                if (!string.IsNullOrWhiteSpace(mapped))
                {
                    path = mapped;
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not map a path.", mapper.Provider);
            }
        }

        return path;
    }

    public async Task<TimeSpan?> AdjustDelayAsync(
        TimeSpan? hostDelay,
        string kind,
        string? fileName,
        DateTime? foundAt,
        DateTime utcNow,
        int timeoutHours,
        CancellationToken cancellationToken = default)
    {
        RetryAdvice? advice = null;
        foreach (var policy in _retries)
        {
            if (!await Enabled(policy.Provider))
            {
                continue;
            }

            try
            {
                advice = await policy.AdviseAsync(kind, fileName, 1, cancellationToken) ?? advice;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not advise a retry.", policy.Provider);
            }
        }

        return ChooseDelay(hostDelay, advice, foundAt, utcNow, timeoutHours);
    }

    public static TimeSpan? ChooseDelay(
        TimeSpan? hostDelay,
        RetryAdvice? advice,
        DateTime? foundAt,
        DateTime utcNow,
        int timeoutHours)
    {
        if (advice == null)
        {
            return hostDelay;
        }

        if (!advice.Retry)
        {
            return null;
        }

        var delay = advice.DelayMinutes is > 0
            ? TimeSpan.FromMinutes(advice.DelayMinutes.Value)
            : hostDelay;
        if (delay == null || foundAt == null)
        {
            return delay;
        }

        if (utcNow.Add(delay.Value) > foundAt.Value.AddHours(timeoutHours))
        {
            return null;
        }

        return delay;
    }

    public async Task<IReadOnlyList<string>> LabelsAsync(string? subtitlePath, CancellationToken cancellationToken = default)
    {
        var labels = new List<string>();
        foreach (var badge in _badges)
        {
            if (!await Enabled(badge.Provider))
            {
                continue;
            }

            try
            {
                var label = CleanLabel(badge.Label(subtitlePath));
                if (label != null && !labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                {
                    labels.Add(label);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not label a title.", badge.Provider);
            }
        }

        return labels;
    }

    public async Task<string?> PromptBlockAsync(
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var blocks = new List<string>();
        foreach (var contributor in _prompts)
        {
            if (!await Enabled(contributor.Provider))
            {
                continue;
            }

            try
            {
                var block = contributor.Block(sourceLanguage, targetLanguage);
                if (!string.IsNullOrWhiteSpace(block))
                {
                    blocks.Add($"{contributor.Name}: {block.Trim()}");
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not add an instruction block.", contributor.Provider);
            }
        }

        return blocks.Count == 0 ? null : string.Join("\n", blocks);
    }

    public async Task ExportAsync(StatisticsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        foreach (var exporter in _exporters)
        {
            if (!await Enabled(exporter.Provider))
            {
                continue;
            }

            try
            {
                await exporter.ExportAsync(snapshot, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not export statistics.", exporter.Provider);
            }
        }
    }

    public async Task<SubtitleMergeChoice> MergeAsync(
        string existingPath,
        string incomingPath,
        bool existingIsOcr,
        CancellationToken cancellationToken = default)
    {
        var choice = SubtitleMergeChoice.Default;
        foreach (var merge in _merges)
        {
            if (!await Enabled(merge.Provider))
            {
                continue;
            }

            try
            {
                var next = await merge.ChooseAsync(existingPath, incomingPath, existingIsOcr, cancellationToken);
                if (next != SubtitleMergeChoice.Default)
                {
                    choice = next;
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not choose a merge.", merge.Provider);
            }
        }

        return choice;
    }

    public static bool ReplaceOcrSource(bool hostReplace, SubtitleMergeChoice choice) =>
        hostReplace && choice != SubtitleMergeChoice.Keep;

    public async Task<IReadOnlyList<ISidecarCodec>> CodecsAsync(CancellationToken cancellationToken = default)
    {
        var codecs = new List<ISidecarCodec>();
        foreach (var codec in _codecs)
        {
            if (await Enabled(codec.Provider) && !string.IsNullOrWhiteSpace(codec.Extension))
            {
                codecs.Add(codec);
            }
        }

        return codecs;
    }

    public async Task<List<Subtitles>> FilterCaptionsAsync(
        IReadOnlyList<Subtitles> files,
        CancellationToken cancellationToken = default)
    {
        var policies = new List<ICaptionPolicy>();
        foreach (var policy in _captions)
        {
            if (await Enabled(policy.Provider))
            {
                policies.Add(policy);
            }
        }

        if (policies.Count == 0)
        {
            return files.ToList();
        }

        var kept = new List<Subtitles>();
        foreach (var file in files)
        {
            var eligible = true;
            foreach (var policy in policies)
            {
                try
                {
                    var decision = await policy.DecideAsync(file.Path, file.Caption, cancellationToken);
                    if (decision is { Eligible: false })
                    {
                        eligible = false;
                        break;
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "{Provider} could not judge {Path}.", policy.Provider, file.Path);
                }
            }

            if (eligible)
            {
                kept.Add(file);
            }
        }

        return kept;
    }

    public async Task<string> ResolvePathAsync(string mappedPath, LibraryQuery query, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(mappedPath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            return mappedPath;
        }

        foreach (var agent in _agents)
        {
            if (!await Enabled(agent.Provider))
            {
                continue;
            }

            try
            {
                var hit = await agent.FindAsync(query, cancellationToken);
                var resolved = Prefer(mappedPath, hit);
                if (!string.Equals(resolved, mappedPath, StringComparison.Ordinal))
                {
                    return resolved;
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not find {Title}.", agent.Provider, query.Title);
            }
        }

        return mappedPath;
    }

    public static string Prefer(string mappedPath, LibraryHit? hit)
    {
        var directory = Path.GetDirectoryName(mappedPath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            return mappedPath;
        }

        if (string.IsNullOrWhiteSpace(hit?.Directory) || !Directory.Exists(hit.Directory))
        {
            return mappedPath;
        }

        var file = string.IsNullOrWhiteSpace(hit.FileName) ? Path.GetFileName(mappedPath) : hit.FileName;
        return string.IsNullOrEmpty(file) ? hit.Directory : Path.Combine(hit.Directory, file);
    }

    private async Task<bool> Enabled(string provider) =>
        PluginCatalog.IsEnabled(await _settings.GetSetting(PluginCatalog.EnabledKey(provider)));

    private static string? CleanLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var trimmed = label.Trim();
        return trimmed.Length <= 24 ? trimmed : trimmed[..24];
    }
}
