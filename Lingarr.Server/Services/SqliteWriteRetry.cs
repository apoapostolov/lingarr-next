using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lingarr.Server.Services;

/// <summary>
/// Saves EF changes through SQLite write contention.
/// </summary>
/// <remarks>
/// The database has one writer at a time, and a library sync holds that lock for as long as its
/// batch takes: a hundred shows measured about twenty seconds. The busy timeout alone turns such
/// a wait into a failed request, so a save that meets a locked database is retried with backoff,
/// and one that still cannot land is reported as an outcome instead of being thrown at the caller.
/// </remarks>
internal static class SqliteWriteRetry
{
    /// <summary>SQLITE_BUSY and SQLITE_LOCKED, the two errors a concurrent writer produces.</summary>
    private const int BusyErrorCode = 5;
    private const int LockedErrorCode = 6;

    internal static readonly TimeSpan[] DefaultDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(750),
        TimeSpan.FromMilliseconds(2000)
    ];

    /// <summary>
    /// Saves the context, retrying while the database is locked. Returns false when the lock
    /// outlives the retries; the caller decides whether the loss matters.
    /// </summary>
    internal static Task<bool> TrySaveAsync(
        DbContext context,
        ILogger logger,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyList<TimeSpan>? delays = null) =>
        TryRunAsync(
            token => context.SaveChangesAsync(token),
            logger,
            operation,
            cancellationToken,
            delays);

    /// <summary>
    /// Runs a write that does not go through SaveChanges, such as a bulk delete, under the same
    /// retry policy.
    /// </summary>
    internal static async Task<bool> TryRunAsync(
        Func<CancellationToken, Task> work,
        ILogger logger,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyList<TimeSpan>? delays = null)
    {
        var schedule = delays ?? DefaultDelays;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await work(cancellationToken);
                return true;
            }
            catch (Exception exception) when (IsBusy(exception))
            {
                if (attempt >= schedule.Count)
                {
                    logger.LogWarning(
                        exception,
                        "Gave up on {Operation} after {Attempts} attempts because the database stayed locked.",
                        operation,
                        attempt + 1);
                    return false;
                }

                logger.LogWarning(
                    "The database was locked during {Operation} (attempt {Attempt}); retrying in {Delay} ms.",
                    operation,
                    attempt + 1,
                    schedule[attempt].TotalMilliseconds);
                await Task.Delay(schedule[attempt], cancellationToken);
            }
        }
    }

    /// <summary>True when the exception, at any depth, is SQLite refusing a write to a locked file.</summary>
    internal static bool IsBusy(Exception exception)
    {
        if (exception is SqliteException sqlite)
        {
            return sqlite.SqliteErrorCode is BusyErrorCode or LockedErrorCode;
        }

        return exception.InnerException is not null && IsBusy(exception.InnerException);
    }
}
