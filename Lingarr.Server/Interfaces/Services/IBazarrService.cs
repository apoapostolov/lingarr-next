using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;

namespace Lingarr.Server.Interfaces.Services;

public interface IBazarrService
{
    Task<bool> IsEnabled();

    Task<(bool Ok, string Message)> Test(CancellationToken cancellationToken);

    /// <summary>
    /// Asks Bazarr to download the highest scored source-language subtitle.
    /// Returns true when that subtitle is then visible next to the media file.
    /// </summary>
    Task<bool> TryEnsureSource(
        IMedia media,
        MediaType mediaType,
        IReadOnlySet<string> sourceLanguages,
        CancellationToken cancellationToken);
}
