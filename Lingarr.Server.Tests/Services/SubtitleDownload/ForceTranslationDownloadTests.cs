using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Models;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.SubtitleDownload;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.SubtitleDownload;

public class ForceTranslationDownloadTests
{
    [Fact]
    public async Task Download_OnlySource_QueuesTranslation()
    {
        var fixture = await CreateFixture();
        fixture.Downloads
            .Setup(service => service.DownloadLanguagesAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.Is<IReadOnlyCollection<string>>(languages => languages.Contains("en")),
                It.IsAny<IReadOnlyList<Subtitles>>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["en"]);
        fixture.Downloads
            .Setup(service => service.DownloadLanguagesAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.Is<IReadOnlyCollection<string>>(languages => languages.Contains("ro")),
                It.IsAny<IReadOnlyList<Subtitles>>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        fixture.Subtitles
            .SetupSequence(service => service.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([])
            .ReturnsAsync(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.en.srt",
                    FileName = "test",
                    Language = "en",
                    Format = ".srt"
                }
            ]);

        var result = await fixture.Service.DownloadAsync(1, MediaType.Movie, CancellationToken.None);

        Assert.Equal(["en"], result.Downloaded);
        Assert.Equal([7], result.RequestIds);
        Assert.Equal("Lingarr placed a source subtitle and queued translation.", result.Message);
        fixture.Requests.Verify(
            service => service.CreateRequest(It.Is<TranslateAbleSubtitle>(request =>
                request.SourceLanguage == "en" && request.TargetLanguage == "ro")),
            Times.Once);
    }

    [Fact]
    public async Task Download_SourceAndTarget_DoesNotQueueTranslation()
    {
        var fixture = await CreateFixture();
        fixture.Downloads
            .Setup(service => service.DownloadLanguagesAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.Is<IReadOnlyCollection<string>>(languages => languages.Contains("en")),
                It.IsAny<IReadOnlyList<Subtitles>>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["en"]);
        fixture.Downloads
            .Setup(service => service.DownloadLanguagesAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.Is<IReadOnlyCollection<string>>(languages => languages.Contains("ro")),
                It.IsAny<IReadOnlyList<Subtitles>>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["ro"]);
        fixture.Subtitles
            .SetupSequence(service => service.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([])
            .ReturnsAsync(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.en.srt",
                    FileName = "test",
                    Language = "en",
                    Format = ".srt"
                }
            ])
            .ReturnsAsync(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.en.srt",
                    FileName = "test",
                    Language = "en",
                    Format = ".srt"
                },
                new Subtitles
                {
                    Path = "/movies/test/test.ro.srt",
                    FileName = "test",
                    Language = "ro",
                    Format = ".srt"
                }
            ]);

        var result = await fixture.Service.DownloadAsync(1, MediaType.Movie, CancellationToken.None);

        Assert.Contains("en", result.Downloaded);
        Assert.Contains("ro", result.Downloaded);
        Assert.Empty(result.RequestIds);
        Assert.Equal("Lingarr placed downloaded subtitles.", result.Message);
        fixture.Requests.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>()),
            Times.Never);
    }

    [Fact]
    public async Task Download_Off_SkipsProviders()
    {
        var fixture = await CreateFixture(enabled: false);

        var result = await fixture.Service.DownloadAsync(1, MediaType.Movie, CancellationToken.None);

        Assert.Equal("Lingarr subtitle downloaders are off.", result.Message);
        fixture.Downloads.Verify(
            service => service.DownloadLanguagesAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<IReadOnlyList<Subtitles>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Force_WithSource_QueuesWithoutDownloaders()
    {
        var fixture = await CreateFixture();
        fixture.Subtitles
            .Setup(service => service.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.en.srt",
                    FileName = "test",
                    Language = "en",
                    Format = ".srt"
                }
            ]);

        var result = await fixture.Service.ForceAsync(1, MediaType.Movie, overwrite: false, CancellationToken.None);

        Assert.Empty(result.Downloaded);
        Assert.Equal([7], result.RequestIds);
        Assert.Equal("Lingarr queued translation for this title.", result.Message);
        fixture.Downloads.Verify(
            service => service.DownloadLanguagesAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<IReadOnlyList<Subtitles>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Requests.Verify(
            service => service.CreateRequest(It.Is<TranslateAbleSubtitle>(request =>
                request.SourceLanguage == "en" && request.TargetLanguage == "ro")),
            Times.Once);
    }

    [Fact]
    public async Task Force_Overwrite_QueuesWithoutDownloaders()
    {
        var fixture = await CreateFixture();
        fixture.Subtitles
            .Setup(service => service.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.en.srt",
                    FileName = "test",
                    Language = "en",
                    Format = ".srt"
                },
                new Subtitles
                {
                    Path = "/movies/test/test.ro.srt",
                    FileName = "test",
                    Language = "ro",
                    Format = ".srt"
                }
            ]);

        var result = await fixture.Service.ForceAsync(1, MediaType.Movie, overwrite: true, CancellationToken.None);

        Assert.Empty(result.Downloaded);
        Assert.Equal([7], result.RequestIds);
        fixture.Downloads.Verify(
            service => service.DownloadLanguagesAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<IReadOnlyList<Subtitles>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Requests.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>()),
            Times.Once);
    }

    [Fact]
    public async Task Force_NoSource_DoesNotCallDownloaders()
    {
        var fixture = await CreateFixture();
        fixture.Subtitles
            .Setup(service => service.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([]);

        var result = await fixture.Service.ForceAsync(1, MediaType.Movie, overwrite: false, CancellationToken.None);

        Assert.True(result.SourceMissing);
        Assert.Empty(result.RequestIds);
        Assert.Equal("Lingarr needs a source subtitle before it can translate.", result.Message);
        fixture.Downloads.Verify(
            service => service.DownloadLanguagesAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<IReadOnlyList<Subtitles>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Requests.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>()),
            Times.Never);
    }

    private static async Task<Fixture> CreateFixture(bool enabled = true)
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new LingarrDbContext(options);
        db.Movies.Add(new Movie
        {
            Id = 1,
            RadarrId = 1,
            Title = "Test",
            Path = "/movies/test",
            FileName = "test",
            DateAdded = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(SettingKeys.SubtitleDownload.Providers))
            .ReturnsAsync("""[{"id":"1","provider":"subdl"}]""");
        settings.Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");
        settings.Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([new SourceLanguage { Code = "en", Name = "English" }]);
        settings.Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);

        var downloads = new Mock<ISubtitleDownloadService>();
        downloads.Setup(service => service.IsEnabledAsync()).ReturnsAsync(enabled);

        var subtitles = new Mock<ISubtitleService>();
        subtitles
            .Setup(service => service.SelectSourceSubtitle(
                It.IsAny<List<Subtitles>>(),
                It.IsAny<HashSet<string>>(),
                It.IsAny<string>()))
            .Returns((List<Subtitles> found, HashSet<string> sources, string _) =>
            {
                var selected = found.FirstOrDefault(item => sources.Contains(item.Language));
                return selected == null
                    ? null
                    : new SelectedSourceSubtitle
                    {
                        Subtitle = selected,
                        SourceLanguage = selected.Language,
                        AvailableLanguages = found.Select(item => item.Language).ToHashSet()
                    };
            });

        var requests = new Mock<ITranslationRequestService>();
        requests.Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>()))
            .ReturnsAsync(7);

        return new Fixture
        {
            Service = new ForceTranslationService(
                db,
                settings.Object,
                subtitles.Object,
                downloads.Object,
                requests.Object,
                NullLogger<ForceTranslationService>.Instance),
            Downloads = downloads,
            Subtitles = subtitles,
            Requests = requests
        };
    }

    private sealed class Fixture
    {
        public required ForceTranslationService Service { get; init; }
        public required Mock<ISubtitleDownloadService> Downloads { get; init; }
        public required Mock<ISubtitleService> Subtitles { get; init; }
        public required Mock<ITranslationRequestService> Requests { get; init; }
    }
}
