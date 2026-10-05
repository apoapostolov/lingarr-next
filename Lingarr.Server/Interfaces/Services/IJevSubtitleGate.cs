namespace Lingarr.Server.Interfaces.Services;

public interface IJevSubtitleGate
{
    Task<bool> SkipEnabled(CancellationToken cancellationToken);

    Task<bool> RejectEnabled(CancellationToken cancellationToken);

    /// <summary>
    /// Positions whose text is a sound cue or a credit, confident enough to leave untranslated.
    /// Empty when the switch is off, the key is missing, or Jev cannot be reached.
    /// </summary>
    Task<IReadOnlySet<int>> PositionsToSkip(
        IReadOnlyList<(int Position, string Text)> lines,
        CancellationToken cancellationToken);

    /// <summary>
    /// Positions whose translation is a refusal or a copy rather than a translation.
    /// Empty on the same fail-open conditions as <see cref="PositionsToSkip"/>.
    /// </summary>
    Task<IReadOnlySet<int>> PositionsToReject(
        IReadOnlyList<(int Position, string Source, string Translation)> lines,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken);
}
