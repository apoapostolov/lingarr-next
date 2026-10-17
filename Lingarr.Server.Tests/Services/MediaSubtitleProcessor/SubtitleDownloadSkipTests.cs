using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Models.FileSystem;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.MediaSubtitleProcessor;

public class SubtitleDownloadSkipTests : MediaSubtitleProcessorTestBase
{
    [Fact]
    public async Task ProcessMedia_DownloadedTarget_SkipsTranslation()
    {
        var movie = await CreateTestMovie();
        var sourceOnly = new List<Subtitles>
        {
            new()
            {
                Path = "/movies/test/test.movie.en.srt",
                FileName = "test.movie",
                Language = "en",
                Caption = "",
                Format = ".srt"
            }
        };
        var withTarget = new List<Subtitles>
        {
            sourceOnly[0],
            new()
            {
                Path = "/movies/test/test.movie.ro.srt",
                FileName = "test.movie",
                Language = "ro",
                Caption = "",
                Format = ".srt"
            }
        };

        SubtitleServiceMock
            .SetupSequence(service => service.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(sourceOnly)
            .ReturnsAsync(withTarget);

        SetupStandardSettings();
        SubtitleDownloadServiceMock
            .Setup(service => service.IsEnabledAsync())
            .ReturnsAsync(true);
        SubtitleDownloadServiceMock
            .Setup(service => service.DownloadLanguagesAsync(
                It.IsAny<IMedia>(),
                MediaType.Movie,
                It.Is<IReadOnlyCollection<string>>(languages => languages.Contains("ro")),
                It.IsAny<IReadOnlyList<Subtitles>>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["ro"]);

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.False(result);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<Lingarr.Server.Models.FileSystem.TranslateAbleSubtitle>()),
            Times.Never);
    }
}
