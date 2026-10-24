using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hangfire;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

public class SubtitleControllerSeasonTests : IDisposable
{
    private readonly LingarrDbContext _dbContext;
    private readonly Mock<ISubtitleService> _subtitles = new();
    private readonly SubtitleController _controller;

    public SubtitleControllerSeasonTests()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new LingarrDbContext(options);
        _controller = new SubtitleController(
            _subtitles.Object,
            new Mock<ISettingService>().Object,
            new Mock<IBackgroundJobClient>().Object,
            _dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetSeasonSubtitles_UnknownSeason_ReturnsNotFound()
    {
        var result = await _controller.GetSeasonSubtitles(new SeasonSubtitlesRequest { SeasonId = 99 });

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetSeasonSubtitles_EmptyPath_ReturnsEmptyList()
    {
        await SeedSeason(path: "");

        var result = await _controller.GetSeasonSubtitles(new SeasonSubtitlesRequest { SeasonId = 1 });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsType<List<Subtitles>>(ok.Value));
        _subtitles.Verify(
            service => service.GetAllSubtitles(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task GetSeasonSubtitles_KnownPath_UsesSeasonFolder()
    {
        await SeedSeason(path: "/tv/show/Season 01");
        _subtitles
            .Setup(service => service.GetAllSubtitles("/tv/show/Season 01"))
            .ReturnsAsync(
            [
                new Subtitles
                {
                    Path = "/tv/show/Season 01/show.en.srt",
                    FileName = "show.en",
                    Language = "en",
                    Caption = "",
                    Format = ".srt"
                }
            ]);

        var result = await _controller.GetSeasonSubtitles(new SeasonSubtitlesRequest { SeasonId = 1 });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var subtitles = Assert.IsType<List<Subtitles>>(ok.Value);
        Assert.Equal("en", Assert.Single(subtitles).Language);
    }

    private async Task SeedSeason(string path)
    {
        var show = new Show
        {
            Id = 1,
            SonarrId = 10,
            Title = "Show",
            Path = "/tv/show",
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Shows.Add(show);
        _dbContext.Seasons.Add(new Season
        {
            Id = 1,
            SeasonNumber = 1,
            Path = path,
            Show = show
        });
        await _dbContext.SaveChangesAsync();
    }
}
