using System;
using System.Collections.Generic;
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
    public void Token_IsCancelledWhenTheClockPassesTheDeadline()
    {
        var time = new ManualTimeProvider();
        using var budget = new RequestBudget(TimeSpan.FromSeconds(10), CancellationToken.None, time);

        time.Advance(TimeSpan.FromSeconds(11));

        Assert.True(budget.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task DelayAsync_ThatOutlivesTheBudget_ThrowsInsteadOfWaiting()
    {
        var time = new ManualTimeProvider();
        using var budget = new RequestBudget(TimeSpan.FromMilliseconds(60), CancellationToken.None, time);
        var pending = budget.DelayAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        time.Advance(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<RequestBudgetExhaustedException>(() => pending);
    }

    [Fact]
    public async Task DelayAsync_WithinTheBudget_Returns()
    {
        var time = new ManualTimeProvider();
        using var budget = new RequestBudget(TimeSpan.FromSeconds(5), CancellationToken.None, time);
        var pending = budget.DelayAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);

        time.Advance(TimeSpan.FromMilliseconds(20));

        await pending;
        Assert.False(budget.Exhausted);
    }

    [Fact]
    public async Task DelayAsync_WhenTheCallerCancels_StaysACancellation()
    {
        var time = new ManualTimeProvider();
        using var caller = new CancellationTokenSource();
        using var budget = new RequestBudget(TimeSpan.FromMinutes(5), caller.Token, time);
        var pending = budget.DelayAsync(TimeSpan.FromSeconds(5), caller.Token);

        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(budget.Exhausted);
    }

    /// <summary>
    /// A clock the test moves by hand. It also owns the timers, so cancellation scheduled by the
    /// budget fires from the same clock that measures the remaining budget.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, _ticks + dueTime.Ticks);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            _ticks += delta.Ticks;
            foreach (var timer in _timers.ToArray())
            {
                timer.FireIfDue(_ticks);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private long _dueAt;
            private bool _disposed;

            public ManualTimer(TimerCallback callback, object? state, long dueAt)
            {
                _callback = callback;
                _state = state;
                _dueAt = dueAt;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _dueAt = long.MaxValue;
                return true;
            }

            public void FireIfDue(long now)
            {
                if (_disposed || now < _dueAt)
                {
                    return;
                }

                _dueAt = long.MaxValue;
                _callback(_state);
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                _disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
