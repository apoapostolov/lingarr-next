using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Providers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lingarr.Server.Tests.Providers;

public class LogBufferTests
{
    [Fact]
    public void Page_ReturnsTheNewestLinesAndTheOlderPage()
    {
        var buffer = new LogBuffer(maxCount: 20);
        for (var index = 1; index <= 5; index++)
        {
            buffer.Add(Line(LogLevel.Information, $"line {index}"));
        }

        var latest = buffer.Page(before: null, after: null, limit: 2, minimum: null);
        Assert.Equal(["line 4", "line 5"], latest.Items.Select(item => item.Message).ToArray());
        Assert.True(latest.HasOlder);
        Assert.Equal(5, latest.NewestId);

        var older = buffer.Page(before: latest.OldestId, after: null, limit: 2, minimum: null);
        Assert.Equal(["line 2", "line 3"], older.Items.Select(item => item.Message).ToArray());
        Assert.True(older.HasOlder);

        var first = buffer.Page(before: older.OldestId, after: null, limit: 2, minimum: null);
        Assert.Equal(["line 1"], first.Items.Select(item => item.Message).ToArray());
        Assert.False(first.HasOlder);
    }

    [Fact]
    public void Page_FiltersByMinimumLevel()
    {
        var buffer = new LogBuffer();
        buffer.Add(Line(LogLevel.Information, "quiet"));
        buffer.Add(Line(LogLevel.Warning, "careful"));
        buffer.Add(Line(LogLevel.Error, "broken"));

        var errors = buffer.Page(before: null, after: null, limit: 10, minimum: LogLevel.Error);
        Assert.Equal(["broken"], errors.Items.Select(item => item.Message).ToArray());

        var after = buffer.Page(before: null, after: errors.Items[0].Id, limit: 10, minimum: LogLevel.Warning);
        Assert.Empty(after.Items);
    }

    [Fact]
    public void Add_DropsLinesPastTheCapAndTheAgeLimit()
    {
        var aged = new LogBuffer(maxCount: 5, maxAge: TimeSpan.FromMinutes(10));
        aged.Add(Line(LogLevel.Information, "old", DateTime.UtcNow.AddHours(-2)));
        aged.Add(Line(LogLevel.Information, "kept"));
        Assert.Equal(
            ["kept"],
            aged.Page(before: null, after: null, limit: 10, minimum: null).Items.Select(item => item.Message).ToArray());

        var capped = new LogBuffer(maxCount: 2);
        capped.Add(Line(LogLevel.Information, "one"));
        capped.Add(Line(LogLevel.Information, "two"));
        var third = capped.Add(Line(LogLevel.Information, "three"));
        var page = capped.Page(before: null, after: null, limit: 10, minimum: null);
        Assert.Equal(["two", "three"], page.Items.Select(item => item.Message).ToArray());
        Assert.Equal(third.Id, page.LatestId);
    }

    [Fact]
    public void Clear_RemovesLinesAndKeepsTheIdSequence()
    {
        var buffer = new LogBuffer();
        var first = buffer.Add(Line(LogLevel.Information, "first"));
        buffer.Clear();
        var second = buffer.Add(Line(LogLevel.Information, "second"));

        Assert.Empty(buffer.Page(before: null, after: null, limit: 10, minimum: null).Items.Where(item => item.Id == first.Id));
        Assert.True(second.Id > first.Id);
    }

    [Fact]
    public async Task WaitAsync_ReturnsALineThatIsAlreadyThere()
    {
        var buffer = new LogBuffer();
        var error = buffer.Add(Line(LogLevel.Error, "Plex connection failed. The token was rejected."));

        var started = DateTime.UtcNow;
        var items = await buffer.WaitAsync(error.Id - 1, LogLevel.Error, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Contains(items, item => item.Id == error.Id);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
        Assert.Equal(
            "Check the API key or token for this service under Settings → Connections.",
            error.Hint);
    }

    [Fact]
    public async Task WaitAsync_EndsWhenNothingArrives()
    {
        var buffer = new LogBuffer();
        var items = await buffer.WaitAsync(buffer.LatestId, LogLevel.Critical, TimeSpan.FromMilliseconds(40), CancellationToken.None);
        Assert.Empty(items);
    }

    [Fact]
    public void Hint_ExplainsAMissingFolderAndLeavesInformationAlone()
    {
        Assert.Equal(
            "Check the folder mapping for this library under Settings → Connections → Path mapping.",
            LogHints.For(LogLevel.Warning, "Directory not found at path: /media/movies", null));
        Assert.Null(LogHints.For(LogLevel.Information, "Directory not found", null));
        Assert.Contains("webhook", LogHints.For(LogLevel.Warning, "Emby webhook payload was not JSON.", null), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Logger_StoresInformationAndSkipsDebug()
    {
        var logger = new InMemoryLogger("Lingarr.Server.Tests.Providers.Sample");
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    private static LogEntry Line(LogLevel level, string message, DateTime? timestamp = null) => new()
    {
        LogLevel = level,
        Message = message,
        Timestamp = timestamp ?? DateTime.UtcNow,
        Category = "Lingarr.Server.Tests.Sample"
    };
}
