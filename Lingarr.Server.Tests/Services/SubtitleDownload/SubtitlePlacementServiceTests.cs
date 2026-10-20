using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Plugins;
using Lingarr.Server.Services.SubtitleDownload;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.SubtitleDownload;

public class SubtitlePlacementServiceTests
{
    [Fact]
    public async Task PlaceAsync_WritesBesideTheVideoNotAShiftedSidecar()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var fileName = "BURN·E (2008) {tmdb-130925} [Bluray-1080p][DTS 5.1][x264]-DON";
            var shifted = Path.Combine(folder.FullName, fileName + ".shifted.en.srt");
            await File.WriteAllTextAsync(shifted, "<!DOCTYPE html><html><head><title>junk</title></head></html>");

            var subtitles = new SubtitleService(
                NullLogger<SubtitleService>.Instance,
                new LanguageCodeService());
            var placement = new SubtitlePlacementService(
                subtitles,
                new MediaLibraryRefreshService(
                    Mock.Of<IHttpClientFactory>(),
                    Mock.Of<ISettingService>(),
                    Mock.Of<IPlexClient>(),
                    NullLogger<MediaLibraryRefreshService>.Instance),
                Mock.Of<IPlexSubtitleSelector>(),
                new PluginSignals(
                    [],
                    [],
                    [],
                    [],
                    [],
                    [],
                    Mock.Of<ISettingService>(),
                    NullLogger<PluginSignals>.Instance),
                NullLogger<SubtitlePlacementService>.Instance);

            var path = await placement.PlaceAsync(
                new Movie
                {
                    RadarrId = 1,
                    Title = "BURN·E",
                    FileName = fileName,
                    Path = folder.FullName,
                    DateAdded = System.DateTime.UtcNow
                },
                new SubtitleDownloadHit
                {
                    Provider = "subtitlecat",
                    Content = "1\n00:00:01,000 --> 00:00:02,000\nHello.\n"u8.ToArray()
                },
                "en",
                [new Subtitles { Path = shifted, Language = "en", FileName = fileName + ".shifted", Format = ".srt" }],
                CancellationToken.None);

            Assert.Equal(Path.Combine(folder.FullName, fileName + ".en.srt"), path);
            Assert.True(File.Exists(path));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PlaceAsync_DoesNotRecreateAMissingMediaDirectory()
    {
        var missing = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        var placement = new SubtitlePlacementService(
            Mock.Of<ISubtitleService>(), null!, null!, null!, NullLogger<SubtitlePlacementService>.Instance);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => placement.PlaceAsync(
            new Movie { RadarrId = 1, Title = "Example Movie", FileName = "Example.Movie", Path = missing, DateAdded = System.DateTime.UtcNow },
            new SubtitleDownloadHit { Provider = "fixture", Content = "1\n00:00:01,000 --> 00:00:02,000\nHello there.\n"u8.ToArray() },
            "en", [], CancellationToken.None));
        Assert.False(Directory.Exists(missing));
    }
}
