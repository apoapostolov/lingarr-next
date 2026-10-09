namespace Lingarr.Contracts.Plugins;

/// <summary>
/// Runs one program for a plugin. Arguments are separate. A shell is refused.
/// </summary>
public interface IPluginCommand
{
    Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
