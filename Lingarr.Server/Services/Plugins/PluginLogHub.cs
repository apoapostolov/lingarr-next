using System.Threading.Channels;
using Lingarr.Contracts.Plugins;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.Plugins;

public readonly record struct PluginLogLine(string Level, string Category, string Message);

public static class PluginLogHub
{
    private static readonly Channel<PluginLogLine> Channel = System.Threading.Channels.Channel.CreateBounded<PluginLogLine>(
        new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });

    private static readonly AsyncLocal<int> Inside = new();

    public static ChannelReader<PluginLogLine> Reader => Channel.Reader;

    public static void Publish(string level, string category, string? message)
    {
        if (Inside.Value > 0 || string.IsNullOrEmpty(message))
        {
            return;
        }

        Channel.Writer.TryWrite(new PluginLogLine(level, category, message));
    }

    public static void Enter() => Inside.Value++;

    public static void Leave()
    {
        if (Inside.Value > 0)
        {
            Inside.Value--;
        }
    }
}

public sealed class PluginLogBridge : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, bool> _enabled = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _stop;
    private Task? _reader;
    private DateTimeOffset _freshUntil;

    public PluginLogBridge(IServiceProvider services)
    {
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stop = new CancellationTokenSource();
        _reader = Task.Run(() => Read(_stop.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stop != null)
        {
            await _stop.CancelAsync();
        }

        if (_reader == null)
        {
            return;
        }

        try
        {
            await _reader.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Stopping the bridge is the point of this call.
        }
    }

    private async Task Read(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var line in PluginLogHub.Reader.ReadAllAsync(cancellationToken))
            {
                PluginLogHub.Enter();
                try
                {
                    await Dispatch(line);
                }
                catch (Exception)
                {
                    // A log sink must not break logging.
                }
                finally
                {
                    PluginLogHub.Leave();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping.
        }
    }

    private async Task Dispatch(PluginLogLine line)
    {
        using var scope = _services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
        foreach (var sink in scope.ServiceProvider.GetServices<ILogSink>())
        {
            if (!await Enabled(settings, sink.Provider))
            {
                continue;
            }

            sink.Write(line.Level, line.Category, line.Message);
        }
    }

    private async Task<bool> Enabled(ISettingService settings, string provider)
    {
        if (DateTimeOffset.UtcNow >= _freshUntil)
        {
            _enabled.Clear();
            _freshUntil = DateTimeOffset.UtcNow.AddSeconds(5);
        }

        if (_enabled.TryGetValue(provider, out var known))
        {
            return known;
        }

        var enabled = PluginCatalog.IsEnabled(
            await settings.GetSetting(PluginCatalog.EnabledKey(provider)));
        _enabled[provider] = enabled;
        return enabled;
    }
}
