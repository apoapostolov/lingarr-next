using System;
using System.Collections.Generic;
using Lingarr.Core.Configuration;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class NonTextSubtitlePolicyTests
{
    [Fact]
    public void MissingSettingsStayOn()
    {
        var policy = NonTextSubtitlePolicy.From(new Dictionary<string, string>());

        Assert.True(policy.PictureOcr);
        Assert.True(policy.CaptionExtract);
        Assert.True(policy.AnyFormatEnabled);
        Assert.True(policy.Allows("hdmv_pgs_subtitle"));
        Assert.True(policy.Allows("eia_708"));
    }

    [Fact]
    public void FalseTurnsOffTheToolAndItsFormats()
    {
        var policy = NonTextSubtitlePolicy.From(new Dictionary<string, string>
        {
            [SettingKeys.Subtitle.PictureOcrEnabled] = "false",
            [SettingKeys.Subtitle.Eia608Enabled] = "false"
        });

        Assert.False(policy.Allows("pgssub"));
        Assert.False(policy.Allows("xsub"));
        Assert.False(policy.Allows("eia_608"));
        Assert.True(policy.Allows("dvb_teletext"));
        Assert.True(policy.AnyFormatEnabled);
        Assert.True(policy.LastResort);
        Assert.Equal(72, policy.WaitHours);
    }

    [Fact]
    public void LastResortWaitsUntilTheFileIsOldEnough()
    {
        var found = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var policy = NonTextSubtitlePolicy.All;

        Assert.False(policy.ConvertImagesNow(found, found.AddHours(71)));
        Assert.True(policy.ConvertImagesNow(found, found.AddHours(72)));
        Assert.Equal(TimeSpan.FromHours(1), policy.DelayUntilImages(found, found.AddHours(71)));
    }

    [Fact]
    public void ImmediateConvertsAsSoonAsTheFileIsFound()
    {
        var found = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var policy = NonTextSubtitlePolicy.All with { LastResort = false };

        Assert.True(policy.ConvertImagesNow(found, found));
        Assert.False(policy.TextOnly().Allows("hdmv_pgs_subtitle"));
    }
}
