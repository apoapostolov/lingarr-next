using Lingarr.Server.Models.Integrations;

namespace Lingarr.Server.Services.Sync;

public static class UnmonitoredScan
{
    public static List<RadarrMovie> MoviesToSync(
        IEnumerable<RadarrMovie> movies,
        bool excludeUnmonitored)
    {
        return excludeUnmonitored
            ? movies.Where(movie => movie.Monitored).ToList()
            : movies.ToList();
    }

    public static List<SonarrShow> ShowsToSync(
        IEnumerable<SonarrShow> shows,
        bool excludeUnmonitored)
    {
        return excludeUnmonitored
            ? shows.Where(show => show.Monitored).ToList()
            : shows.ToList();
    }

    public static List<SonarrEpisode> EpisodesToImport(
        IEnumerable<SonarrEpisode> episodes,
        bool excludeUnmonitored)
    {
        var source = excludeUnmonitored
            ? episodes.Where(episode => episode.Monitored)
            : episodes;
        return source.Where(episode => episode.HasFile).ToList();
    }
}
