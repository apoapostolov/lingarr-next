using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class SqliteWriteRetryTests
{
    private static readonly TimeSpan[] FastDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400)
    ];

    [Fact]
    public async Task TrySaveAsync_WaitsOutAWriterThatHoldsTheLock()
    {
        var fixture = await LockedDatabaseAsync();
        try
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(150);
                await fixture.BlockerTransaction.CommitAsync();
            });

            var saved = await SqliteWriteRetry.TrySaveAsync(
                fixture.Context,
                NullLogger.Instance,
                "a health reading",
                CancellationToken.None,
                FastDelays);

            Assert.True(saved);
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
                NullLogger.Instance,
                "a health reading",
                CancellationToken.None,
                [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)]);

            Assert.False(saved);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryRunAsync_ReportsFailureInsteadOfThrowingForABulkWrite()
    {
        var fixture = await LockedDatabaseAsync();
        try
        {
            var ran = await SqliteWriteRetry.TryRunAsync(
                token => fixture.Context.ProviderHealthSnapshots
                    .Where(item => item.Provider == "microsoft")
                    .ExecuteDeleteAsync(token),
                NullLogger.Instance,
                "a retention delete",
                CancellationToken.None,
                [TimeSpan.FromMilliseconds(50)]);

            Assert.False(ran);
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

    /// <summary>
    /// A context holding an unsaved reading, plus a second connection that has taken the write
    /// lock, which is the state a library sync leaves the database in.
    /// </summary>
    private static async Task<LockedDatabase> LockedDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lingarr-write-retry-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            // A zero command timeout means "wait forever" in Microsoft.Data.Sqlite, which turns the
            // contention this class exists to survive into a hang. One second bounds each attempt.
            .UseSqlite($"Data Source={path};Default Timeout=1")
            .Options;

        var context = new LingarrDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var blocker = new SqliteConnection($"Data Source={path}");
        await blocker.OpenAsync();
        var transaction = blocker.BeginTransaction(deferred: false);

        context.ProviderHealthSnapshots.Add(new ProviderHealthSnapshot
        {
            Provider = "microsoft",
            State = "healthy",
            Reason = "A recent translation completed successfully."
        });

        return new LockedDatabase(context, blocker, transaction, path);
    }

    private sealed class LockedDatabase(
        LingarrDbContext context,
        SqliteConnection blocker,
        SqliteTransaction blockerTransaction,
        string path) : IAsyncDisposable
    {
        public LingarrDbContext Context { get; } = context;
        public SqliteConnection Blocker { get; } = blocker;
        public SqliteTransaction BlockerTransaction { get; } = blockerTransaction;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await BlockerTransaction.DisposeAsync();
            }
            catch (SqliteException)
            {
                // The give-up test leaves the transaction open on purpose.
            }

            await Blocker.DisposeAsync();
            await Context.DisposeAsync();
            SqliteConnection.ClearAllPools();

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
    }
}
