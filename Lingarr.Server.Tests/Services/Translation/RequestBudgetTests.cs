using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class RequestBudgetTests
{
    [Fact]
    public void Remaining_ShrinksWithTheClock()
    {
        var time = new ManualTimeProvider();
        using var budget = new RequestBudget(TimeSpan.FromSeconds(10), CancellationToken.None, time);

        time.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal(TimeSpan.FromSeconds(6), budget.Remaining);
        Assert.False(budget.Exhausted);
    }

    [Fact]
    public void Exhausted_WhenTheBudgetIsSpent()
    {
        var time = new ManualTimeProvider();
        using var budget = new RequestBudget(TimeSpan.FromSeconds(10), CancellationToken.None, time);

        time.Advance(TimeSpan.FromSeconds(12));

        Assert.Equal(TimeSpan.Zero, budget.Remaining);
        Assert.True(budget.Exhausted);
    }

    [Fact]
    public async Task DelayAsync_ThatOutlivesTheBudget_ThrowsInsteadOfWaiting()
    {
        using var budget = new RequestBudget(TimeSpan.FromMilliseconds(60), CancellationToken.None);
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<RequestBudgetExhaustedException>(
            () => budget.DelayAsync(TimeSpan.FromSeconds(5), CancellationToken.None));

        // The wait ended with the deadline, not with the requested five seconds.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"waited {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task DelayAsync_WithinTheBudget_Returns()
    {
        using var budget = new RequestBudget(TimeSpan.FromSeconds(5), CancellationToken.None);

        await budget.DelayAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);

        Assert.False(budget.Exhausted);
    }

    [Fact]
    public async Task DelayAsync_WhenTheCallerCancels_StaysACancellation()
    {
        using var caller = new CancellationTokenSource();
        using var budget = new RequestBudget(TimeSpan.FromMinutes(5), caller.Token);
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => budget.DelayAsync(TimeSpan.FromSeconds(5), caller.Token));
    }

    [Fact]
    public async Task Token_IsCancelledWhenTheBudgetExpires()
    {
        using var budget = new RequestBudget(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        var cancelled = new TaskCompletionSource();
        using var registration = budget.Token.Register(() => cancelled.TrySetResult());

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(budget.Token.IsCancellationRequested);
    }

    /// <summary>A clock the test moves by hand, in the same tick unit the budget reads.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan delta) => _ticks += delta.Ticks;
    }
}
