namespace Lingarr.Contracts.Plugins;

public interface IPathMapper
{
    string Provider { get; }

    string? Map(string path, string mediaType);
}

public interface IRetryPolicy
{
    string Provider { get; }

    Task<RetryAdvice?> AdviseAsync(string kind, string? fileName, int attempt, CancellationToken cancellationToken);
}

public sealed class RetryAdvice
{
    public bool Retry { get; init; } = true;

    public int? DelayMinutes { get; init; }
}

public interface IListBadge
{
    string Provider { get; }

    string? Label(string? subtitlePath);
}

public interface IPromptContributor
{
    string Provider { get; }

    string Name { get; }

    string? Block(string? sourceLanguage, string? targetLanguage);
}

public interface IStatisticsExporter
{
    string Provider { get; }

    Task ExportAsync(StatisticsSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed class StatisticsSnapshot
{
    public int Movies { get; init; }

    public int Episodes { get; init; }

    public int SubtitleFiles { get; init; }
}

public interface ISubtitleMerge
{
    string Provider { get; }

    Task<SubtitleMergeChoice> ChooseAsync(
        string existingPath,
        string incomingPath,
        bool existingIsOcr,
        CancellationToken cancellationToken);
}

public enum SubtitleMergeChoice
{
    Default = 0,
    Keep = 1,
    Replace = 2,
    Queue = 3
}

public interface ISidecarCodec
{
    string Provider { get; }

    string Extension { get; }

    Task<string?> ReadAsync(string path, CancellationToken cancellationToken);

    Task WriteAsync(string path, string srtText, CancellationToken cancellationToken);
}

public interface ICaptionPolicy
{
    string Provider { get; }

    Task<CaptionDecision?> DecideAsync(string path, string? caption, CancellationToken cancellationToken);
}

public sealed class CaptionDecision
{
    public string Kind { get; init; } = "plain";

    public bool Eligible { get; init; } = true;
}

public interface ILibraryAgent
{
    string Provider { get; }

    Task<LibraryHit?> FindAsync(LibraryQuery query, CancellationToken cancellationToken);
}

public sealed class LibraryQuery
{
    public string Kind { get; init; } = "movie";

    public string? ExternalId { get; init; }

    public string? Title { get; init; }
}

public sealed class LibraryHit
{
    public string? Directory { get; init; }

    public string? FileName { get; init; }
}
