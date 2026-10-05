using System;
using System.Collections.Generic;
using Lingarr.Core.Configuration;
using Lingarr.Server.Services.Integration.Bazarr;
using Xunit;

namespace Lingarr.Server.Tests.Services.Integration;

public class BazarrRetryPolicyTests
{
    [Fact]
    public void MissingSettingsUseTwelveAndOneHundredSixtyEight()
    {
        var policy = BazarrRetryPolicy.From(new Dictionary<string, string>());

        Assert.Equal(12, policy.RetryHours);
        Assert.Equal(168, policy.TimeoutHours);
        Assert.True(policy.ReplaceOcr);
    }

    [Fact]
    public void RetriesEveryTwelveHoursUntilTheWindowCloses()
    {
        var found = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var policy = BazarrRetryPolicy.From(new Dictionary<string, string>());

        Assert.Equal(TimeSpan.FromHours(12), policy.NextDelay(found, found));
        Assert.Equal(TimeSpan.FromHours(12), policy.NextDelay(found, found.AddHours(156)));
        Assert.Null(policy.NextDelay(found, found.AddHours(160)));
        Assert.True(policy.AttemptAllowed(found, found.AddHours(168)));
        Assert.False(policy.AttemptAllowed(found, found.AddHours(169)));
    }

    [Fact]
    public void ZeroRetryHoursSearchesOnlyOnce()
    {
        var found = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var policy = BazarrRetryPolicy.From(new Dictionary<string, string>
        {
            [SettingKeys.Integration.BazarrRetryHours] = "0"
        });

        Assert.Null(policy.NextDelay(found, found));
    }
}
