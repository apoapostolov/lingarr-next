using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleHtmlTests
{
    [Fact]
    public void Strip_RemovesFontTagsAndKeepsTheWords()
    {
        Assert.Equal(
            "good.",
            SubtitleHtml.Strip("<font face=\"sans-serif\" size=\"71\">good.</font>"));
        Assert.Equal(
            "добре.",
            SubtitleHtml.Strip("<шрифт face=\"sans-serif\" size=\"71\">добре.</font>"));
        Assert.Equal(
            "О, да. Мисля да си взема такъв.",
            SubtitleHtml.Strip("<font face=\"sans-serif\" size=\"71\"> О, да. Мисля да си взема такъв</font>."));
    }

    [Fact]
    public void Strip_LeavesALineWithoutTagsAlone()
    {
        Assert.Equal("Yeah. It seems...", SubtitleHtml.Strip("Yeah. It seems..."));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Enabled_IsOnUnlessSetToFalse(string? value, bool enabled)
    {
        Assert.Equal(enabled, SubtitleHtml.Enabled(value));
    }
}
