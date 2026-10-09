using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class ProviderCancelRetryTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("0", 0)]
    [InlineData("5", 5)]
    public void Count_ZeroMeansDoNotRetry(string? raw, int count)
    {
        Assert.Equal(count, ProviderCancelRetry.Count(raw));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("0", 1)]
    [InlineData("2", 2)]
    public void Hours_BelowOneBecomesOneHour(string? raw, int hours)
    {
        Assert.Equal(hours, ProviderCancelRetry.Hours(raw));
    }

    [Fact]
    public void Plan_SchedulesUntilTheLimit()
    {
        var first = ProviderCancelRetry.Plan(0, 5);
        var last = ProviderCancelRetry.Plan(4, 5);
        var done = ProviderCancelRetry.Plan(5, 5);
        var off = ProviderCancelRetry.Plan(0, 0);

        Assert.True(first.Retry);
        Assert.Equal(1, first.Attempt);
        Assert.True(last.Retry);
        Assert.Equal(5, last.Attempt);
        Assert.False(done.Retry);
        Assert.Equal(5, done.Attempt);
        Assert.False(off.Retry);
    }
}
