namespace Lingarr.Server.Services.Translation;

public readonly record struct ProviderCancelPlan(bool Retry, int Attempt, int Max);

public static class ProviderCancelRetry
{
    public static int Count(string? raw) =>
        int.TryParse(raw, out var value) && value > 0 ? value : 0;

    public static int Hours(string? raw) =>
        int.TryParse(raw, out var value) && value >= 1 ? value : 1;

    public static ProviderCancelPlan Plan(int attemptsSoFar, int max)
    {
        if (max <= 0)
        {
            return new ProviderCancelPlan(false, 0, 0);
        }

        var attempt = attemptsSoFar + 1;
        if (attempt <= max)
        {
            return new ProviderCancelPlan(true, attempt, max);
        }

        return new ProviderCancelPlan(false, max, max);
    }
}
