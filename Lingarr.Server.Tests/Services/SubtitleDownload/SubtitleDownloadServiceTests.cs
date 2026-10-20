using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.SubtitleDownload;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.SubtitleDownload;

public class SubtitleDownloadServiceTests
{
    [Fact]
    public async Task TryDownload_WalksChainUntilHit()
    {
        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(SettingKeys.SubtitleDownload.Enabled))
            .ReturnsAsync("true");
        settings.Setup(service => service.GetSetting(SettingKeys.SubtitleDownload.Providers))
            .ReturnsAsync("""[{"id":"1","provider":"gestdown"},{"id":"2","provider":"subdl"}]""");

        var gateway = new Mock<ISubtitleDownloadGateway>();
        gateway
            .Setup(service => service.DownloadAsync(
                It.Is<SubtitleProviderDefinition>(provider => provider.Id == "gestdown"),
                It.IsAny<SubtitleDownloadQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubtitleDownloadHit?)null);
        gateway
            .Setup(service => service.DownloadAsync(
                It.Is<SubtitleProviderDefinition>(provider => provider.Id == "subdl"),
                It.IsAny<SubtitleDownloadQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubtitleDownloadHit
            {
                Provider = "subdl",
                Content = "1\n00:00:01,000 --> 00:00:02,000\nHi"u8.ToArray()
            });

        var downloads = new SubtitleDownloadService(
            settings.Object,
            gateway.Object,
            new Mock<ISubtitlePlacementService>().Object,
            new Mock<ILogger<SubtitleDownloadService>>().Object);

        var hit = await downloads.TryDownloadAsync(
            new SubtitleDownloadQuery
            {
                MediaType = MediaType.Movie,
                MediaId = 1,
                Title = "Test",
                Language = "en"
            },
            CancellationToken.None);

        Assert.NotNull(hit);
        Assert.Equal("subdl", hit!.Provider);
        gateway.Verify(
            service => service.DownloadAsync(
                It.IsAny<SubtitleProviderDefinition>(),
                It.IsAny<SubtitleDownloadQuery>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task TryDownload_Off_SkipsProviders()
    {
        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(SettingKeys.SubtitleDownload.Enabled))
            .ReturnsAsync("false");
        settings.Setup(service => service.GetSetting(SettingKeys.SubtitleDownload.Providers))
            .ReturnsAsync("""[{"id":"1","provider":"subdl"}]""");
        var gateway = new Mock<ISubtitleDownloadGateway>();
        var downloads = new SubtitleDownloadService(
            settings.Object,
            gateway.Object,
            new Mock<ISubtitlePlacementService>().Object,
            new Mock<ILogger<SubtitleDownloadService>>().Object);

        var hit = await downloads.TryDownloadAsync(
            new SubtitleDownloadQuery
            {
                MediaType = MediaType.Movie,
                MediaId = 1,
                Title = "Test",
                Language = "en"
            },
            CancellationToken.None);

        Assert.Null(hit);
        gateway.Verify(
            service => service.DownloadAsync(
                It.IsAny<SubtitleProviderDefinition>(),
                It.IsAny<SubtitleDownloadQuery>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DownloadLanguages_PlacesMissingTarget()
    {
        using var folder = new TempFolder();
        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(SettingKeys.SubtitleDownload.Enabled))
            .ReturnsAsync("true");
        settings.Setup(service => service.GetSetting(SettingKeys.SubtitleDownload.Providers))
            .ReturnsAsync("""[{"id":"1","provider":"subdl"}]""");

        var gateway = new Mock<ISubtitleDownloadGateway>();
        gateway
            .Setup(service => service.DownloadAsync(
                It.IsAny<SubtitleProviderDefinition>(),
                It.IsAny<SubtitleDownloadQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubtitleDownloadHit
            {
                Provider = "subdl",
                Content = "1\n00:00:01,000 --> 00:00:02,000\nHi"u8.ToArray(),
                Extension = ".srt"
            });

        var placement = new Mock<ISubtitlePlacementService>();
        placement
            .Setup(service => service.PlaceAsync(
                It.IsAny<IMedia>(),
                It.IsAny<SubtitleDownloadHit>(),
                "ro",
                It.IsAny<IReadOnlyList<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("/movies/test/test.movie.ro.srt");

        var downloads = new SubtitleDownloadService(
            settings.Object,
            gateway.Object,
            placement.Object,
            new Mock<ILogger<SubtitleDownloadService>>().Object);

        var movie = new Movie
        {
            Id = 1,
            RadarrId = 1,
            Title = "Test Movie",
            Path = folder.Path,
            FileName = "test.movie",
            DateAdded = System.DateTime.UtcNow
        };
        var existing = new List<Subtitles>
        {
            new()
            {
                Path = "/movies/test/test.movie.en.srt",
                FileName = "test.movie",
                Language = "en",
                Format = ".srt"
            }
        };

        var placed = await downloads.DownloadLanguagesAsync(
            movie,
            MediaType.Movie,
            ["ro"],
            existing,
            overwrite: false,
            CancellationToken.None);

        Assert.Equal(["ro"], placed);
        placement.Verify(
            service => service.NotifyAsync(
                movie,
                MediaType.Movie,
                "/movies/test/test.movie.ro.srt",
                "ro",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class TempFolder : System.IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
        public string Path => _directory.FullName;
        public void Dispose() => _directory.Delete(true);
    }
}
