using Hangfire;
using Hangfire.Storage;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Integration.Plex;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

public class PlexLibraryRefreshJob
{
    private static int _gate;

    private readonly PlexLibraryRefreshService _refresh;
    private readonly IScheduleService _scheduleService;
    private readonly ILogger<PlexLibraryRefreshJob> _logger;

    public PlexLibraryRefreshJob(
        PlexLibraryRefreshService refresh,
        IScheduleService scheduleService,
        ILogger<PlexLibraryRefreshJob> logger)
    {
        _refresh = refresh;
        _scheduleService = scheduleService;
        _logger = logger;
    }

    public static bool IsRunning => Volatile.Read(ref _gate) == 1;

    public static bool IsBusy()
    {
        if (IsRunning)
        {
            return true;
        }

        try
        {
            var monitor = JobStorage.Current.GetMonitoringApi();
            if (monitor.ProcessingJobs(0, 200).Any(job => IsThisJob(job.Value?.Job)))
            {
                return true;
            }

            return monitor.EnqueuedJobs("system", 0, 200).Any(job => IsThisJob(job.Value?.Job));
        }
        catch (Exception)
        {
            return IsRunning;
        }
    }

    public static bool TryEnter() => Interlocked.CompareExchange(ref _gate, 1, 0) == 0;

    public static void Exit() => Interlocked.Exchange(ref _gate, 0);

    private static bool IsThisJob(Hangfire.Common.Job? job) =>
        job?.Type == typeof(PlexLibraryRefreshJob);

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 6 * 60 * 60)]
    [Queue("system")]
    public Task ExecuteScheduled() => Run(() => _refresh.RefreshScheduledAsync(CancellationToken.None));

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 6 * 60 * 60)]
    [Queue("system")]
    public Task ExecuteSection(string sectionKey) =>
        Run(() => _refresh.RefreshLibraryAsync(sectionKey, CancellationToken.None));

    private async Task Run(Func<Task> action)
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        if (!TryEnter())
        {
            _logger.LogInformation("Plex library refresh is already running.");
            return;
        }

        try
        {
            await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());
            await action();
            await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Plex library refresh failed.");
            await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
            throw;
        }
        finally
        {
            Exit();
        }
    }
}
