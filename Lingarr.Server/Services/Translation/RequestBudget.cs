namespace Lingarr.Server.Services.Translation;

/// <summary>
/// One deadline for a whole provider call. Attempts, the waits between them, and every chunk of a
/// long line share it, so a retry sequence cannot outlive the request timeout the provider was
/// configured with. <see cref="System.Net.Http.HttpClient.Timeout"/> only bounds one HTTP request.
/// </summary>
public sealed class RequestBudget : IDisposable
{
    private readonly CancellationTokenSource _expiry;
    private readonly TimeProvider _time;
    private readonly TimeSpan _budget;
    private readonly long _started;

    public RequestBudget(TimeSpan budget, CancellationToken callerToken, TimeProvider? time = null)
    {
        _budget = budget < TimeSpan.Zero ? TimeSpan.Zero : budget;
        _time = time ?? TimeProvider.System;
        _started = _time.GetTimestamp();
        _expiry = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        _expiry.CancelAfter(_budget);
    }

    /// <summary>Token that ends the call when the budget runs out or the caller cancels.</summary>
    public CancellationToken Token => _expiry.Token;

    public TimeSpan Remaining
    {
        get
        {
            var left = _budget - _time.GetElapsedTime(_started);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public bool Exhausted => Remaining == TimeSpan.Zero;

    /// <summary>
    /// Waits between attempts, without letting the wait run past the deadline. Throws
    /// <see cref="RequestBudgetExhaustedException"/> when the budget cannot cover the wait, and
    /// lets caller cancellation stay a cancellation.
    /// </summary>
    public async Task DelayAsync(TimeSpan delay, CancellationToken callerToken)
    {
        try
        {
            await Task.Delay(delay, _expiry.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new RequestBudgetExhaustedException();
        }
    }

    public void Dispose() => _expiry.Dispose();
}

/// <summary>The provider call spent its whole deadline before it produced a translation.</summary>
public sealed class RequestBudgetExhaustedException : Exception
{
    public RequestBudgetExhaustedException()
        : base("The request budget for this provider call is exhausted.")
    {
    }
}
