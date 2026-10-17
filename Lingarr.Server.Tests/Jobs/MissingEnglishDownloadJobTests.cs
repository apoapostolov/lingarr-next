using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.SubtitleDownload;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class MissingEnglishDownloadJobTests
{
    [Fact]
    public void HasEnglish_AcceptsEnUs()
    {
        Assert.True(MissingEnglishDownloadJob.HasEnglish(
        [
            new Subtitles { Language = "en-US" }
        ]));
        Assert.False(MissingEnglishDownloadJob.HasEnglish(
        [
            new Subtitles { Language = "bg" }
        ]));
    }

    [Fact]
    public void FormatSummary_NamesCounts()
    {
        var text = MissingEnglishDownloadJob.FormatSummary(10, 20, 4, 1, 2, 1, true);
        Assert.Equal(
            "Finished. Looked at 10 of 20 titles. Missing English on 4. Placed 1. No match on 2. Failed on 1.",
            text);
    }

    [Fact]
    public async Task Execute_PlacesEnglishWhenMissing()
    {
        var directory = Directory.CreateTempSubdirectory();
        MissingEnglishDownloadJob.Exit();
        MissingEnglishDownloadJob.TryEnter();
        try
        {
            await using var db = BuildContext();
            db.Movies.Add(Movie(directory.FullName, "Missing English"));
            await db.SaveChangesAsync();

            var subtitles = new Mock<ISubtitleService>();
            subtitles.Setup(service => service.GetSubtitles(directory.FullName, "movie"))
                .ReturnsAsync([]);

            var downloads = new Mock<ISubtitleDownloadService>();
            downloads.Setup(service => service.IsEnabledAsync()).ReturnsAsync(true);
            downloads
                .Setup(service => service.TryDownloadAsync(
                    It.Is<SubtitleDownloadQuery>(query => query.Language == "en"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SubtitleDownloadHit
                {
                    Provider = "opensubtitlescom",
                    Content = "1\n00:00:01,000 --> 00:00:02,000\nHi"u8.ToArray()
                });

            var placement = new Mock<ISubtitlePlacementService>();
            placement
                .Setup(service => service.PlaceAsync(
                    It.IsAny<IMedia>(),
                    It.IsAny<SubtitleDownloadHit>(),
                    "en",
                    It.IsAny<IReadOnlyList<Subtitles>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Path.Combine(directory.FullName, "movie.en.srt"));

            var settings = new InMemorySettings();
            await settings.SetSetting(SettingKeys.SubtitleDownload.Providers, """[{"id":"1","provider":"opensubtitlescom"}]""");
            var jobs = new Mock<IBackgroundJobClient>();
            var job = new MissingEnglishDownloadJob(
                db,
                settings,
                subtitles.Object,
                downloads.Object,
                placement.Object,
                jobs.Object,
                NullLogger<MissingEnglishDownloadJob>.Instance);

            await job.Execute(0, 0, 0, 0, 0, 0);

            Assert.Equal("succeeded", await settings.GetSetting(SettingKeys.SubtitleDownload.MissingEnglishStatus));
            Assert.Contains("Placed 1", await settings.GetSetting(SettingKeys.SubtitleDownload.MissingEnglishSummary));
            placement.Verify(
                service => service.NotifyAsync(
                    It.IsAny<IMedia>(),
                    It.IsAny<MediaType>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            jobs.Verify(
                client => client.Create(It.IsAny<Job>(), It.IsAny<IState>()),
                Times.Never);
        }
        finally
        {
            MissingEnglishDownloadJob.Exit();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_SkipsWhenEnglishPresent()
    {
        var directory = Directory.CreateTempSubdirectory();
        MissingEnglishDownloadJob.Exit();
        MissingEnglishDownloadJob.TryEnter();
        try
        {
            await using var db = BuildContext();
            db.Movies.Add(Movie(directory.FullName, "Has English"));
            await db.SaveChangesAsync();

            var subtitles = new Mock<ISubtitleService>();
            subtitles.Setup(service => service.GetSubtitles(directory.FullName, "movie"))
                .ReturnsAsync([new Subtitles { Language = "en" }]);

            var downloads = new Mock<ISubtitleDownloadService>();
            downloads.Setup(service => service.IsEnabledAsync()).ReturnsAsync(true);

            var settings = new InMemorySettings();
            await settings.SetSetting(SettingKeys.SubtitleDownload.Providers, """[{"id":"1","provider":"gestdown"}]""");
            var job = new MissingEnglishDownloadJob(
                db,
                settings,
                subtitles.Object,
                downloads.Object,
                new Mock<ISubtitlePlacementService>().Object,
                new Mock<IBackgroundJobClient>().Object,
                NullLogger<MissingEnglishDownloadJob>.Instance);

            await job.Execute(0, 0, 0, 0, 0, 0);

            downloads.Verify(
                service => service.TryDownloadAsync(
                    It.IsAny<SubtitleDownloadQuery>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.Contains("Missing English on 0", await settings.GetSetting(SettingKeys.SubtitleDownload.MissingEnglishSummary));
        }
        finally
        {
            MissingEnglishDownloadJob.Exit();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_CountsMissWhenProvidersHaveNothing()
    {
        var directory = Directory.CreateTempSubdirectory();
        MissingEnglishDownloadJob.Exit();
        MissingEnglishDownloadJob.TryEnter();
        try
        {
            await using var db = BuildContext();
            db.Movies.Add(Movie(directory.FullName, "No Match"));
            await db.SaveChangesAsync();

            var subtitles = new Mock<ISubtitleService>();
            subtitles.Setup(service => service.GetSubtitles(directory.FullName, "movie"))
                .ReturnsAsync([]);

            var downloads = new Mock<ISubtitleDownloadService>();
            downloads.Setup(service => service.IsEnabledAsync()).ReturnsAsync(true);
            downloads
                .Setup(service => service.TryDownloadAsync(
                    It.IsAny<SubtitleDownloadQuery>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((SubtitleDownloadHit?)null);

            var settings = new InMemorySettings();
            await settings.SetSetting(SettingKeys.SubtitleDownload.Providers, """[{"id":"1","provider":"gestdown"}]""");
            var job = new MissingEnglishDownloadJob(
                db,
                settings,
                subtitles.Object,
                downloads.Object,
                new Mock<ISubtitlePlacementService>().Object,
                new Mock<IBackgroundJobClient>().Object,
                NullLogger<MissingEnglishDownloadJob>.Instance);

            await job.Execute(0, 0, 0, 0, 0, 0);

            Assert.Contains("No match on 1", await settings.GetSetting(SettingKeys.SubtitleDownload.MissingEnglishSummary));
            Assert.Contains("Placed 0", await settings.GetSetting(SettingKeys.SubtitleDownload.MissingEnglishSummary));
        }
        finally
        {
            MissingEnglishDownloadJob.Exit();
            directory.Delete(recursive: true);
        }
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static Movie Movie(string path, string title) => new()
    {
        RadarrId = 1,
        Title = title,
        FileName = "movie",
        Path = path,
        DateAdded = DateTime.UtcNow.AddDays(-10)
    };

    private sealed class InMemorySettings : ISettingService
    {
        private readonly Dictionary<string, string> _values = new();

        public event SettingChangedHandler? SettingChanged
        {
            add { }
            remove { }
        }

        public Task<string?> GetSetting(string key) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task<Dictionary<string, string>> GetSettings(IEnumerable<string> keys)
        {
            return Task.FromResult(keys.ToDictionary(
                static key => key,
                key => _values.TryGetValue(key, out var value) ? value : string.Empty));
        }

        public Task<bool> SetSetting(string key, string value)
        {
            _values[key] = value;
            return Task.FromResult(true);
        }

        public Task UpsertSetting(string key, string value) => SetSetting(key, value);

        public Task<bool> SetSettings(Dictionary<string, string> settings)
        {
            foreach (var pair in settings)
            {
                _values[pair.Key] = pair.Value;
            }

            return Task.FromResult(true);
        }

        public Task<List<T>> GetSettingAsJson<T>(string key) where T : class => Task.FromResult(new List<T>());
        public Task<bool> SetEncryptedSetting(string key, string value) => SetSetting(key, value);
        public Task<string?> GetEncryptedSetting(string key) => GetSetting(key);
        public Task<Dictionary<string, string>> GetEncryptedSettings(IEnumerable<string> keys) => GetSettings(keys);
    }
}
