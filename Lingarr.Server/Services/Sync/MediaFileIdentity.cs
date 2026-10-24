using Lingarr.Core.Entities;
using Lingarr.Core.Interfaces;

namespace Lingarr.Server.Services.Sync;

public static class MediaFileIdentity
{
    public static void Assign(IMedia media, string fileName, string? directory)
    {
        var changed = !string.Equals(media.FileName, fileName, StringComparison.Ordinal)
            || !string.Equals(media.Path, directory, StringComparison.Ordinal);
        if (changed)
        {
            media.MediaHash = null;
            switch (media)
            {
                case Movie movie:
                    movie.LanguageCoverage = null;
                    movie.DiskStamp = null;
                    break;
                case Episode episode:
                    episode.LanguageCoverage = null;
                    episode.DiskStamp = null;
                    break;
            }
        }

        media.FileName = fileName;
        media.Path = directory;
    }
}
