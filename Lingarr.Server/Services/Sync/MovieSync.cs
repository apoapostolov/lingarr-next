using System.Globalization;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Contracts.Plugins;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Services.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Sync;

public class MovieSync : IMovieSync
{
    private readonly LingarrDbContext _dbContext;
    private readonly PathConversionService _pathConversionService;
    private readonly ILogger<MovieSync> _logger;
    private readonly IImageSync _imageSync;
    private readonly PluginShelf _shelf;

    public MovieSync(
        LingarrDbContext dbContext,
        PathConversionService pathConversionService,
        ILogger<MovieSync> logger,
        IImageSync imageSync,
        PluginShelf shelf)
    {
        _dbContext = dbContext;
        _pathConversionService = pathConversionService;
        _logger = logger;
        _imageSync = imageSync;
        _shelf = shelf;
    }

    /// <inheritdoc />
    public async Task<Movie?> SyncMovie(RadarrMovie movie, bool defaultInclude = true)
    {
        if (!movie.HasFile)
        {
            _logger.LogDebug("Movie '{Title}' (ID: {Id}) has no file, skipping.", movie.Title, movie.Id);
            return null;
        }

        var movieEntity = await _dbContext.Movies
            .Include(m => m.Images)
            .FirstOrDefaultAsync(m => m.RadarrId == movie.Id);

        var moviePath = await _shelf.ResolvePathAsync(
            _pathConversionService.ConvertAndMapPath(
                movie.MovieFile.Path ?? string.Empty,
                MediaType.Movie),
            new LibraryQuery
            {
                Kind = "movie",
                ExternalId = movie.Id.ToString(),
                Title = movie.Title
            });

        if (movieEntity == null)
        {
            movieEntity = new Movie
            {
                RadarrId = movie.Id,
                Title = movie.Title,
                DateAdded = DateTime.Parse(movie.Added, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                FileName = Path.GetFileNameWithoutExtension(moviePath),
                Path = Path.GetDirectoryName(moviePath) ?? string.Empty,
                IncludeInTranslation = defaultInclude
            };
            _dbContext.Movies.Add(movieEntity);
        }
        else
        {
            movieEntity.Title = movie.Title;
            movieEntity.DateAdded = DateTime.Parse(movie.Added, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            movieEntity.FileName = Path.GetFileNameWithoutExtension(moviePath);
            movieEntity.Path = Path.GetDirectoryName(moviePath) ?? string.Empty;
        }

        _logger.LogDebug("Syncing movie: {MovieId} with Path: {Path}", movie.Id, movieEntity.Path);

        if (movie.Images?.Any() == true)
        {
            _imageSync.SyncImages(movieEntity.Images, movie.Images);
        }

        return movieEntity;
    }
}