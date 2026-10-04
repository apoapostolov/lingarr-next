namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Reads release information from the Bedroom fork repository.
/// </summary>
public interface ILingarrApiService
{
    /// <summary>
    /// Gets the latest semantic release or tag from apoapostolov/lingarr-next.
    /// </summary>
    /// <returns>Version string or null if unavailable</returns>
    Task<string?> GetLatestVersion();
}
