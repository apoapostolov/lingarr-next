using System.Diagnostics;
using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

public class SubtitleMaintenanceJob
{
    private static readonly string[] VideoExtensions = [".mkv", ".mp4", ".m4v", ".avi", ".ts"];
    private static readonly string[] SidecarExtensions = [".srt", ".ass", ".ssa"];

    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settings;
    private readonly ISubtitleService _subtitleService;
    private readonly IScheduleService _scheduleService;
    private readonly MediaLibraryRefreshService _refreshService;
    private readonly ILogger<SubtitleMaintenanceJob> _logger;

    public SubtitleMaintenanceJob(
        LingarrDbContext dbContext,
        ISettingService settings,
        ISubtitleService subtitleService,
        IScheduleService scheduleService,
        MediaLibraryRefreshService refreshService,
        ILogger<SubtitleMaintenanceJob> logger)
    {
        _dbContext = dbContext;
        _settings = settings;
        _subtitleService = subtitleService;
        _scheduleService = scheduleService;
        _refreshService = refreshService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 3 * 60 * 60)]
    [Queue("system")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());

        var settings = await _settings.GetSettings([
            SettingKeys.Automation.SubtitleNamingEnabled,
            SettingKeys.Automation.SubtitleExtractEnabled,
            SettingKeys.Automation.SubtitleExtractMaxPerRun,
            SettingKeys.Automation.LibraryDiskScanEnabled,
            SettingKeys.Translation.SourceLanguages
        ]);

        if (settings.GetValueOrDefault(SettingKeys.Automation.LibraryDiskScanEnabled) == "false")
        {
            _logger.LogInformation("Library disk scan is paused. Subtitle maintenance did not read folders.");
            await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
            return;
        }

        var namingEnabled = settings.GetValueOrDefault(SettingKeys.Automation.SubtitleNamingEnabled, "true") == "true";
        var extractEnabled = settings.GetValueOrDefault(SettingKeys.Automation.SubtitleExtractEnabled, "false") == "true";
        if (!int.TryParse(settings.GetValueOrDefault(SettingKeys.Automation.SubtitleExtractMaxPerRun), out var maxExtracts)
            || maxExtracts <= 0)
        {
            maxExtracts = 80;
        }

        var touchedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renamed = 0;
        var extracted = 0;

        if (namingEnabled)
        {
            renamed += await NormalizeLibraryAsync("bg", touchedFolders, CancellationToken.None);
            renamed += await NormalizeLibraryAsync("en", touchedFolders, CancellationToken.None);
        }

        if (extractEnabled)
        {
            extracted = await ExtractMissingEnglishAsync(maxExtracts, touchedFolders, CancellationToken.None);
        }

        if (touchedFolders.Count > 0)
        {
            await _refreshService.RefreshFoldersAsync(touchedFolders, CancellationToken.None);
        }

        _logger.LogInformation(
            "Subtitle maintenance finished. Renamed {Renamed}, extracted {Extracted}, folders {Folders}",
            renamed,
            extracted,
            touchedFolders.Count);
        await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
    }

    private async Task<int> NormalizeLibraryAsync(
        string language,
        HashSet<string> touchedFolders,
        CancellationToken cancellationToken)
    {
        var renamed = 0;
        var movies = await _dbContext.Movies.AsTracking().ToListAsync(cancellationToken);
        foreach (var movie in movies)
        {
            if (await TryNormalizeAsync(movie.Path, movie.FileName, language, movie, touchedFolders, cancellationToken))
            {
                renamed++;
            }
        }

        var episodes = await _dbContext.Episodes.AsTracking().ToListAsync(cancellationToken);
        foreach (var episode in episodes)
        {
            if (await TryNormalizeAsync(episode.Path, episode.FileName, language, episode, touchedFolders, cancellationToken))
            {
                renamed++;
            }
        }

        if (renamed > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return renamed;
    }

    private async Task<bool> TryNormalizeAsync(
        string? directory,
        string? fileName,
        string language,
        IMedia media,
        HashSet<string> touchedFolders,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
        {
            return false;
        }

        var sidecarNames = ListSidecarFileNames(directory);
        if (sidecarNames.Count == 0)
        {
            return false;
        }

        var plan = SubtitleNaming.PlanRename(directory, fileName, sidecarNames, language);
        if (plan == null)
        {
            return false;
        }

        if (File.Exists(plan.DestinationPath))
        {
            _logger.LogInformation(
                "Skip subtitle rename, destination exists: {Destination}",
                plan.DestinationPath);
            return false;
        }

        try
        {
            File.Move(plan.SourcePath, plan.DestinationPath);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Failed to rename {Source} -> {Destination}", plan.SourcePath, plan.DestinationPath);
            return false;
        }

        media.MediaHash = string.Empty;
        touchedFolders.Add(directory);
        _logger.LogInformation("Renamed subtitle {Source} -> {Destination}", plan.SourcePath, plan.DestinationPath);
        return true;
    }

    private async Task<int> ExtractMissingEnglishAsync(
        int maxExtracts,
        HashSet<string> touchedFolders,
        CancellationToken cancellationToken)
    {
        var extracted = 0;
        var ioErrors = 0;
        var picturePolicy = NonTextSubtitlePolicy.From(
            await _settings.GetSettings(NonTextSubtitlePolicy.Keys));
        if (picturePolicy.LastResort)
        {
            picturePolicy = picturePolicy.TextOnly();
        }
        var movies = await _dbContext.Movies.AsTracking().ToListAsync(cancellationToken);
        var episodes = await _dbContext.Episodes.AsTracking().ToListAsync(cancellationToken);

        foreach (var media in movies.Cast<IMedia>().Concat(episodes))
        {
            if (extracted >= maxExtracts || ioErrors >= 3)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var directory = media.Path;
            var fileName = media.FileName;
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
            {
                continue;
            }

            var matching = await _subtitleService.GetSubtitles(directory, fileName);
            if (matching.Any(subtitle => SubtitleNaming.NormalizeLanguage(subtitle.Language) == "en"))
            {
                continue;
            }

            var videoPath = FindVideoFile(directory, fileName);
            if (videoPath == null)
            {
                continue;
            }

            try
            {
                if (!await EmbeddedSubtitleExtractor.TryExtractEnglish(
                        directory,
                        fileName,
                        cancellationToken,
                        picturePolicy))
                {
                    continue;
                }

                media.MediaHash = string.Empty;
                touchedFolders.Add(directory);
                extracted++;
                _logger.LogInformation("Extracted an English subtitle for {File}", fileName);
            }
            catch (IOException exception)
            {
                ioErrors++;
                _logger.LogWarning(exception, "I/O error while extracting from {Directory}", directory);
            }
        }

        if (extracted > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return extracted;
    }

    private static List<string> ListSidecarFileNames(string directory)
    {
        var names = new List<string>();
        try
        {
            foreach (var extension in SidecarExtensions)
            {
                names.AddRange(
                    Directory.EnumerateFiles(directory, $"*{extension}", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))!);
            }
        }
        catch (IOException)
        {
            return [];
        }

        return names;
    }

    private static string? FindVideoFile(string directory, string fileName)
    {
        try
        {
            foreach (var extension in VideoExtensions)
            {
                var exact = Path.Combine(directory, fileName + extension);
                if (File.Exists(exact))
                {
                    return exact;
                }
            }

            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(path =>
                {
                    var stem = Path.GetFileNameWithoutExtension(path);
                    return stem.Equals(fileName, StringComparison.OrdinalIgnoreCase)
                           || stem.StartsWith(fileName + ".", StringComparison.OrdinalIgnoreCase);
                });
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }

            return (-1, await stdout, "timed out");
        }

        return (process.ExitCode, await stdout, await stderr);
    }
}
