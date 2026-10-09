namespace Lingarr.Contracts.Plugins;

/// <summary>
/// Runs a button declared on a plugin panel. The message is shown to the operator.
/// </summary>
public interface IPluginActionHandler
{
    string Provider { get; }

    Task<string> ExecuteAsync(string actionId, CancellationToken cancellationToken);
}
