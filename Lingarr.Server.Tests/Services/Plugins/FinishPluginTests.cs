using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Plugins;
using Lingarr.Plugin.StyleSample;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plugins;

public class FinishPluginTests
{
    [Fact]
    public async Task Badge_LabelsAnOcrFileWhenEnabled()
    {
        var labels = await Shelf(true, "ocr-badge", badges: [new OcrBadge()])
            .LabelsAsync("movie.fr.ocr.srt");
        Assert.Equal("ocr", Assert.Single(labels));
    }

    [Fact]
    public async Task PathMap_RewritesTheServerPrefix()
    {
        var mapped = await Shelf(true, "film-map", mappers: [new FilmMap()])
            .MapAsync("/server/films/Amélie.mkv", "Movie");
        Assert.Equal("/media/films/Amélie.mkv", mapped);
    }

    [Fact]
    public void Retry_StopsWhenTheAdviceSaysSo()
    {
        var delay = PluginShelf.ChooseDelay(
            TimeSpan.FromHours(12),
            new RetryAdvice { Retry = false },
            DateTime.UtcNow,
            DateTime.UtcNow,
            168);
        Assert.Null(delay);
    }

    [Fact]
    public async Task Prompt_NamesFrenchCharactersAndSkipsOtherLanguages()
    {
        var french = await Shelf(true, "name-prompt", prompts: [new NamePrompt()])
            .PromptBlockAsync("en", "fr");
        var german = await Shelf(true, "name-prompt", prompts: [new NamePrompt()])
            .PromptBlockAsync("en", "de");
        Assert.Equal("Names: Keep Amélie and Nino spelled as written.", french);
        Assert.Null(german);
    }

    [Fact]
    public async Task Export_ReceivesCountsWithoutAPath()
    {
        CountExport.Last = null;
        var snapshot = new StatisticsSnapshot { Movies = 2, Episodes = 3, SubtitleFiles = 4 };
        await Shelf(true, "count-export", exporters: [new CountExport()]).ExportAsync(snapshot);
        Assert.Equal(2, CountExport.Last?.Movies);
        Assert.Equal(4, CountExport.Last?.SubtitleFiles);
    }

    [Fact]
    public void Merge_KeepsTheOcrFileWhenAsked()
    {
        Assert.False(PluginShelf.ReplaceOcrSource(true, SubtitleMergeChoice.Keep));
        Assert.True(PluginShelf.ReplaceOcrSource(true, SubtitleMergeChoice.Default));
        Assert.False(PluginShelf.ReplaceOcrSource(false, SubtitleMergeChoice.Replace));
    }

    [Fact]
    public async Task Caption_DropsAnSdhSource()
    {
        var kept = await Shelf(true, "sdh-policy", captions: [new SdhPolicy()]).FilterCaptionsAsync(
        [
            new Subtitles { Path = "a.en.sdh.srt", Caption = "sdh", Language = "en" },
            new Subtitles { Path = "a.en.srt", Caption = "", Language = "en" }
        ]);
        Assert.Equal("a.en.srt", Assert.Single(kept).Path);
    }

    [Fact]
    public async Task Codec_ReadsSrtTextFromASubFile()
    {
        var directory = Directory.CreateTempSubdirectory("lingarr-sub");
        var path = Path.Combine(directory.FullName, "clip.en.sub");
        await File.WriteAllTextAsync(path, "1\n00:00:01,000 --> 00:00:02,000\nBonjour\n");
        var text = await new SubCodec().ReadAsync(path, CancellationToken.None);
        Assert.Contains("Bonjour", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_UsesTheHitWhenTheMappedFolderIsMissing()
    {
        var directory = Directory.CreateTempSubdirectory("lingarr-library");
        var hit = new LibraryHit { Directory = directory.FullName, FileName = "Amélie.mkv" };
        var resolved = PluginShelf.Prefer("/missing/Amélie.mkv", hit);
        Assert.Equal(Path.Combine(directory.FullName, "Amélie.mkv"), resolved);
    }

    private static PluginShelf Shelf(
        bool enabled,
        string provider,
        IEnumerable<IPathMapper>? mappers = null,
        IEnumerable<IListBadge>? badges = null,
        IEnumerable<IPromptContributor>? prompts = null,
        IEnumerable<IStatisticsExporter>? exporters = null,
        IEnumerable<ICaptionPolicy>? captions = null)
    {
        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(It.IsAny<string>())).ReturnsAsync("false");
        settings.Setup(service => service.GetSetting(PluginCatalog.EnabledKey(provider)))
            .ReturnsAsync(enabled ? "true" : "false");
        return new PluginShelf(
            mappers ?? [],
            [],
            badges ?? [],
            prompts ?? [],
            exporters ?? [],
            [],
            [],
            captions ?? [],
            [],
            settings.Object,
            NullLogger<PluginShelf>.Instance);
    }
}
