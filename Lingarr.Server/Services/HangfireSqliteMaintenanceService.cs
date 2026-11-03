using Microsoft.Data.Sqlite;

namespace Lingarr.Server.Services;

/// <summary>
/// Periodically checkpoints Hangfire's SQLite WAL so it does not grow without bound
/// (observed multi‑GB WAL files with few jobs on Bedroom).
/// </summary>
public sealed class HangfireSqliteMaintenanceService : BackgroundService
{
    /// <summary>SQLITE_CORRUPT: the file is damaged and cannot be read.</summary>
    private const int SqliteCorrupt = 11;

    /// <summary>SQLITE_NOTADB: the file is not a SQLite database.</summary>
    private const int SqliteNotADatabase = 26;

    private readonly ILogger<HangfireSqliteMaintenanceService> _logger;
    private readonly string? _sqlitePath;
    private readonly TimeSpan _interval;

    public HangfireSqliteMaintenanceService(ILogger<HangfireSqliteMaintenanceService> logger)
    {
        _logger = logger;
        var dbConnection = Environment.GetEnvironmentVariable("DB_CONNECTION")?.ToLowerInvariant() ?? "sqlite";
        if (dbConnection is "mysql" or "postgres" or "postgresql")
        {
            _sqlitePath = null;
        }
        else
        {
            _sqlitePath = Environment.GetEnvironmentVariable("DB_HANGFIRE_SQLITE_PATH")
                          ?? "/app/config/Hangfire.db";
        }

        var minutes = 15;
        if (int.TryParse(Environment.GetEnvironmentVariable("HANGFIRE_WAL_CHECKPOINT_MINUTES"), out var configured)
            && configured > 0)
        {
            minutes = configured;
        }

        _interval = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_sqlitePath == null)
        {
            _logger.LogDebug("Hangfire SQLite maintenance disabled (non-SQLite Hangfire storage).");
            return;
        }

        // First checkpoint shortly after start (jobs may have opened WAL already).
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CheckpointWal(_sqlitePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Hangfire SQLite WAL checkpoint failed for {Path}", _sqlitePath);
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void CheckpointWal(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var walPath = path + "-wal";
        long walBytesBefore = 0;
        if (File.Exists(walPath))
        {
            walBytesBefore = new FileInfo(walPath).Length;
        }

        using var connection = OpenConnection(path);
        using var command = connection.CreateCommand();
        // PASSIVE copies what it can without waiting for readers and never resets the WAL under a
        // writer. The TRUNCATE mode used here before needs exclusive access, which is what a busy
        // Hangfire store can never give it.
        command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
        using var reader = command.ExecuteReader();
        // result columns: busy, log, checkpointed
        var busy = 0;
        var log = 0;
        var checkpointed = 0;
        if (reader.Read())
        {
            busy = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            log = reader.FieldCount > 1 && !reader.IsDBNull(1) ? reader.GetInt32(1) : 0;
            checkpointed = reader.FieldCount > 2 && !reader.IsDBNull(2) ? reader.GetInt32(2) : 0;
        }

        long walBytesAfter = File.Exists(walPath) ? new FileInfo(walPath).Length : 0;
        if (walBytesBefore > 8 * 1024 * 1024 || walBytesAfter > 8 * 1024 * 1024 || busy != 0)
        {
            _logger.LogInformation(
                "Hangfire WAL checkpoint: path={Path} busy={Busy} log={Log} checkpointed={Checkpointed} walBefore={Before} walAfter={After}",
                path, busy, log, checkpointed, walBytesBefore, walBytesAfter);
        }
        else
        {
            _logger.LogDebug(
                "Hangfire WAL checkpoint: path={Path} busy={Busy} walAfter={After}",
                path, busy, walBytesAfter);
        }
    }

    /// <summary>
    /// If Hangfire.db cannot be opened as SQLite, rebuild it in place when only indexes are damaged,
    /// and quarantine the files only when the database itself is unreadable.
    /// </summary>
    /// <remarks>
    /// A failed open is not proof of corruption: a busy, locked or briefly unreadable file looks the
    /// same from here. Quarantining on that would trade the whole job history for a transient lock,
    /// so only a corruption error or a failed integrity_check after an index rebuild quarantines.
    /// </remarks>
    public static bool TryRecoverCorruptDatabase(string sqliteDbPath, ILogger logger)
    {
        if (!File.Exists(sqliteDbPath))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection(sqliteDbPath);
            var failure = CheckIntegrity(connection);
            if (failure == null)
            {
                return false;
            }

            if (TryRepair(connection, logger) && CheckIntegrity(connection) == null)
            {
                logger.LogWarning(
                    "Hangfire SQLite index damage at {Path} was rebuilt in place; the job history is kept.",
                    sqliteDbPath);
                return false;
            }

            logger.LogError(
                "Hangfire SQLite integrity_check failed ({Result}). Quarantining database files so a fresh store can be created.",
                failure);
        }
        catch (SqliteException ex) when (IsCorruption(ex))
        {
            logger.LogError(ex,
                "Hangfire SQLite database at {Path} is unreadable. Quarantining files for recreation.",
                sqliteDbPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Hangfire SQLite database at {Path} could not be checked. It is left in place and Hangfire opens it next.",
                sqliteDbPath);
            return false;
        }

        Quarantine(sqliteDbPath, logger);
        return true;
    }

    private static SqliteConnection OpenConnection(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            // Private cache: shared cache makes one busy connection fail the whole open, and it is
            // not what a single-process Hangfire store needs.
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=30000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static string? CheckIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = command.ExecuteScalar()?.ToString();
        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase) ? null : result;
    }

    /// <summary>
    /// Rebuilds every index from its table, then rewrites the file when index damage left page
    /// accounting behind. Damage of the "row missing from index" kind lives only in the index, so
    /// this restores a usable database instead of discarding it.
    /// </summary>
    private static bool TryRepair(SqliteConnection connection, ILogger logger)
    {
        try
        {
            using var rebuild = connection.CreateCommand();
            rebuild.CommandText = "REINDEX;";
            rebuild.ExecuteNonQuery();

            if (CheckIntegrity(connection) == null)
            {
                return true;
            }

            // REINDEX alone can leave a freed page unaccounted for; VACUUM rewrites the file from
            // the surviving data, which is the same operation SQLite's recovery tools use.
            using var rewrite = connection.CreateCommand();
            rewrite.CommandText = "VACUUM;";
            rewrite.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rebuilding Hangfire SQLite indexes failed.");
            return false;
        }
    }

    private static bool IsCorruption(SqliteException exception) =>
        exception.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase;

    private static void Quarantine(string sqliteDbPath, ILogger logger)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var source = sqliteDbPath + suffix;
            if (!File.Exists(source))
            {
                continue;
            }

            var dest = $"{sqliteDbPath}.corrupt-{stamp}{suffix}";
            try
            {
                File.Move(source, dest, overwrite: true);
                logger.LogWarning("Quarantined Hangfire file {Source} -> {Dest}", source, dest);
            }
            catch (Exception moveEx)
            {
                logger.LogError(moveEx, "Failed to quarantine Hangfire file {Source}", source);
            }
        }
    }
}
