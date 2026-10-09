using System;
using System.IO;
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

public class TailPluginTests
{
    [Fact]
    public void HearingFilter_ClearsACueLineAndKeepsDialogue()
    {
        var text = """
            1
            00:00:01,000 --> 00:00:02,000
            [music]
            How are you?
            """;
        var filtered = HearingFilter.Apply(text.Replace("\r\n", "\n"));
        Assert.DoesNotContain("[music]", filtered, StringComparison.Ordinal);
        Assert.Contains("How are you?", filtered, StringComparison.Ordinal);
        Assert.Contains("-->", filtered, StringComparison.Ordinal);
    }

    [Fact]
    public void CastGlossary_NamesFrenchAndJapaneseCharacters()
    {
        Assert.Equal(["Amélie", "Nino"], CastGlossary.For("fr"));
        Assert.Equal(["Chihiro", "Haku"], CastGlossary.For("ja"));
        Assert.Empty(CastGlossary.For("de"));
    }

    [Fact]
    public async Task Runner_AppliesAFilter_PassesGlossary_AndStillRunsAFileTool()
    {
        var directory = Directory.CreateTempSubdirectory("lingarr-tail");
        var path = Path.Combine(directory.FullName, "movie.fr.srt");
        await File.WriteAllTextAsync(path, "[music]\nBonjour");
        var seenGlossary = "";
        var settings = Settings(true, "hearing-filter", "cast-glossary", "trailing-line", "recorder");
        var runner = new SubtitlePostProcessRunner(
            [new GlossaryRecorder(value => seenGlossary = value)],
            [new HearingFilter()],
            [new CastGlossary()],
            [new TrailingLine()],
            settings.Object,
            NullLogger<SubtitlePostProcessRunner>.Instance);

        await runner.RunAsync(new SubtitlePostProcessJob
        {
            SourcePath = path,
            TargetPath = path,
            SourceLanguage = "en",
            TargetLanguage = "fr"
        }, CancellationToken.None);

        var written = await File.ReadAllTextAsync(path);
        Assert.Equal("Amélie\nNino", seenGlossary);
        Assert.DoesNotContain("[music]", written, StringComparison.Ordinal);
        Assert.EndsWith("\n", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_RunsAFileToolWhenTheTextStaysTheSame()
    {
        var directory = Directory.CreateTempSubdirectory("lingarr-tail-same");
        var path = Path.Combine(directory.FullName, "movie.es.srt");
        await File.WriteAllTextAsync(path, "Hola");
        var ran = 0;
        var settings = Settings(true, "touch");
        var runner = new SubtitlePostProcessRunner(
            [],
            [],
            [],
            [new CountingFileTool("touch", () => ran++)],
            settings.Object,
            NullLogger<SubtitlePostProcessRunner>.Instance);

        await runner.RunAsync(new SubtitlePostProcessJob
        {
            SourcePath = path,
            TargetPath = path,
            SourceLanguage = "en",
            TargetLanguage = "es"
        }, CancellationToken.None);

        Assert.Equal(1, ran);
        Assert.Equal("Hola", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WidgetAndHealth_AnswerWhenEnabled()
    {
        ShelfNotes.LastFile = null;
        ShelfNotes.LastWarning = null;
        var settings = Settings(true, "shelf-widget", "shelf-health", "shelf-watch");
        var signals = Signals(settings);
        await signals.OnDiscoveredAsync("/media", "Amélie.mkv", CancellationToken.None);
        var widgets = await signals.WidgetsAsync(CancellationToken.None);
        var health = await signals.ProbeAsync("shelf-health", CancellationToken.None);

        Assert.Equal("Shelf", Assert.Single(widgets).Title);
        Assert.Equal("Shelf last saw Amélie.mkv.", widgets[0].Text);
        Assert.Equal("Shelf is ready.", health);
    }

    [Fact]
    public async Task Health_WhenDisabled_ReturnsAnEmptySentence()
    {
        var settings = Settings(false, "shelf-health");
        var health = await Signals(settings).ProbeAsync("shelf-health", CancellationToken.None);
        Assert.Equal("", health);
    }

    private static PluginSignals Signals(Mock<ISettingService> settings) => new(
        [],
        [],
        [],
        [new ShelfWatch()],
        [new ShelfHealth()],
        [new ShelfWidget()],
        settings.Object,
        NullLogger<PluginSignals>.Instance);

    private static Mock<ISettingService> Settings(bool enabled, params string[] providers)
    {
        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(It.IsAny<string>())).ReturnsAsync("false");
        foreach (var provider in providers)
        {
            settings.Setup(service => service.GetSetting(PluginCatalog.EnabledKey(provider)))
                .ReturnsAsync(enabled ? "true" : "false");
            settings.Setup(service => service.GetSetting(PluginCatalog.OrderKey(provider)))
                .ReturnsAsync("100");
        }

        return settings;
    }

    private sealed class GlossaryRecorder : ISubtitlePostProcessor
    {
        private readonly Action<string?> _seen;

        public GlossaryRecorder(Action<string?> seen)
        {
            _seen = seen;
        }

        public string Provider => "recorder";

        public SubtitlePostProcessKind Kind => SubtitlePostProcessKind.Style;

        public Task<SubtitlePostProcessResult> ProcessAsync(
            SubtitlePostProcessInput input,
            CancellationToken cancellationToken)
        {
            _seen(input.Glossary);
            return Task.FromResult(new SubtitlePostProcessResult());
        }
    }

    private sealed class CountingFileTool : IFileTool
    {
        private readonly Action _count;

        public CountingFileTool(string provider, Action count)
        {
            Provider = provider;
            _count = count;
        }

        public string Provider { get; }

        public Task RunAsync(string targetPath, CancellationToken cancellationToken)
        {
            _count();
            return Task.CompletedTask;
        }
    }
}
