namespace Lingarr.Contracts.Plugins;

/// <summary>
/// Turns one embedded track into a text subtitle when the built-in tools miss.
/// </summary>
public interface IExtractTool
{
    string Provider { get; }

    IReadOnlyList<string> Codecs { get; }

    Task<bool> TryExtractAsync(ExtractToolRequest request, CancellationToken cancellationToken);
}

public sealed class ExtractToolRequest
{
    public required string Directory { get; init; }
    public required string MediaFileName { get; init; }
    public required string Codec { get; init; }
}
