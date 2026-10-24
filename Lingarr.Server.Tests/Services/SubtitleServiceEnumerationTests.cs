using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class SubtitleServiceEnumerationTests
{
    [Fact]
    public async Task GetAllSubtitles_IgnoresTrailersAndFindsTopLevelAndSubsFolder()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            // Top-level sidecars
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "Movie.en.srt"), "1");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "Movie.bg.srt"), "1");

            // Allowed subdir
            var subs = Directory.CreateDirectory(Path.Combine(root.FullName, "Subs"));
            await File.WriteAllTextAsync(Path.Combine(subs.FullName, "Movie.fr.srt"), "1");

            // Junk extras — must not be scanned
            var trailers = Directory.CreateDirectory(Path.Combine(root.FullName, "Trailers"));
            await File.WriteAllTextAsync(Path.Combine(trailers.FullName, "trailer.en.srt"), "1");
            var featurettes = Directory.CreateDirectory(Path.Combine(root.FullName, "Featurettes"));
            await File.WriteAllTextAsync(Path.Combine(featurettes.FullName, "extra.en.srt"), "1");

            var service = new SubtitleService(
                NullLogger<SubtitleService>.Instance,
                new LanguageCodeService());

            var all = await service.GetAllSubtitles(root.FullName);
            var names = all.Select(s => Path.GetFileName(s.Path)).OrderBy(n => n).ToList();

            Assert.Contains("Movie.en.srt", names);
            Assert.Contains("Movie.bg.srt", names);
            Assert.Contains("Movie.fr.srt", names);
            Assert.DoesNotContain("trailer.en.srt", names);
            Assert.DoesNotContain("extra.en.srt", names);
            Assert.Equal(3, all.Count);
            Assert.Equal("en", all.Single(s => s.Path.EndsWith("Movie.en.srt")).Language);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetSubtitles_MatchesOnlyRequestedMediaFile()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "Show.S01E01.en.srt"), "1");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "Show.S01E01.bg.srt"), "1");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "Show.S01E02.en.srt"), "1");

            var service = new SubtitleService(
                NullLogger<SubtitleService>.Instance,
                new LanguageCodeService());

            var matches = await service.GetSubtitles(root.FullName, "Show.S01E01");
            Assert.Equal(2, matches.Count);
            Assert.All(matches, s => Assert.StartsWith("Show.S01E01", s.FileName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetSubtitles_MatchesReleaseNameDrift()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root.FullName, "A Spark Story (2021) WEBRip-1080p.eng.srt"),
                "1\n00:00:01,000 --> 00:00:02,000\nHello.\n");
            await File.WriteAllTextAsync(
                Path.Combine(root.FullName, "Show.S01E02.WEBRip-1080p.eng.srt"),
                "1\n00:00:01,000 --> 00:00:02,000\nOther episode.\n");

            var service = new SubtitleService(
                NullLogger<SubtitleService>.Instance,
                new LanguageCodeService());

            var movie = await service.GetSubtitles(
                root.FullName,
                "A Spark Story (2021) {tmdb-852724} [WEBDL-1080p][EAC3 5.1][h264]-KOGi");
            Assert.Single(movie);
            Assert.EndsWith("A Spark Story (2021) WEBRip-1080p.eng.srt", movie[0].Path);

            var episode = await service.GetSubtitles(
                root.FullName,
                "Show.S01E01.WEBDL-1080p");
            Assert.Empty(episode);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetAllSubtitles_UsesUnknownWhenTheFileNameHasNoLanguage()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "Movie.srt"), "1");

            var service = new SubtitleService(
                NullLogger<SubtitleService>.Instance,
                new LanguageCodeService());

            var all = await service.GetAllSubtitles(root.FullName);

            Assert.Single(all);
            Assert.Equal("unknown", all[0].Language);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void SelectSourceSubtitle_SkipsHtmlSidecar()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var htmlEn = Path.Combine(root.FullName, "Movie.en.srt");
            File.WriteAllText(
                htmlEn,
                "<!DOCTYPE html>\n<html lang=\"bg\"><head><title>Субтитри BG</title></head><body>download</body></html>");
            var htmlBg = Path.Combine(root.FullName, "Movie.bg.srt");
            File.WriteAllText(
                htmlBg,
                "<!DOCTYPE html>\n<html lang=\"bg\"><head><title>Субтитри BG</title></head><body>download</body></html>");
            var realEn = Path.Combine(root.FullName, "Movie.real.en.srt");
            File.WriteAllText(
                realEn,
                "1\n00:00:01,000 --> 00:00:02,000\nStay quiet.\n\n2\n00:00:03,000 --> 00:00:04,000\nThe night nurse is here.\n\n3\n00:00:05,000 --> 00:00:06,000\nYou don't have to get up.\n\n4\n00:00:07,000 --> 00:00:08,000\nI have to see my new client.\n");

            var service = new SubtitleService(
                NullLogger<SubtitleService>.Instance,
                new LanguageCodeService());
            var sources = new HashSet<string> { "en" };

            Assert.Null(service.SelectSourceSubtitle(
                [
                    new Subtitles
                    {
                        Path = htmlEn,
                        FileName = "Movie.en",
                        Language = "en",
                        Format = ".srt"
                    }
                ],
                sources,
                "true"));

            var withReal = service.SelectSourceSubtitle(
                [
                    new Subtitles
                    {
                        Path = htmlEn,
                        FileName = "Movie.en",
                        Language = "en",
                        Format = ".srt"
                    },
                    new Subtitles
                    {
                        Path = realEn,
                        FileName = "Movie.real.en",
                        Language = "en",
                        Format = ".srt"
                    }
                ],
                sources,
                "true");
            Assert.NotNull(withReal);
            Assert.Equal(realEn, withReal.Subtitle.Path);

            var htmlTarget = service.SelectSourceSubtitle(
                [
                    new Subtitles
                    {
                        Path = realEn,
                        FileName = "Movie.real.en",
                        Language = "en",
                        Format = ".srt"
                    },
                    new Subtitles
                    {
                        Path = htmlBg,
                        FileName = "Movie.bg",
                        Language = "bg",
                        Format = ".srt"
                    }
                ],
                sources,
                "true");
            Assert.NotNull(htmlTarget);
            Assert.DoesNotContain("bg", htmlTarget.AvailableLanguages);
            Assert.Contains("en", htmlTarget.AvailableLanguages);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void SelectSourceSubtitle_MatchesSourceCodeCaseInsensitively()
    {
        var service = new SubtitleService(
            NullLogger<SubtitleService>.Instance,
            new LanguageCodeService());

        var selected = service.SelectSourceSubtitle(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.en.srt",
                    FileName = "test.en",
                    Language = "en",
                    Caption = "",
                    Format = ".srt"
                }
            ],
            new HashSet<string> { "EN" },
            "true");

        Assert.NotNull(selected);
        Assert.Equal("en", selected.SourceLanguage);
        Assert.Equal("/movies/test/test.en.srt", selected.Subtitle.Path);
    }

    [Fact]
    public void SelectSourceSubtitle_RegionalSourceUsesNeutralFile()
    {
        var service = new SubtitleService(
            NullLogger<SubtitleService>.Instance,
            new LanguageCodeService());

        var selected = service.SelectSourceSubtitle(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.en.srt",
                    FileName = "test.en",
                    Language = "en",
                    Caption = "",
                    Format = ".srt"
                }
            ],
            new HashSet<string> { "en-US" },
            "true");

        Assert.NotNull(selected);
        Assert.Equal("en", selected.SourceLanguage);
        Assert.Equal("/movies/test/test.en.srt", selected.Subtitle.Path);
    }

    [Fact]
    public void SelectSourceSubtitle_PrefersExactRegionalOverNeutral()
    {
        var service = new SubtitleService(
            NullLogger<SubtitleService>.Instance,
            new LanguageCodeService());

        var selected = service.SelectSourceSubtitle(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.pt.srt",
                    FileName = "test.pt",
                    Language = "pt",
                    Caption = "",
                    Format = ".srt"
                },
                new Subtitles
                {
                    Path = "/movies/test/test.pt-BR.srt",
                    FileName = "test.pt-BR",
                    Language = "pt-br",
                    Caption = "",
                    Format = ".srt"
                }
            ],
            new HashSet<string> { "pt-BR" },
            "true");

        Assert.NotNull(selected);
        Assert.Equal("pt-br", selected.SourceLanguage);
        Assert.Equal("/movies/test/test.pt-BR.srt", selected.Subtitle.Path);
    }
}
