namespace Lingarr.Contracts.Plugins;

public interface IContentFilter
{
    string Provider { get; }

    Task<string> FilterAsync(string subtitleText, CancellationToken cancellationToken);
}

public interface IGlossary
{
    string Provider { get; }

    Task<IReadOnlyList<string>> TermsAsync(string language, CancellationToken cancellationToken);
}

public interface IFileTool
{
    string Provider { get; }

    Task RunAsync(string targetPath, CancellationToken cancellationToken);
}

public interface ILogSink
{
    string Provider { get; }

    void Write(string level, string category, string message);
}

public interface IPluginHealthCheck
{
    string Provider { get; }

    Task<string> ProbeAsync(CancellationToken cancellationToken);
}

public interface IDashboardWidget
{
    string Provider { get; }

    string Title { get; }

    Task<string> TextAsync(CancellationToken cancellationToken);
}

public interface IMediaEventSink
{
    string Provider { get; }

    Task OnDiscoveredAsync(string? directory, string? fileName, CancellationToken cancellationToken);
}
