using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Plugins;
using Lingarr.Plugin.StyleSample;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plugins;

public class PluginToolTests
{
    [Fact]
    public async Task Command_PrintsArgumentsWithoutAShell_AndTimesOut()
    {
        var printed = await ExternalCommandRunner.RunAsync(new ExternalCommandRequest
        {
            FileName = "/usr/bin/printf",
            Arguments = ["%s", "hello"],
            Timeout = TimeSpan.FromSeconds(5)
        }, CancellationToken.None);
        Assert.False(printed.TimedOut);
        Assert.Equal(0, printed.ExitCode);
        Assert.Equal("hello", printed.StandardOutput);

        var slept = await ExternalCommandRunner.RunAsync(new ExternalCommandRequest
        {
            FileName = "/bin/sleep",
            Arguments = ["5"],
            Timeout = TimeSpan.FromMilliseconds(200)
        }, CancellationToken.None);
        Assert.True(slept.TimedOut);
    }

    [Fact]
    public async Task Command_RefusesAShell()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExternalCommandRunner.RunAsync(
            new ExternalCommandRequest
            {
                FileName = "bash",
                Arguments = ["-c", "echo hi"]
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task FixtureCommand_CopiesOneTrackIntoAnSrt()
    {
        var directory = Directory.CreateTempSubdirectory("lingarr-track").FullName;
        try
        {
            var track = Path.Combine(directory, "clip.track");
            await File.WriteAllTextAsync(track, "1\n00:00:01,000 --> 00:00:02,000\nBonjour\n");
            var tool = new FixtureExtractTool(new DirectCommand());
            var wrote = await tool.TryExtractAsync(new ExtractToolRequest
            {
                Directory = directory,
                MediaFileName = "clip",
                Codec = "fixture"
            }, CancellationToken.None);

            Assert.True(wrote);
            var subtitle = await File.ReadAllTextAsync(Path.Combine(directory, "clip.en.srt"));
            Assert.Contains("Bonjour", subtitle, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DeskSource_LeavesASidecarTheHostCanRead()
    {
        var directory = Directory.CreateTempSubdirectory("lingarr-desk").FullName;
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "clip.desk.txt"),
                "1\n00:00:01,000 --> 00:00:02,000\nHola\n");
            var settings = new Mock<ISettingService>();
            settings.Setup(service => service.GetSetting(PluginCatalog.EnabledKey("desk-source")))
                .ReturnsAsync("true");
            settings.Setup(service => service.GetSetting(PluginCatalog.OrderKey("desk-source")))
                .ReturnsAsync("10");
            settings.Setup(service => service.GetSetting(PluginCatalog.PolicyKey("desk-source")))
                .ReturnsAsync("skip");
            var runner = new PluginToolRunner(
                Array.Empty<IExtractTool>(),
                [new DeskSource()],
                settings.Object,
                NullLogger<PluginToolRunner>.Instance);

            var supplied = await runner.TrySupplySourceAsync(directory, "clip", "en", CancellationToken.None);

            Assert.True(supplied);
            Assert.Contains(
                "Hola",
                await File.ReadAllTextAsync(Path.Combine(directory, "clip.en.srt")),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class DirectCommand : IPluginCommand
    {
        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            ExternalCommandRunner.RunAsync(new ExternalCommandRequest
            {
                FileName = fileName,
                Arguments = arguments,
                Timeout = timeout
            }, cancellationToken).ContinueWith(
                task => task.Result.TimedOut ? -1 : task.Result.ExitCode,
                cancellationToken);
    }
}
