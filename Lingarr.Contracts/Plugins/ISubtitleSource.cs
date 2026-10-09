namespace Lingarr.Contracts.Plugins;

/// <summary>
/// Supplies a source subtitle file beside the video. Lingarr asks only when
/// Bazarr is off, or Bazarr has already missed.
/// </summary>
public interface ISubtitleSource
{
    string Provider { get; }

    Task<bool> TrySupplyAsync(SubtitleSourceRequest request, CancellationToken cancellationToken);
}

public sealed class SubtitleSourceRequest
{
    public required string Directory { get; init; }
    public required string MediaFileName { get; init; }
    public required string Language { get; init; }
}
