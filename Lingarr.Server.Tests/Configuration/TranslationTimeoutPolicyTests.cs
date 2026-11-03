using System.Collections.Generic;
using Lingarr.Core.Configuration;
using Xunit;

namespace Lingarr.Server.Tests.Configuration;

public class TranslationTimeoutPolicyTests
{
    [Fact]
    public void RequestTimeoutForProvider_NormalizesProviderId()
    {
        var key = SettingKeys.Translation.RequestTimeoutForProvider("OpenCode-Go");

        Assert.Equal("opencode_go_request_timeout", key);
    }

    [Fact]
    public void ResolveMinutes_UsesProviderSpecificValue()
    {
        var settings = new Dictionary<string, string>
        {
            [SettingKeys.Translation.RequestTimeout] = "5",
            [SettingKeys.Translation.RequestTimeoutForProvider("microsoft")] = "15"
        };

        var timeout = TranslationTimeoutPolicy.ResolveMinutes(settings, "microsoft");

        Assert.Equal(15, timeout);
    }

    [Fact]
    public void ResolveMinutes_FallsBackToLegacyGlobalValue()
    {
        var settings = new Dictionary<string, string>
        {
            [SettingKeys.Translation.RequestTimeout] = "7"
        };

        var timeout = TranslationTimeoutPolicy.ResolveMinutes(settings, "openai");

        Assert.Equal(7, timeout);
    }

    [Fact]
    public void ResolveMinutes_UsesLongerMicrosoftDefaultWhenSettingsAreInvalid()
    {
        var settings = new Dictionary<string, string>
        {
            [SettingKeys.Translation.RequestTimeout] = "0",
            [SettingKeys.Translation.RequestTimeoutForProvider("microsoft")] = "invalid"
        };

        var timeout = TranslationTimeoutPolicy.ResolveMinutes(settings, "microsoft");

        Assert.Equal(TranslationTimeoutPolicy.MicrosoftDefaultTimeoutMinutes, timeout);
    }

    [Fact]
    public void AttemptTimeoutForProvider_NormalizesProviderId()
    {
        var key = SettingKeys.Translation.RequestAttemptTimeoutForProvider("OpenCode-Go");

        Assert.Equal("opencode_go_request_attempt_timeout", key);
    }

    [Fact]
    public void ResolveAttemptSeconds_DefaultsToTwoMinutes()
    {
        var attempt = TranslationTimeoutPolicy.ResolveAttemptSeconds(
            new Dictionary<string, string>(), "microsoft");

        Assert.Equal(TranslationTimeoutPolicy.DefaultAttemptTimeoutSeconds, attempt);
    }

    [Fact]
    public void ResolveAttemptSeconds_UsesProviderSpecificValue()
    {
        var settings = new Dictionary<string, string>
        {
            [SettingKeys.Translation.RequestAttemptTimeout] = "30",
            [SettingKeys.Translation.RequestAttemptTimeoutForProvider("microsoft")] = "90"
        };

        var attempt = TranslationTimeoutPolicy.ResolveAttemptSeconds(settings, "microsoft");

        Assert.Equal(90, attempt);
    }

    [Fact]
    public void ResolveAttemptSeconds_FallsBackToGlobalValue()
    {
        var settings = new Dictionary<string, string>
        {
            [SettingKeys.Translation.RequestAttemptTimeout] = "45"
        };

        var attempt = TranslationTimeoutPolicy.ResolveAttemptSeconds(settings, "google");

        Assert.Equal(45, attempt);
    }
}
