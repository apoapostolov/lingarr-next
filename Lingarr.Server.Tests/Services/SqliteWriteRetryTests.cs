using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class SqliteWriteRetryTests
{
    [Fact]
    public async Task TrySaveAsync_RetriesAfterTheLockIsReleased()
    {
        var fixture = await LockedDatabaseAsync();
        try
        {
            // Hold the lock past the first attempt's two-second budget, so the save that lands is a
            // retry rather than a first try that simply waited the lock out.
            var release = Task.Run(async () =>
            {
                await Task.Delay(2500);
                await fixture.BlockerTransaction!.CommitAsync();
            });

            var saved = await SqliteWriteRetry.TrySaveAsync(
                fixture.Context,
                fixture.Logger,
                "a health reading",
                CancellationToken.None,
                [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(400)]);

            await release;

            Assert.True(saved);
            Assert.Contains(fixture.Logger.Messages, message => message.Contains("retrying in"));
            Assert.Equal(1, await fixture.Context.ProviderHealthSnapshots.CountAsync());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task TrySaveAsync_ReportsFailureWhenTheLockOutlivesTheRetries()
    {
        var fixture = await LockedDatabaseAsync();
        try
        {
            var saved = await SqliteWriteRetry.TrySaveAsync(
                fixture.Context,
                fixture.Logger,
                "a health reading",
                CancellationToken.None,
                [TimeSpan.FromMilliseconds(50)]);

            Assert.False(saved);
            Assert.Contains(fixture.Logger.Messages, message => message.Contains("stayed locked"));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task TrySaveAsync_RestoresTheCommandTimeout()
    {
        var database = await DatabaseAsync();
        try
        {
            database.Context.ProviderHealthSnapshots.Add(Reading());

            var saved = await SqliteWriteRetry.TrySaveAsync(
                database.Context,
                database.Logger,
                "a health reading",
                CancellationToken.None);

            Assert.True(saved);
            Assert.Null(database.Context.Database.GetCommandTimeout());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryRunAsync_ReportsFailureInsteadOfThrowingForABulkWrite()
    {
        var fixture = await LockedDatabaseAsync();
        try
        {
            var attempts = 0;
            var ran = await SqliteWriteRetry.TryRunAsync(
                token =>
                {
                    attempts++;
                    return fixture.Context.ProviderHealthSnapshots
                        .Where(item => item.Provider == "microsoft")
                        .ExecuteDeleteAsync(token);
                },
                fixture.Logger,
                "a retention delete",
                CancellationToken.None,
                [TimeSpan.FromMilliseconds(50)]);

            Assert.False(ran);
            Assert.True(attempts > 1, $"the write ran {attempts} time(s), so no retry happened");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public void IsBusy_SeesThroughTheWrappingException()
    {
        var busy = new SqliteException("SQLite Error 5: 'database is locked'.", 5);

        Assert.True(SqliteWriteRetry.IsBusy(busy));
        Assert.True(SqliteWriteRetry.IsBusy(new InvalidOperationException("outer", busy)));
        Assert.False(SqliteWriteRetry.IsBusy(new SqliteException("SQLite Error 1: 'no such table'.", 1)));
        Assert.False(SqliteWriteRetry.IsBusy(new InvalidOperationException("plain")));
    }

    private static ProviderHealthSnapshot Reading() => new()
    {
        Provider = "microsoft",
        State = "healthy",
        Reason = "A recent translation completed successfully."
    };

    /// <summary>
    /// A context holding an unsaved reading, plus a second connection that has taken the write
    /// lock, which is the state a library sync leaves the database in.
    /// </summary>
    private static async Task<TestDatabase> LockedDatabaseAsync()
    {
        var database = await DatabaseAsync();
        database.Blocker = new SqliteConnection($"Data Source={database.Path}");
        await database.Blocker.OpenAsync();
        database.BlockerTransaction = database.Blocker.BeginTransaction(deferred: false);
        database.Context.ProviderHealthSnapshots.Add(Reading());
        return database;
    }

    private static async Task<TestDatabase> DatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lingarr-write-retry-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            // A zero command timeout means "wait forever" in Microsoft.Data.Sqlite, which would turn
            // the contention this class exists to survive into a hang.
            .UseSqlite($"Data Source={path};Default Timeout=1")
            .Options;

        var database = new TestDatabase(new LingarrDbContext(options), path);
        await database.Context.Database.EnsureCreatedAsync();
        return database;
    }

    private sealed class TestDatabase(LingarrDbContext context, string path) : IAsyncDisposable
    {
        public LingarrDbContext Context { get; } = context;
        public string Path { get; } = path;
        public RecordingLogger Logger { get; } = new();
        public SqliteConnection? Blocker { get; set; }
        public SqliteTransaction? BlockerTransaction { get; set; }

        public async ValueTask DisposeAsync()
        {
            if (BlockerTransaction is not null)
            {
                await BlockerTransaction.DisposeAsync();
            }

            if (Blocker is not null)
            {
                await Blocker.DisposeAsync();
            }

            await Context.DisposeAsync();
            SqliteConnection.ClearAllPools();

            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
