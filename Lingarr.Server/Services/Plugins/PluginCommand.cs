using Lingarr.Contracts.Plugins;

namespace Lingarr.Server.Services.Plugins;

internal sealed class PluginCommand : IPluginCommand
{
    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await ExternalCommandRunner.RunAsync(new ExternalCommandRequest
        {
            FileName = fileName,
            Arguments = arguments,
            Timeout = timeout
        }, cancellationToken);
        return result.TimedOut ? -1 : result.ExitCode;
    }
}
