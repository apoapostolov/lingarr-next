using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Services.Integration.Bazarr;
using Lingarr.Server.Services.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Integration;

public class BazarrReliabilityTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpFailure_IsAWarningRatherThanNoSource(HttpStatusCode status)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            await using var db = Database();
            using var handler = new Handler(status);
            var http = new Mock<IHttpClientFactory>();
            http.Setup(h => h.CreateClient("bazarr")).Returns(() => new HttpClient(handler, false));
            var settings = Settings();
            var logger = new Mock<ILogger<BazarrService>>();
            var service = new BazarrService(settings.Object, http.Object, Mock.Of<ISubtitleService>(),
                db, logger.Object, Shelf(settings));
            var found = await service.TryEnsureSource(Movie(folder.FullName), MediaType.Movie,
                new HashSet<string> { "en" }, CancellationToken.None);
            Assert.False(found);
            Assert.Equal(1, handler.Calls);
            Assert.Contains(logger.Invocations, i => i.Method.Name == "Log"
                && Equals(i.Arguments[0], LogLevel.Warning)
                && i.Arguments[2].ToString()!.Contains(((int)status).ToString()));
            Assert.DoesNotContain(logger.Invocations, i => i.Method.Name == "Log"
                && i.Arguments[2].ToString()!.Contains("no source subtitle"));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task MissingDirectory_RetryDefersWithoutSearchingAndKeepsSchedule()
    {
        await using var db = Database();
        var movie = Movie(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        var settings = Settings();
        var bazarr = new Mock<IBazarrService>();
        bazarr.Setup(b => b.IsEnabled()).ReturnsAsync(true);
        var subtitles = new Mock<ISubtitleService>(MockBehavior.Strict);
        var jobs = new Mock<IBackgroundJobClient>();
        var job = new BazarrRetryJob(db, settings.Object, subtitles.Object, bazarr.Object,
            Mock.Of<IMediaSubtitleProcessor>(), jobs.Object, Shelf(settings), NullLogger<BazarrRetryJob>.Instance);
        try
        {
            await job.Execute(movie.Id, (int)MediaType.Movie);
            subtitles.VerifyNoOtherCalls();
            bazarr.Verify(b => b.TryEnsureSource(It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()), Times.Never);
            jobs.Verify(j => j.Create(It.Is<Job>(j => j.Type == typeof(BazarrRetryJob)),
                It.Is<IState>(s => s is ScheduledState)), Times.Once);
        }
        finally { BazarrRetryJob.Clear(movie.Id, MediaType.Movie); }
    }

    private static Mock<ISettingService> Settings()
    {
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetSetting(SettingKeys.Integration.BazarrEnabled)).ReturnsAsync("true");
        settings.Setup(s => s.GetSetting(SettingKeys.Integration.BazarrUrl)).ReturnsAsync("http://bazarr.test");
        settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.BazarrApiKey)).ReturnsAsync("fixture-key");
        settings.Setup(s => s.GetSettingAsJson<Lingarr.Contracts.Models.SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([new() { Code = "en", Name = "English" }]);
        settings.Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>())).ReturnsAsync(new Dictionary<string, string>());
        return settings;
    }

    private static PluginShelf Shelf(Mock<ISettingService> settings) =>
        new([], [], [], [], [], [], [], [], [], settings.Object, NullLogger<PluginShelf>.Instance);
    private static LingarrDbContext Database() => new(new DbContextOptionsBuilder<LingarrDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Movie Movie(string path) => new()
    { RadarrId = 1, Title = "Example Movie", FileName = "Example.Movie", Path = path, DateAdded = DateTime.UtcNow };

    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
