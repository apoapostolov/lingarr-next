using Lingarr.Contracts.Plugins;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.Plugins;

public sealed class SubtitlePostProcessJob
{
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
}

public sealed class ReadyPostProcessor
{
    public required ISubtitlePostProcessor Processor { get; init; }
    public required int Order { get; init; }
    public required string FailurePolicy { get; init; }
}

public sealed class PostProcessOutcome
{
    public required string Text { get; init; }
    public required bool Write { get; init; }
    public bool Failed { get; init; }
    public string? Reason { get; init; }
}

public sealed class SubtitlePostProcessRunner
{
    private readonly IEnumerable<ISubtitlePostProcessor> _processors;
    private readonly IEnumerable<IContentFilter> _filters;
    private readonly IEnumerable<IGlossary> _glossaries;
    private readonly IEnumerable<IFileTool> _fileTools;
    private readonly ISettingService _settings;
    private readonly ILogger<SubtitlePostProcessRunner> _logger;

    public SubtitlePostProcessRunner(
        IEnumerable<ISubtitlePostProcessor> processors,
        IEnumerable<IContentFilter> filters,
        IEnumerable<IGlossary> glossaries,
        IEnumerable<IFileTool> fileTools,
        ISettingService settings,
        ILogger<SubtitlePostProcessRunner> logger)
    {
        _processors = processors;
        _filters = filters;
        _glossaries = glossaries;
        _fileTools = fileTools;
        _settings = settings;
        _logger = logger;
    }

    public async Task RunAsync(SubtitlePostProcessJob job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.TargetPath) || !File.Exists(job.TargetPath))
        {
            return;
        }

        var ready = new List<ReadyPostProcessor>();
        foreach (var processor in _processors)
        {
            var enabled = PluginCatalog.IsEnabled(
                await _settings.GetSetting(PluginCatalog.EnabledKey(processor.Provider)));
            if (!enabled)
            {
                continue;
            }

            PluginCatalog.TryNormalizePolicy(
                await _settings.GetSetting(PluginCatalog.PolicyKey(processor.Provider)),
                out var policy);
            ready.Add(new ReadyPostProcessor
            {
                Processor = processor,
                Order = PluginCatalog.ParseOrder(
                    await _settings.GetSetting(PluginCatalog.OrderKey(processor.Provider))),
                FailurePolicy = policy
            });
        }

        var original = await File.ReadAllTextAsync(job.TargetPath, cancellationToken);
        var filtered = await ApplyFilters(original, cancellationToken);
        var glossary = await GlossaryText(job.TargetLanguage, cancellationToken);
        var seed = Input(job, filtered, glossary);
        var outcome = await ExecuteAsync(filtered, ready, seed, cancellationToken, _logger);
        if (outcome.Failed)
        {
            throw new InvalidOperationException(outcome.Reason ?? "A subtitle post-processor failed.");
        }

        if (outcome.Text != original)
        {
            var temporary = job.TargetPath + ".tmp";
            await File.WriteAllTextAsync(temporary, outcome.Text, cancellationToken);
            File.Move(temporary, job.TargetPath, overwrite: true);
        }

        await RunFileTools(job.TargetPath, cancellationToken);
    }

    private async Task<string> ApplyFilters(string text, CancellationToken cancellationToken)
    {
        foreach (var filter in await Ordered(_filters, filter => filter.Provider))
        {
            try
            {
                var next = await filter.FilterAsync(text, cancellationToken);
                if (next != null)
                {
                    text = next;
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not filter the subtitle.", filter.Provider);
            }
        }

        return text;
    }

    private async Task<string?> GlossaryText(string language, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var glossary in await Ordered(_glossaries, glossary => glossary.Provider))
        {
            try
            {
                var terms = await glossary.TermsAsync(language, cancellationToken);
                if (terms == null)
                {
                    continue;
                }

                lines.AddRange(terms.Where(term => !string.IsNullOrWhiteSpace(term)));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not list glossary terms.", glossary.Provider);
            }
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private async Task RunFileTools(string targetPath, CancellationToken cancellationToken)
    {
        foreach (var tool in await Ordered(_fileTools, tool => tool.Provider))
        {
            try
            {
                await tool.RunAsync(targetPath, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "{Provider} could not finish {Path}.", tool.Provider, targetPath);
            }
        }
    }

    private async Task<List<T>> Ordered<T>(IEnumerable<T> items, Func<T, string> provider)
    {
        var ready = new List<(int Order, string Name, T Item)>();
        foreach (var item in items)
        {
            var name = provider(item);
            if (!await Enabled(name))
            {
                continue;
            }

            ready.Add((
                PluginCatalog.ParseOrder(await _settings.GetSetting(PluginCatalog.OrderKey(name))),
                name,
                item));
        }

        return ready
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Item)
            .ToList();
    }

    private async Task<bool> Enabled(string provider) =>
        PluginCatalog.IsEnabled(await _settings.GetSetting(PluginCatalog.EnabledKey(provider)));

    public static async Task<PostProcessOutcome> ExecuteAsync(
        string original,
        IReadOnlyList<ReadyPostProcessor> processors,
        SubtitlePostProcessInput seed,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        var text = original;
        var write = false;
        foreach (var step in processors
                     .OrderBy(item => KindRank(item.Processor.Kind))
                     .ThenBy(item => item.Order)
                     .ThenBy(item => item.Processor.Provider, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var result = await step.Processor.ProcessAsync(
                    Copy(seed, text),
                    cancellationToken);
                if (result.Reject)
                {
                    logger?.LogInformation(
                        "{Provider} rejected the subtitle. {Reason}",
                        step.Processor.Provider,
                        result.Reason);
                    break;
                }

                if (result.Text != null && result.Text != text)
                {
                    text = result.Text;
                    write = true;
                }
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    exception,
                    "{Provider} failed while rewriting {Path}.",
                    step.Processor.Provider,
                    seed.TargetPath);
                if (step.FailurePolicy == PluginCatalog.PolicyFail)
                {
                    return new PostProcessOutcome
                    {
                        Text = original,
                        Write = false,
                        Failed = true,
                        Reason = $"{step.Processor.Provider} failed."
                    };
                }

                if (step.FailurePolicy == PluginCatalog.PolicyStop)
                {
                    break;
                }
            }
        }

        return new PostProcessOutcome
        {
            Text = text,
            Write = write && text != original
        };
    }

    public static bool PathIsOcr(string? path)
    {
        var name = Path.GetFileNameWithoutExtension(path ?? string.Empty);
        return name.Split('.').Any(part => part.Equals("ocr", StringComparison.OrdinalIgnoreCase));
    }

    private static SubtitlePostProcessInput Input(SubtitlePostProcessJob job, string text, string? glossary) => new()
    {
        SourcePath = job.SourcePath,
        TargetPath = job.TargetPath,
        SourceLanguage = job.SourceLanguage,
        TargetLanguage = job.TargetLanguage,
        SourceIsOcr = PathIsOcr(job.SourcePath) || PathIsOcr(job.TargetPath),
        TargetText = text,
        Glossary = glossary
    };

    private static SubtitlePostProcessInput Copy(SubtitlePostProcessInput seed, string text) => new()
    {
        SourcePath = seed.SourcePath,
        TargetPath = seed.TargetPath,
        SourceLanguage = seed.SourceLanguage,
        TargetLanguage = seed.TargetLanguage,
        SourceIsOcr = seed.SourceIsOcr,
        TargetText = text,
        Glossary = seed.Glossary
    };

    private static int KindRank(SubtitlePostProcessKind kind) => kind switch
    {
        SubtitlePostProcessKind.Language => 0,
        SubtitlePostProcessKind.Style => 1,
        SubtitlePostProcessKind.LineFit => 2,
        SubtitlePostProcessKind.QualityGate => 3,
        _ => 9
    };
}
