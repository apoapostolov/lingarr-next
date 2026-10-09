using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Plugins;
using Lingarr.Plugin.StyleSample;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plugins;

public class LibraryPluginTests
{
    [Fact]
    public void FixtureInbox_QueuesAnAddedMovieAndIgnoresPlayback()
    {
        var inbox = new FixtureInbox();
        var added = inbox.Read("""{"event":"added","title":"Amélie","year":2001,"tmdb":"194"}""");
        Assert.False(added.Ignore);
        Assert.Equal("Amélie", added.Title);
        Assert.Equal(2001, added.Year);
        Assert.Equal("194", added.Tmdb);

        var playback = inbox.Read("""{"event":"playback","title":"Amélie"}""");
        Assert.True(playback.Ignore);
    }

    [Fact]
    public async Task Notifier_RunsOnlyWhenEnabled()
    {
        var calls = 0;
        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(PluginCatalog.EnabledKey("shelf-notice")))
            .ReturnsAsync("true");
        settings.Setup(service => service.GetSetting(PluginCatalog.EnabledKey("quiet")))
            .ReturnsAsync("false");
        var signals = new PluginSignals(
            [
                new CountingNotifier("shelf-notice", () => calls++),
                new CountingNotifier("quiet", () => calls += 10)
            ],
            [],
            [],
            [],
            [],
            [],
            settings.Object,
            NullLogger<PluginSignals>.Instance);

        await signals.NotifyAsync(new PluginNotice { Succeeded = true, Title = "Amélie.fr.srt" }, CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MediaAction_ReturnsASentenceForOneTitle()
    {
        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(PluginCatalog.EnabledKey("mark-title")))
            .ReturnsAsync("true");
        var signals = new PluginSignals(
            [],
            [],
            [new MarkTitle()],
            [],
            [],
            [],
            settings.Object,
            NullLogger<PluginSignals>.Instance);

        var message = await signals.RunMediaActionAsync(
            "mark-title",
            "mark",
            new MediaItemRef { Title = "Spirited Away" },
            CancellationToken.None);

        Assert.Equal("Marked Spirited Away.", message);
    }

    private sealed class CountingNotifier : IPluginNotifier
    {
        private readonly System.Action _count;

        public CountingNotifier(string provider, System.Action count)
        {
            Provider = provider;
            _count = count;
        }

        public string Provider { get; }

        public Task NotifyAsync(PluginNotice notice, CancellationToken cancellationToken)
        {
            _count();
            return Task.CompletedTask;
        }
    }
}
