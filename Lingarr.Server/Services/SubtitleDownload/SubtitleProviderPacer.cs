namespace Lingarr.Server.Services.SubtitleDownload;

/// <summary>
/// What a paced call costs the provider. A download spends the request budget and the download
/// budget, because SubDL counts both and SubSource rate-limits every endpoint.
/// </summary>
public enum PacerCallKind
{
    Request,
    Download
}

/// <summary>
/// The answer to "may this provider be called now?". <see cref="Reason"/> is a short phrase meant
/// for one log line, and <see cref="RetryAfter"/> is how long the caller would have to wait.
/// </summary>
public readonly record struct PacerDecision(bool Allowed, string? Reason, TimeSpan RetryAfter)
{
    public static PacerDecision Allow() => new(true, null, TimeSpan.Zero);

    public static PacerDecision Stop(string reason, TimeSpan retryAfter) => new(false, reason, retryAfter);
}

/// <summary>
/// Per-provider pacing for the two key-based subtitle sources. SubSource publishes 60 requests a
/// minute and returns its live remainder in <c>X-RateLimit-Remaining-Hour</c>/<c>-Day</c>; SubDL
/// publishes a free key's daily allowance and sends no rate headers at all. Neither provider was
/// paced before this type existed, so a library sweep could spend a whole day's budget in a
/// minute.
/// </summary>
/// <remarks>
/// The state is process-local and in memory on purpose: a restart can spend a little of a daily
/// budget twice, which is cheaper than a table and a migration for counters that reset at
/// midnight. The clock comes from <see cref="TimeProvider"/> so a test can expire a cooldown
/// without waiting for it.
/// </remarks>
public sealed class SubtitleProviderPacer
{
    public const string Subsource = "subsource";
    public const string Subdl = "subdl";

    /// <summary>A provider that reports an hour almost spent gets this long to refill.</summary>
    internal static readonly TimeSpan HourPause = TimeSpan.FromMinutes(10);

    /// <summary>Cooldown after an HTTP 429 that carried no usable <c>Retry-After</c>.</summary>
    internal static readonly TimeSpan DefaultRateLimitCooldown = TimeSpan.FromSeconds(900);

    internal static readonly TimeSpan BaseServerErrorCooldown = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan MaxServerErrorCooldown = TimeSpan.FromSeconds(1800);

    private const int LowHourRemaining = 30;
    private const int MaxJitterMilliseconds = 250;

    private readonly TimeProvider _time;
    private readonly Random _random;
    private readonly object _gate = new();
    private readonly Dictionary<string, ProviderState> _states;

    public SubtitleProviderPacer(TimeProvider? time = null, Random? random = null)
    {
        _time = time ?? TimeProvider.System;
        _random = random ?? Random.Shared;
        _states = new Dictionary<string, ProviderState>(StringComparer.OrdinalIgnoreCase)
        {
            [Subsource] = new ProviderState(TimeSpan.FromMilliseconds(2500), dailyRequests: 1200, dailyDownloads: 200),
            [Subdl] = new ProviderState(TimeSpan.FromMilliseconds(3500), dailyRequests: 300, dailyDownloads: 40)
        };
    }

    /// <summary>
    /// Whether the provider may be called right now, and if not, which budget or cooldown stopped
    /// it. Pure query: it never counts a call and never waits.
    /// </summary>
    public PacerDecision Check(string providerId, PacerCallKind call = PacerCallKind.Request)
    {
        lock (_gate)
        {
            var state = State(providerId);
            if (state == null)
            {
                return PacerDecision.Allow();
            }

            var now = _time.GetUtcNow();
            RollDay(state, now);

            if (state.FinishedDay == Today(now))
            {
                return PacerDecision.Stop(
                    $"{providerId} is finished for today (provider reports no daily requests left)",
                    UntilTomorrow(now));
            }

            if (state.BlockedUntil is { } blockedUntil && blockedUntil > now)
            {
                return PacerDecision.Stop(
                    state.BlockedReason ?? $"{providerId} is cooling down",
                    blockedUntil - now);
            }

            if (state.Requests >= state.DailyRequests)
            {
                return PacerDecision.Stop(
                    $"{providerId} daily request budget is spent ({state.Requests}/{state.DailyRequests})",
                    UntilTomorrow(now));
            }

            if (call == PacerCallKind.Download && state.Downloads >= state.DailyDownloads)
            {
                return PacerDecision.Stop(
                    $"{providerId} daily download budget is spent ({state.Downloads}/{state.DailyDownloads})",
                    UntilTomorrow(now));
            }

            return PacerDecision.Allow();
        }
    }

    /// <summary>
    /// How long the caller still has to wait before the minimum interval between two calls has
    /// passed. Zero on the first call, and zero once the interval is behind us.
    /// </summary>
    public TimeSpan Wait(string providerId)
    {
        lock (_gate)
        {
            var state = State(providerId);
            if (state?.LastRequestAt is not { } last)
            {
                return TimeSpan.Zero;
            }

            var wait = state.LastInterval - (_time.GetUtcNow() - last);
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Checks, waits out the remaining minimum interval on the injected clock, and counts the call.
    /// The gateway calls this before every outbound request.
    /// </summary>
    public async Task<PacerDecision> AcquireAsync(
        string providerId,
        PacerCallKind call,
        CancellationToken cancellationToken)
    {
        var decision = Check(providerId, call);
        if (!decision.Allowed)
        {
            return decision;
        }

        var wait = Wait(providerId);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (State(providerId) is { } state)
            {
                Record(state, call);
            }
        }

        return PacerDecision.Allow();
    }

    /// <summary>
    /// Records what the provider answered, so the next check knows about a 429, a 5xx, a nearly
    /// spent hour or a finished day. Call it after every outbound request, before the status is
    /// judged.
    /// </summary>
    public void Observe(
        string providerId,
        int statusCode,
        TimeSpan? retryAfter = null,
        int? remainingHour = null,
        int? remainingDay = null)
    {
        lock (_gate)
        {
            var state = State(providerId);
            if (state == null)
            {
                return;
            }

            var now = _time.GetUtcNow();
            RollDay(state, now);

            switch (statusCode)
            {
                case 429:
                    state.ConsecutiveFailures = 0;
                    Block(state, now,
                        retryAfter is { Ticks: > 0 } ? retryAfter.Value : DefaultRateLimitCooldown,
                        $"{providerId} rate limited (HTTP 429)");
                    break;
                case >= 500:
                    state.ConsecutiveFailures++;
                    Block(state, now, ServerErrorCooldown(state.ConsecutiveFailures),
                        $"{providerId} cooling down after {state.ConsecutiveFailures} consecutive server error(s)");
                    break;
                case >= 200 and < 300:
                    state.ConsecutiveFailures = 0;
                    break;
            }

            if (remainingDay == 0)
            {
                state.FinishedDay = Today(now);
                state.BlockedUntil = null;
                state.BlockedReason = null;
            }
            else if (remainingHour is <= LowHourRemaining)
            {
                Block(state, now, HourPause,
                    $"{providerId} hour budget nearly spent ({remainingHour} left)");
            }
        }
    }

    /// <summary>Requests counted against the provider's daily request budget.</summary>
    public int RequestsUsed(string providerId)
    {
        lock (_gate)
        {
            return State(providerId) is { } state ? state.Requests : 0;
        }
    }

    /// <summary>Downloads counted against the provider's daily download budget.</summary>
    public int DownloadsUsed(string providerId)
    {
        lock (_gate)
        {
            return State(providerId) is { } state ? state.Downloads : 0;
        }
    }

    /// <summary>The clock the pacer waits on, so callers delay against the same one.</summary>
    public TimeProvider Clock => _time;

    /// <summary>
    /// Doubles per consecutive failure, from 60 s up to the 30 minute cap: 60, 120, 240, ... 1920
    /// is capped at 1800, so a provider that stays broken is retried twice an hour rather than
    /// once every second.
    /// </summary>
    internal static TimeSpan ServerErrorCooldown(int consecutiveFailures)
    {
        var exponent = Math.Min(Math.Max(consecutiveFailures - 1, 0), 5);
        var seconds = BaseServerErrorCooldown.TotalSeconds * Math.Pow(2, exponent);
        return seconds >= MaxServerErrorCooldown.TotalSeconds
            ? MaxServerErrorCooldown
            : TimeSpan.FromSeconds(seconds);
    }

    private static void Block(ProviderState state, DateTimeOffset now, TimeSpan cooldown, string reason)
    {
        var until = now + cooldown;
        if (state.BlockedUntil is null || until > state.BlockedUntil)
        {
            state.BlockedUntil = until;
            state.BlockedReason = reason;
        }
    }

    private void Record(ProviderState state, PacerCallKind call)
    {
        var now = _time.GetUtcNow();
        RollDay(state, now);

        state.Requests++;
        if (call == PacerCallKind.Download)
        {
            state.Downloads++;
        }

        state.LastRequestAt = now;
        state.LastInterval = state.Interval + Jitter();
    }

    /// <summary>Forward-only jitter: the interval is never shorter than the published minimum.</summary>
    private TimeSpan Jitter() =>
        TimeSpan.FromMilliseconds(_random.Next(0, MaxJitterMilliseconds + 1));

    private static void RollDay(ProviderState state, DateTimeOffset now)
    {
        var today = Today(now);
        if (state.Day == today)
        {
            return;
        }

        state.Day = today;
        state.Requests = 0;
        state.Downloads = 0;
        state.FinishedDay = null;
        state.LastRequestAt = null;
        state.LastInterval = TimeSpan.Zero;
    }

    private static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);

    private static TimeSpan UntilTomorrow(DateTimeOffset now)
    {
        var midnight = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var wait = midnight - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.FromMinutes(1);
    }

    private ProviderState? State(string providerId) =>
        string.IsNullOrWhiteSpace(providerId) ? null : _states.GetValueOrDefault(providerId.Trim());

    private sealed class ProviderState(TimeSpan interval, int dailyRequests, int dailyDownloads)
    {
        public TimeSpan Interval { get; } = interval;
        public int DailyRequests { get; } = dailyRequests;
        public int DailyDownloads { get; } = dailyDownloads;

        public DateOnly Day { get; set; }
        public int Requests { get; set; }
        public int Downloads { get; set; }
        public DateTimeOffset? LastRequestAt { get; set; }
        public TimeSpan LastInterval { get; set; }
        public DateTimeOffset? BlockedUntil { get; set; }
        public string? BlockedReason { get; set; }
        public DateOnly? FinishedDay { get; set; }
        public int ConsecutiveFailures { get; set; }
    }
}
