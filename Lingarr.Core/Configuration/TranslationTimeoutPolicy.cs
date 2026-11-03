namespace Lingarr.Core.Configuration;

/// <summary>
/// Resolves translation request timeouts without forcing every provider to share one value.
/// Provider-specific settings fall back to the legacy global setting for backward compatibility.
/// </summary>
public static class TranslationTimeoutPolicy
{
    public const int DefaultTimeoutMinutes = 5;
    public const int MicrosoftDefaultTimeoutMinutes = 15;
    public const int DefaultAttemptTimeoutSeconds = 120;

    public static int ResolveMinutes(
        IReadOnlyDictionary<string, string> settings,
        string provider)
    {
        var providerKey = SettingKeys.Translation.RequestTimeoutForProvider(provider);
        if (TryGetPositiveMinutes(settings, providerKey, out var providerMinutes))
        {
            return providerMinutes;
        }

        if (TryGetPositiveMinutes(settings, SettingKeys.Translation.RequestTimeout, out var globalMinutes))
        {
            return globalMinutes;
        }

        return provider.Equals("microsoft", StringComparison.OrdinalIgnoreCase)
            ? MicrosoftDefaultTimeoutMinutes
            : DefaultTimeoutMinutes;
    }

    /// <summary>
    /// Seconds one provider call may run before it is abandoned and retried. The provider keeps its
    /// full request budget: a call that never answers is dropped quickly and tried again, instead of
    /// occupying the job for the whole request timeout.
    /// </summary>
    public static int ResolveAttemptSeconds(
        IReadOnlyDictionary<string, string> settings,
        string provider)
    {
        var providerKey = SettingKeys.Translation.RequestAttemptTimeoutForProvider(provider);
        if (TryGetPositive(settings, providerKey, out var providerSeconds))
        {
            return providerSeconds;
        }

        return TryGetPositive(settings, SettingKeys.Translation.RequestAttemptTimeout, out var globalSeconds)
            ? globalSeconds
            : DefaultAttemptTimeoutSeconds;
    }

    private static bool TryGetPositiveMinutes(
        IReadOnlyDictionary<string, string> settings,
        string key,
        out int minutes)
    {
        minutes = 0;
        return settings.TryGetValue(key, out var value)
               && int.TryParse(value, out minutes)
               && minutes > 0;
    }

    private static bool TryGetPositive(
        IReadOnlyDictionary<string, string> settings,
        string key,
        out int value)
    {
        value = 0;
        return settings.TryGetValue(key, out var raw)
               && int.TryParse(raw, out value)
               && value > 0;
    }
}
