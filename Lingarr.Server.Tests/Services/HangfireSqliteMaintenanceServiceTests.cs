using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Lingarr.Server.Services;

namespace Lingarr.Server.Tests.Services;

public class HangfireSqliteMaintenanceServiceTests
{
    [Fact]
    public void TryRecoverCorruptDatabase_HealthyDatabase_IsLeftInPlace()
    {
        var path = CreateHealthyDatabase(rows: 25);
        try
        {
            var quarantined = HangfireSqliteMaintenanceService.TryRecoverCorruptDatabase(
                path, NullLogger.Instance);

            Assert.False(quarantined);
            Assert.True(File.Exists(path));
            Assert.Empty(QuarantineFiles(path));
            Assert.Equal(25, CountRows(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void TryRecoverCorruptDatabase_IndexDamage_IsRebuiltInPlace()
    {
        // The failure seen four times on Bedroom: indices no longer match their tables while the
        // data itself is fine. Rebuilding the index keeps the job history.
        var path = CreateDamagedIndexDatabase();
        try
        {
            var quarantined = HangfireSqliteMaintenanceService.TryRecoverCorruptDatabase(
                path, NullLogger.Instance);

            Assert.False(quarantined);
            Assert.True(File.Exists(path));
            Assert.Empty(QuarantineFiles(path));
            Assert.Equal(200, CountRows(path));
            Assert.Equal("ok", IntegrityCheck(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void TryRecoverCorruptDatabase_NotADatabase_IsQuarantined()
    {
        var path = CreateTempPath();
        File.WriteAllText(path, "this file is not a SQLite database, it is a leftover log");
        try
        {
            var quarantined = HangfireSqliteMaintenanceService.TryRecoverCorruptDatabase(
                path, NullLogger.Instance);

            Assert.True(quarantined);
            Assert.False(File.Exists(path));
            Assert.Single(QuarantineFiles(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void TryRecoverCorruptDatabase_MissingFile_IsNotQuarantined()
    {
        var path = CreateTempPath();

        var quarantined = HangfireSqliteMaintenanceService.TryRecoverCorruptDatabase(
            path, NullLogger.Instance);

        Assert.False(quarantined);
        Assert.Empty(QuarantineFiles(path));
    }

    private static string CreateHealthyDatabase(int rows)
    {
        var path = CreateTempPath();
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY, v TEXT);";
        command.ExecuteNonQuery();
        InsertRows(connection, rows);
        return path;
    }

    private static string CreateDamagedIndexDatabase()
    {
        var path = CreateTempPath();
        using (var connection = Open(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY, v TEXT); CREATE INDEX ix ON t(id);";
            command.ExecuteNonQuery();
            InsertRows(connection, 200);
        }

        using (var connection = Open(path))
        using (var command = connection.CreateCommand())
        {
            // Point the stored schema at another column, so every index entry no longer matches the
            // table: PRAGMA integrity_check then reports "row N missing from index ix".
            command.CommandText =
                "PRAGMA writable_schema=ON; UPDATE sqlite_master SET sql='CREATE INDEX ix ON t(v)' WHERE name='ix'; " +
                "PRAGMA schema_version=2; PRAGMA writable_schema=OFF;";
            command.ExecuteNonQuery();
        }

        Assert.NotEqual("ok", IntegrityCheck(path));
        return path;
    }

    private static void InsertRows(SqliteConnection connection, int rows)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO t(v) VALUES ($v);";
        var value = command.CreateParameter();
        value.ParameterName = "$v";
        command.Parameters.Add(value);
        for (var index = 0; index < rows; index++)
        {
            value.Value = $"row {index}";
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static SqliteConnection Open(string path)
    {
        // Pooling would hand back a handle that still caches the pre-damage schema.
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }

    private static string IntegrityCheck(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    private static int CountRows(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM t;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), $"hangfire-maintenance-{Guid.NewGuid():N}.db");

    private static string[] QuarantineFiles(string path) =>
        Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".corrupt-*");

    private static void Delete(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" }.Concat(QuarantineFiles(path)))
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // A leftover temp file never fails the suite.
            }
        }
    }
}
