using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Integration.Bazarr;

/// <summary>
/// How often to ask Bazarr again after a miss, and when to stop.
/// A missing value uses the default.
/// </summary>
public sealed record BazarrRetryPolicy
{
    public static readonly string[] Keys =
    [
        SettingKeys.Integration.BazarrRetryHours,
        SettingKeys.Integration.BazarrRetryTimeoutHours,
        SettingKeys.Integration.BazarrReplaceOcr
    ];

    public int RetryHours { get; init; } = 12;
    public int TimeoutHours { get; init; } = 168;
    public bool ReplaceOcr { get; init; } = true;

    public static BazarrRetryPolicy From(IReadOnlyDictionary<string, string>? settings)
    {
        settings ??= new Dictionary<string, string>();
        return new BazarrRetryPolicy
        {
            RetryHours = Parse(settings, SettingKeys.Integration.BazarrRetryHours, 12, 0),
            TimeoutHours = Parse(settings, SettingKeys.Integration.BazarrRetryTimeoutHours, 168, 1),
            ReplaceOcr = !settings.TryGetValue(SettingKeys.Integration.BazarrReplaceOcr, out var replace)
                || !string.Equals(replace, "false", StringComparison.OrdinalIgnoreCase)
        };
    }

    public bool AttemptAllowed(DateTime? foundAt, DateTime utcNow)
    {
        if (foundAt == null)
        {
            return false;
        }

        return utcNow <= AsUtc(foundAt.Value).AddHours(TimeoutHours);
    }

    public TimeSpan? NextDelay(DateTime? foundAt, DateTime utcNow)
    {
        if (RetryHours <= 0 || foundAt == null)
        {
            return null;
        }

        var next = utcNow.AddHours(RetryHours);
        if (next > AsUtc(foundAt.Value).AddHours(TimeoutHours))
        {
            return null;
        }

        return TimeSpan.FromHours(RetryHours);
    }

    private static int Parse(IReadOnlyDictionary<string, string> settings, string key, int fallback, int minimum)
    {
        if (!settings.TryGetValue(key, out var value) || !int.TryParse(value, out var hours))
        {
            return fallback;
        }

        return Math.Clamp(hours, minimum, 24 * 365);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
