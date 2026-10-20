using System;
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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.SubtitleDownload;

public class SubtitleDownloadReliabilityTests
{
    [Fact]
    public async Task MissingDirectory_DoesNotSearchProviders()
    {
        var gateway = new Mock<ISubtitleDownloadGateway>(MockBehavior.Strict);
        var service = Service(gateway.Object, Mock.Of<ISubtitlePlacementService>());
        var placed = await service.DownloadLanguagesAsync(
            Movie(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            MediaType.Movie, ["en"], [], false, CancellationToken.None);
        Assert.Empty(placed);
        gateway.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlacementIoFailure_DoesNotAbortTheCaller(bool unauthorized)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var gateway = new Mock<ISubtitleDownloadGateway>();
            gateway.Setup(g => g.DownloadAsync(It.IsAny<SubtitleProviderDefinition>(),
                    It.IsAny<SubtitleDownloadQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SubtitleDownloadHit { Provider = "subdl", Content = new byte[64] });
            var placement = new Mock<ISubtitlePlacementService>();
            placement.Setup(p => p.PlaceAsync(It.IsAny<IMedia>(), It.IsAny<SubtitleDownloadHit>(),
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<Subtitles>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(unauthorized ? new UnauthorizedAccessException("Denied") : new IOException("No such device"));
            var service = Service(gateway.Object, placement.Object);
            var placed = await service.DownloadLanguagesAsync(
                Movie(folder.FullName), MediaType.Movie, ["en", "bg"], [], false, CancellationToken.None);
            Assert.Empty(placed);
            gateway.Verify(g => g.DownloadAsync(It.IsAny<SubtitleProviderDefinition>(),
                It.IsAny<SubtitleDownloadQuery>(), It.IsAny<CancellationToken>()), Times.Once);
            placement.Verify(p => p.NotifyAsync(It.IsAny<IMedia>(), It.IsAny<MediaType>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task Cancellation_DoesNotBecomeAMiss()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var service = Service(Mock.Of<ISubtitleDownloadGateway>(), Mock.Of<ISubtitlePlacementService>());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadLanguagesAsync(
                Movie(folder.FullName), MediaType.Movie, ["en"], [], false, cts.Token));
        }
        finally { folder.Delete(true); }
    }

    private static Movie Movie(string path) => new()
    {
        RadarrId = 1, Title = "Example Movie", FileName = "Example.Movie", Path = path, DateAdded = DateTime.UtcNow
    };

    private static SubtitleDownloadService Service(ISubtitleDownloadGateway gateway, ISubtitlePlacementService placement)
    {
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetSetting(SettingKeys.SubtitleDownload.Enabled)).ReturnsAsync("true");
        settings.Setup(s => s.GetSetting(SettingKeys.SubtitleDownload.Providers))
            .ReturnsAsync("""[{"provider":"subdl"}]""");
        return new SubtitleDownloadService(settings.Object, gateway, placement, NullLogger<SubtitleDownloadService>.Instance);
    }
}
