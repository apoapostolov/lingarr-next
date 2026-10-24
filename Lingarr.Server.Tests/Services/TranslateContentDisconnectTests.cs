using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Contracts.Translation;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Plugins;
using Lingarr.Server.Services.Translation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class TranslateContentDisconnectTests : IDisposable
{
    private readonly LingarrDbContext _dbContext;
    private readonly Mock<ITranslationServiceFactory> _factoryMock = new();
    private readonly Mock<ITranslationPromptProfileService> _promptProfiles = new();
    private readonly TranslationRequestService _service;

    public TranslateContentDisconnectTests()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new LingarrDbContext(options);

        var settings = new Mock<ISettingService>();
        settings
            .Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> keys) => keys.ToDictionary(
                key => key,
                key => key == SettingKeys.Translation.ServiceType ? "microsoft" : "false"));

        _factoryMock
            .Setup(factory => factory.CreateTranslationServices(It.IsAny<IReadOnlyList<TranslationChainEntry>>()))
            .Returns(
            [
                new TranslationServiceEntry("microsoft", new Mock<ITranslationService>().Object, null)
            ]);

        _promptProfiles
            .Setup(profiles => profiles.ResolveChainAsync(
                It.IsAny<IReadOnlyList<TranslationChainEntry>>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var media = new Mock<IMediaService>();
        media
            .Setup(service => service.GetMovieIdOrSyncFromRadarrMovieId(It.IsAny<int>()))
            .ReturnsAsync(1);

        _service = new TranslationRequestService(
            _dbContext,
            new Mock<IBackgroundJobClient>().Object,
            new Mock<IHubContext<TranslationRequestsHub>> { DefaultValue = DefaultValue.Mock }.Object,
            _factoryMock.Object,
            new Mock<IProgressService>().Object,
            new Mock<IStatisticsService>().Object,
            media.Object,
            settings.Object,
            new Mock<ISubtitleService>().Object,
            new Mock<ITranslationRequestEventService>().Object,
            new Mock<IProviderHealthService>().Object,
            new Mock<ITranslationQualityService>().Object,
            _promptProfiles.Object,
            new Mock<IClassifierSubtitleGate>().Object,
            new PluginShelf(
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                settings.Object,
                new Mock<ILogger<PluginShelf>>().Object),
            NullLogger<TranslationRequestService>.Instance);
    }

    [Fact]
    public async Task TranslateContent_ActiveDuplicate_ReturnsNull()
    {
        await SeedMovie();
        await SeedRequest(TranslationStatus.InProgress, "stored");

        var result = await _service.TranslateContentAsync(NewContent("First line"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TranslateContent_CancelledClientToken_StillReportsActiveDuplicate()
    {
        await SeedMovie();
        await SeedRequest(TranslationStatus.InProgress, "stored");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _service.TranslateContentAsync(NewContent("First line"), cts.Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task TranslateContent_CompletedMatchingLines_ReusesStoredTargets()
    {
        await SeedMovie();
        await SeedRequest(TranslationStatus.Completed, "stored");

        var result = await _service.TranslateContentAsync(NewContent("First line", "Second line"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("stored 0", result.Single(line => line.Position == 0).Line);
        Assert.Equal("stored 1", result.Single(line => line.Position == 1).Line);
    }

    private async Task SeedMovie()
    {
        _dbContext.Movies.Add(new Movie
        {
            Id = 1,
            RadarrId = 1,
            Title = "Canonical Movie",
            Path = "/movies/test",
            FileName = "test.movie",
            DateAdded = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedRequest(TranslationStatus status, string target)
    {
        var request = new TranslationRequest
        {
            MediaId = 1,
            MediaType = MediaType.Movie,
            Title = "Canonical Movie",
            SourceLanguage = "en",
            TargetLanguage = "es",
            Status = status
        };
        _dbContext.TranslationRequests.Add(request);
        _dbContext.TranslationRequestLines.AddRange(NewContent("First line", "Second line").Lines
            .Select(line => new TranslationRequestLine
            {
                TranslationRequest = request,
                Position = line.Position,
                Source = line.Line,
                Target = $"{target} {line.Position}"
            }));
        await _dbContext.SaveChangesAsync();
    }

    private static TranslateAbleSubtitleContent NewContent(params string[] lines) => new()
    {
        ArrMediaId = 1,
        Title = "Canonical Movie",
        SourceLanguage = "en",
        TargetLanguage = "es",
        MediaType = MediaType.Movie,
        Lines = lines.Select((line, index) => new BatchSubtitleLine
        {
            Position = index,
            Line = line
        }).ToList()
    };

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
