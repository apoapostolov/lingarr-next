using System.Diagnostics;

namespace Lingarr.Server.Services.Plugins;

public sealed class ExternalCommandRequest
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}

public sealed class ExternalCommandResult
{
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
}

public static class ExternalCommandRunner
{
    private static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase)
    {
        "sh", "bash", "dash", "zsh", "cmd", "cmd.exe", "powershell", "powershell.exe", "pwsh", "pwsh.exe"
    };

    public static async Task<ExternalCommandResult> RunAsync(
        ExternalCommandRequest request,
        CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(request.FileName);
        if (Shells.Contains(name))
        {
            throw new InvalidOperationException("Lingarr does not run a shell for a plugin command.");
        }

        var start = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? Environment.CurrentDirectory
                : request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in request.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ExternalCommandResult
            {
                ExitCode = -1,
                TimedOut = true,
                StandardOutput = await stdout,
                StandardError = await stderr
            };
        }

        return new ExternalCommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = await stdout,
            StandardError = await stderr
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already left.
        }
    }
}
