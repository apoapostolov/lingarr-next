using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
}
