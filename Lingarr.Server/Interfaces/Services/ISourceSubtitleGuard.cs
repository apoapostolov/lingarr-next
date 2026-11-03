using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Rejects a source subtitle that cannot produce a usable translation: a typesetting or drawing
/// track instead of dialogue, and a translation that came back unchanged because the file was not
/// in the declared source language.
/// </summary>
public interface ISourceSubtitleGuard
{
    /// <summary>
    /// Runs before translation starts, on the parsed source lines.
    /// </summary>
    SubtitleSourceVerdict InspectSource(IReadOnlyList<SubtitleItem> subtitles);

    /// <summary>
    /// Runs after translation, before the translated file is written.
    /// </summary>
    SubtitleSourceVerdict InspectTranslation(
        IReadOnlyList<SubtitleItem> subtitles,
        string sourceLanguage);
}

/// <summary>
/// The verdict of <see cref="ISourceSubtitleGuard"/>. A blocked verdict carries the reason that is
/// shown to the user, so it has to name the file problem and the way out.
/// </summary>
public sealed record SubtitleSourceVerdict(bool Blocked, string? Reason)
{
    public static readonly SubtitleSourceVerdict Allowed = new(false, null);

    public static SubtitleSourceVerdict Refuse(string reason) => new(true, reason);
}
