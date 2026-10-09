using DeepL;
using Hangfire;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Translation;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Configuration;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.TranslationRequests;
using Lingarr.Server.Services.Plugins;
using Lingarr.Server.Services.Subtitle;
using Lingarr.Server.Services.Translation;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class TranslationRequestService : ITranslationRequestService
{
    private readonly LingarrDbContext _dbContext;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IHubContext<TranslationRequestsHub> _hubContext;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly IProgressService _progressService;
    private readonly IStatisticsService _statisticsService;
    private readonly IMediaService _mediaService;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly ITranslationRequestEventService _eventService;
    private readonly IProviderHealthService _providerHealth;
    private readonly ITranslationQualityService _translationQuality;
    private readonly ITranslationPromptProfileService _promptProfiles;
    private readonly IClassifierSubtitleGate _classifier;
    private readonly PluginShelf _shelf;
    private readonly ILogger<TranslationRequestService> _logger;
    private static readonly ConcurrentDictionary<int, CancellationTokenSource> _asyncTranslationJobs = new();

    public TranslationRequestService(
        LingarrDbContext dbContext,
        IBackgroundJobClient backgroundJobClient,
        IHubContext<TranslationRequestsHub> hubContext,
        ITranslationServiceFactory translationServiceFactory,
        IProgressService progressService,
        IStatisticsService statisticsService,
        IMediaService mediaService,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ITranslationRequestEventService eventService,
        IProviderHealthService providerHealth,
        ITranslationQualityService translationQuality,
        ITranslationPromptProfileService promptProfiles,
        IClassifierSubtitleGate classifier,
        PluginShelf shelf,
        ILogger<TranslationRequestService> logger)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
        _backgroundJobClient = backgroundJobClient;
        _translationServiceFactory = translationServiceFactory;
        _progressService = progressService;
        _statisticsService = statisticsService;
        _mediaService = mediaService;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _eventService = eventService;
        _providerHealth = providerHealth;
        _translationQuality = translationQuality;
        _promptProfiles = promptProfiles;
        _classifier = classifier;
        _shelf = shelf;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TranslationRequestDetail?> GetTranslationRequest(int id)
    {
        var request = await _dbContext.TranslationRequests.FindAsync(id);
        if (request == null)
        {
            return null;
        }

        var events = await _eventService.GetEvents(id);

        var translationRequestLines = await _dbContext.TranslationRequestLines
            .Where(translationRequestLine => translationRequestLine.TranslationRequestId == id)
            .OrderBy(translationRequestLine => translationRequestLine.Position)
            .Select(translationRequestLine => new TranslationRequestSubtitleLines
            {
                Position = translationRequestLine.Position,
                Source = translationRequestLine.Source,
                Target = translationRequestLine.Target,
                Service = translationRequestLine.Service
            })
            .ToListAsync();
        
        return new TranslationRequestDetail
        {
            Id = request.Id,
            JobId = request.JobId,
            MediaId = request.MediaId,
            Title = request.Title,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            SubtitleToTranslate = request.SubtitleToTranslate,
            TranslatedSubtitle = request.TranslatedSubtitle,
            MediaType = request.MediaType,
            Status = request.Status,
            CompletedAt = request.CompletedAt,
            ErrorMessage = request.ErrorMessage,
            StackTrace = request.StackTrace,
            QualityScore = request.QualityScore,
            QualityGrade = request.QualityGrade,
            QualityStatus = request.QualityStatus,
            Progress = request.Status == TranslationStatus.Completed ? 100 : 0,
            ProviderCancelAttempts = request.ProviderCancelAttempts,
            ProviderCancelRetryMax = request.ProviderCancelRetryMax,
            CreatedAt = request.CreatedAt,
            UpdatedAt = request.UpdatedAt,
            Events = events.Select(translationRequestEvent => new TranslationRequestEventDetail
            {
                Id = translationRequestEvent.Id,
                Status = translationRequestEvent.Status,
                Message = translationRequestEvent.Message,
                CreatedAt = translationRequestEvent.CreatedAt
            }).ToList(),
            Lines = translationRequestLines.Count > 0 ? translationRequestLines : []
        };
    }

    /// <inheritdoc />
    public async Task<int> CreateRequest(TranslateAbleSubtitle translateAbleSubtitle)
    {
        var continued = await TryContinueCancelled(translateAbleSubtitle);
        if (continued != null)
        {
            return continued.Value;
        }

        var mediaTitle = await FormatMediaTitle(translateAbleSubtitle.MediaId, translateAbleSubtitle.MediaType);
        var translationRequest = new TranslationRequest
        {
            MediaId = translateAbleSubtitle.MediaId,
            Title = mediaTitle,
            SourceLanguage = translateAbleSubtitle.SourceLanguage,
            TargetLanguage = translateAbleSubtitle.TargetLanguage,
            SubtitleToTranslate = translateAbleSubtitle.SubtitlePath,
            MediaType = translateAbleSubtitle.MediaType,
            Status = TranslationStatus.Pending
        };

        return await EnqueueRequest(translationRequest);
    }

    private async Task<int> EnqueueRequest(TranslationRequest translationRequest)
    {
        if (translationRequest.SubtitleToTranslate != null)
        {
            var existing = await _dbContext.TranslationRequests
                .Where(activeRequest =>
                    activeRequest.SubtitleToTranslate == translationRequest.SubtitleToTranslate &&
                    activeRequest.TargetLanguage == translationRequest.TargetLanguage &&
                    new[]
                        {
                            TranslationStatus.Pending, 
                            TranslationStatus.InProgress
                        }.Contains(activeRequest.Status))
                .Select(activeRequest => new { activeRequest.Id })
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                _logger.LogInformation(
                    "Duplicate translation request skipped for '{Subtitle}' -> '{Language}' (existing id {Id}).",
                    translationRequest.SubtitleToTranslate, translationRequest.TargetLanguage, existing.Id);
                return existing.Id;
            }
        }

        // Create a new TranslationRequest to not keep ID and JobID
        var translationRequestCopy = new TranslationRequest
        {
            MediaId = translationRequest.MediaId,
            Title = translationRequest.Title,
            SourceLanguage = translationRequest.SourceLanguage,
            TargetLanguage = translationRequest.TargetLanguage,
            SubtitleToTranslate = translationRequest.SubtitleToTranslate,
            MediaType = translationRequest.MediaType,
            Status = TranslationStatus.Pending
        };

        _dbContext.TranslationRequests.Add(translationRequestCopy);
        await _dbContext.SaveChangesAsync();
        await _eventService.LogEvent(translationRequestCopy.Id, TranslationStatus.Pending);

        var jobId = _backgroundJobClient.Enqueue<TranslationJob>(job =>
            job.Execute(translationRequestCopy, CancellationToken.None)
        );
        await UpdateTranslationRequest(translationRequestCopy, TranslationStatus.Pending, jobId);

        await UpdateActiveCount();

        return translationRequestCopy.Id;
    }

    private async Task<int?> TryContinueCancelled(TranslateAbleSubtitle subtitle)
    {
        if (!await CancelledCacheEnabled() || string.IsNullOrWhiteSpace(subtitle.SubtitlePath))
        {
            return null;
        }

        var cancelled = await _dbContext.TranslationRequests
            .Where(request =>
                request.Status == TranslationStatus.Cancelled &&
                request.MediaId == subtitle.MediaId &&
                request.MediaType == subtitle.MediaType &&
                request.SubtitleToTranslate == subtitle.SubtitlePath &&
                request.SourceLanguage == subtitle.SourceLanguage &&
                request.TargetLanguage == subtitle.TargetLanguage)
            .OrderByDescending(request => request.UpdatedAt)
            .ThenByDescending(request => request.Id)
            .FirstOrDefaultAsync();
        if (cancelled == null)
        {
            return null;
        }

        var lines = await _dbContext.TranslationRequestLines
            .Where(line => line.TranslationRequestId == cancelled.Id)
            .ToListAsync();
        if (lines.Count == 0)
        {
            return null;
        }

        List<SubtitleItem> current;
        try
        {
            current = await _subtitleService.ReadSubtitles(subtitle.SubtitlePath);
        }
        catch (Exception exception)
        {
            _logger.LogInformation(
                exception,
                "Cancelled request {Id} was not continued because the subtitle could not be read.",
                cancelled.Id);
            return null;
        }

        var byPosition = current
            .GroupBy(item => item.Position)
            .ToDictionary(group => group.Key, group => group.First());
        var newest = lines
            .GroupBy(line => line.Position)
            .Select(group => group.OrderByDescending(line => line.Id).First());
        foreach (var line in newest)
        {
            if (!byPosition.TryGetValue(line.Position, out var item) ||
                !CancelledProgressCache.SourceStillMatches(line.Source, item.Lines, item.PlaintextLines))
            {
                _logger.LogInformation(
                    "Cancelled request {Id} was not continued because the source subtitle changed.",
                    cancelled.Id);
                return null;
            }
        }

        var score = _translationQuality.ScorePartial(lines, cancelled.TargetLanguage);
        var threshold = await CancelledQualityThreshold();
        if (!CancelledProgressCache.ShouldContinue(score, threshold))
        {
            _logger.LogInformation(
                "Cancelled request {Id} scored {Score}, below {Threshold}. The next run starts over.",
                cancelled.Id, score, threshold);
            return null;
        }

        _logger.LogInformation(
            "Continuing cancelled request {Id} at quality {Score} (threshold {Threshold}).",
            cancelled.Id, score, threshold);
        await ResumeTranslationRequest(cancelled);
        return cancelled.Id;
    }

    private async Task RememberMissingCompletedQuality(List<TranslationRequest> requests)
    {
        foreach (var request in requests)
        {
            if (request.Status != TranslationStatus.Completed ||
                request.QualityScore != null ||
                request.QualityStatus == "file-missing")
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(request.SubtitleToTranslate) ||
                string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
            {
                request.QualityStatus = "file-missing";
                continue;
            }

            try
            {
                var sourceItems = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate);
                var targetItems = await _subtitleService.ReadSubtitles(request.TranslatedSubtitle);
                var targets = targetItems
                    .GroupBy(item => item.Position)
                    .ToDictionary(group => group.Key, group => group.First());
                var lines = new List<TranslationRequestLine>();
                var id = 1;
                foreach (var source in sourceItems)
                {
                    if (!targets.TryGetValue(source.Position, out var target))
                    {
                        continue;
                    }

                    lines.Add(new TranslationRequestLine
                    {
                        Id = id++,
                        TranslationRequestId = request.Id,
                        Position = source.Position,
                        Source = string.Join(" ", source.PlaintextLines),
                        Target = string.Join(" ", target.PlaintextLines)
                    });
                }

                var score = _translationQuality.ScorePartial(lines, request.TargetLanguage);
                if (score == null)
                {
                    request.QualityStatus = "file-missing";
                    continue;
                }

                request.QualityScore = score;
                request.QualityGrade = TranslationQualityService.GradeFor(score.Value);
                request.QualityStatus = "completed";
            }
            catch (Exception exception)
            {
                _logger.LogInformation(
                    exception,
                    "Could not score completed request {Id} from its subtitle files.",
                    request.Id);
                request.QualityStatus = "file-missing";
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task RememberMissingCancelledProgress(List<TranslationRequest> requests)
    {
        if (!await CancelledCacheEnabled())
        {
            return;
        }

        foreach (var request in requests)
        {
            if (request.Status == TranslationStatus.Cancelled &&
                (request.QualityStatus == null ||
                 (request.CachedProgress == 0 && request.QualityScore >= 90)))
            {
                await RememberCancelledProgress(request);
            }
        }
    }

    private async Task RememberCancelledProgress(TranslationRequest request)
    {
        if (!await CancelledCacheEnabled())
        {
            return;
        }

        var lines = await _dbContext.TranslationRequestLines
            .Where(line => line.TranslationRequestId == request.Id)
            .ToListAsync();
        var newest = lines
            .GroupBy(line => line.Position)
            .Select(group => group.OrderByDescending(line => line.Id).First())
            .ToList();
        var translated = newest.Count(line => !string.IsNullOrWhiteSpace(line.Target));
        var score = _translationQuality.ScorePartial(lines, request.TargetLanguage);
        request.QualityScore = score;
        request.QualityGrade = score.HasValue ? TranslationQualityService.GradeFor(score.Value) : null;
        request.QualityStatus = score.HasValue ? "partial" : "none";

        int? total = null;
        if (!string.IsNullOrWhiteSpace(request.SubtitleToTranslate))
        {
            try
            {
                total = (await _subtitleService.ReadSubtitles(request.SubtitleToTranslate)).Count;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    exception,
                    "Could not count lines for cancelled request {Id}.",
                    request.Id);
            }
        }

        request.CachedProgress = CancelledProgressCache.ProgressPercent(translated, total ?? 0);
        await _dbContext.SaveChangesAsync();
    }

    private async Task<bool> CancelledCacheEnabled()
    {
        var value = await _settingService.GetSetting(SettingKeys.Translation.CacheCancelledProgress);
        return CancelledProgressCache.IsEnabled(value);
    }

    private async Task<int> CancelledQualityThreshold()
    {
        var value = await _settingService.GetSetting(SettingKeys.Translation.CacheCancelledQualityThreshold);
        return CancelledProgressCache.ThresholdOrDefault(value);
    }

    /// <inheritdoc />
    public async Task CreateBulkRequest(BulkTranslateRequest request)
    {
        var sourceLanguages = await _settingService.GetSettingAsJson<SourceLanguage>(
            SettingKeys.Translation.SourceLanguages
            );
        var sourceCodes = sourceLanguages.Select(sourceLanguage => sourceLanguage.Code).ToHashSet();
        var ignoreCaptions = await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions) ?? "false";

        switch (request.MediaType)
        {
            case MediaType.Movie:
                var movies = await _dbContext.Movies
                    .Where(movie => request.MediaIds.Contains(movie.Id))
                    .ToListAsync();

                foreach (var movie in movies)
                {
                    if (movie.Path == null)
                    {
                        _logger.LogInformation("Bulk: skipping movie {Id} — path is null", movie.Id);
                        continue;
                    }
                    await ProcessMediaSubtitles(
                        movie.Path, 
                        movie.FileName, 
                        movie.Id, 
                        MediaType.Movie,
                        request.TargetLanguage, 
                        sourceCodes, 
                        ignoreCaptions);
                }
                break;

            case MediaType.Show:
                var shows = await _dbContext.Shows
                    .Include(show => show.Seasons)
                    .ThenInclude(season => season.Episodes)
                    .Where(show => request.MediaIds.Contains(show.Id))
                    .ToListAsync();

                foreach (var show in shows)
                {
                    foreach (var season in show.Seasons)
                    {
                        if (string.IsNullOrEmpty(season.Path))
                        {
                            continue;
                        }

                        foreach (var episode in season.Episodes)
                        {
                            if (string.IsNullOrEmpty(episode.FileName) || string.IsNullOrEmpty(episode.Path))
                            {
                                continue;
                            }
                            await ProcessMediaSubtitles(
                                season.Path, 
                                episode.FileName, 
                                episode.Id, 
                                MediaType.Episode,
                                request.TargetLanguage,
                                sourceCodes, 
                                ignoreCaptions);
                        }
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Discovers subtitles for a media item, resolves the source subtitle, and creates a translation request.
    /// </summary>
    private async Task ProcessMediaSubtitles(
        string path, 
        string? fileName, 
        int mediaId, 
        MediaType mediaType,
        string targetLanguage, 
        HashSet<string> sourceCodes, 
        string ignoreCaptions)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            _logger.LogDebug("Bulk: skipping mediaId {MediaId} — fileName is empty", mediaId);
            return;
        }

        var subtitles = await _shelf.FilterCaptionsAsync(await _subtitleService.GetSubtitles(path, fileName));
        var selected = _subtitleService.SelectSourceSubtitle(subtitles, sourceCodes, ignoreCaptions);
        if (selected == null)
        {
            _logger.LogDebug("Bulk: skipping mediaId {MediaId} — no source subtitle found (sourceCodes: {SourceCodes}, available: {Available})",
                mediaId, string.Join(", ", sourceCodes),
                string.Join(", ", subtitles.Select(s => s.Language)));
            return;
        }

        if (selected.AvailableLanguages.Contains(targetLanguage.ToLowerInvariant()))
        {
            _logger.LogDebug("Bulk: skipping mediaId {MediaId} — target language {Target} already exists",
                mediaId, targetLanguage);
            return;
        }

        await CreateRequest(new TranslateAbleSubtitle
        {
            MediaId = mediaId,
            MediaType = mediaType,
            SubtitlePath = selected.Subtitle.Path,
            TargetLanguage = targetLanguage,
            SourceLanguage = selected.SourceLanguage,
            SubtitleFormat = selected.Subtitle.Format
        });
    }

    /// <inheritdoc />
    public Task<List<ActiveTranslation>> GetActiveTranslations()
    {
        return _dbContext.TranslationRequests
            .Where(translationRequest =>
                translationRequest.Status == TranslationStatus.Pending ||
                translationRequest.Status == TranslationStatus.InProgress)
            .Select(translationRequest => new ActiveTranslation
            {
                MediaId = translationRequest.MediaId,
                MediaType = translationRequest.MediaType,
                Status = translationRequest.Status
            })
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<ActiveTranslation>> UpdateActiveCount()
    {
        var activeTranslations = await GetActiveTranslations();
        await _hubContext.Clients.Group("TranslationRequests")
            .SendAsync("ActiveTranslations", activeTranslations);

        return activeTranslations;
    }
    
    /// <inheritdoc />
    public async Task<string?> CancelTranslationRequest(TranslationRequest cancelRequest)
    {
        var translationRequest = await _dbContext.TranslationRequests.FirstOrDefaultAsync(
            translationRequest => translationRequest.Id == cancelRequest.Id);
        if (translationRequest == null)
        {
            return null;
        }

        if (translationRequest.JobId != null)
        {
            _backgroundJobClient.Delete(translationRequest.JobId);
        }
        else if (_asyncTranslationJobs.TryGetValue(translationRequest.Id, out var cts))
        {
            // Maybe an async translation job
            await cts.CancelAsync();
        }

        translationRequest.CompletedAt = DateTime.UtcNow;
        translationRequest.Status = TranslationStatus.Cancelled;
        translationRequest.ErrorMessage = "Translation was cancelled";
        translationRequest.ProviderCancelAttempts = 0;
        translationRequest.ProviderCancelRetryMax = 0;
        translationRequest.ProviderCancelRetryPending = false;
        await _dbContext.SaveChangesAsync();
        await RememberCancelledProgress(translationRequest);
        await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Cancelled, "Translation was cancelled");
        await UpdateActiveCount();
        await _progressService.Emit(translationRequest, translationRequest.CachedProgress ?? 0);

        return $"Translation request with id {cancelRequest.Id} has been cancelled";
    }
    
    /// <inheritdoc />
    public async Task<string?> RemoveTranslationRequest(TranslationRequest cancelRequest)
    {
        var translationRequest = await _dbContext.TranslationRequests.FirstOrDefaultAsync(
            translationRequest => translationRequest.Id == cancelRequest.Id);
        if (translationRequest == null)
        {
            return null;
        }
        
        _dbContext.TranslationRequests.Remove(translationRequest);
        await _dbContext.SaveChangesAsync();
        
        return $"Translation request with id {cancelRequest.Id} has been removed";
    }

    /// <inheritdoc />
    public async Task<string?> RetryTranslationRequest(TranslationRequest retryRequest)
    {
        var translationRequest = await _dbContext.TranslationRequests.FirstOrDefaultAsync(
            translationRequest => translationRequest.Id == retryRequest.Id);
        if (translationRequest == null)
        {
            return null;
        }


        var newTranslationRequestId = await EnqueueRequest(translationRequest);
        return $"Translation request with id {retryRequest.Id} has been restarted, new job id {newTranslationRequestId}";
    }

    /// <inheritdoc />
    public async Task<string?> ResumeTranslationRequest(TranslationRequest resumeRequest)
    {
        var translationRequest = await _dbContext.TranslationRequests.FirstOrDefaultAsync(
            tr => tr.Id == resumeRequest.Id);
        if (translationRequest == null)
        {
            return null;
        }

        var resumable = new[]
        {
            TranslationStatus.Failed,
            TranslationStatus.Cancelled,
            TranslationStatus.Interrupted
        };
        if (!resumable.Contains(translationRequest.Status))
        {
            _logger.LogInformation(
                "Resume skipped for request {Id}: status {Status} is not resumable.",
                translationRequest.Id, translationRequest.Status);
            return null;
        }

        translationRequest.Status = TranslationStatus.Pending;
        translationRequest.ErrorMessage = null;
        translationRequest.StackTrace = null;
        translationRequest.CompletedAt = null;
        await _dbContext.SaveChangesAsync();
        await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Pending, "Resumed");

        var jobId = _backgroundJobClient.Enqueue<TranslationJob>(job =>
            job.Execute(translationRequest, CancellationToken.None));
        await UpdateTranslationRequest(translationRequest, TranslationStatus.Pending, jobId);
        await UpdateActiveCount();

        return $"Translation request with id {resumeRequest.Id} has been resumed, new job id {jobId}";
    }

    /// <inheritdoc />
    public async Task<string?> ProofreadTranslationRequest(TranslationRequest proofreadRequest)
    {
        var translationRequest = await _dbContext.TranslationRequests.FirstOrDefaultAsync(
            request => request.Id == proofreadRequest.Id);
        if (translationRequest == null)
        {
            return null;
        }

        if (translationRequest.Status != TranslationStatus.Completed
            || string.IsNullOrEmpty(translationRequest.TranslatedSubtitle)
            || string.IsNullOrEmpty(translationRequest.SubtitleToTranslate))
        {
            _logger.LogInformation(
                "AI revision skipped for request {Id}: status {Status} or missing subtitle paths.",
                translationRequest.Id, translationRequest.Status);
            return null;
        }

        translationRequest.Status = TranslationStatus.Pending;
        translationRequest.ErrorMessage = null;
        translationRequest.StackTrace = null;
        await _dbContext.SaveChangesAsync();
        await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Pending, "AI revision queued");

        var jobId = _backgroundJobClient.Enqueue<ProofreadJob>(job =>
            job.Execute(translationRequest, CancellationToken.None));
        await UpdateTranslationRequest(translationRequest, TranslationStatus.Pending, jobId);
        await UpdateActiveCount();

        return $"AI revision queued for request {translationRequest.Id}";
    }
    
    /// <inheritdoc />
    public async Task<TranslationRequest> UpdateTranslationRequest(TranslationRequest translationRequest,
        TranslationStatus status, string? jobId = null)
    {
        var request = await _dbContext.TranslationRequests.FindAsync(translationRequest.Id);
        if (request == null)
        {
            throw new NotFoundException($"TranslationRequest with ID {translationRequest.Id} not found.");
        }

        if (jobId != null)
        {
            request.JobId = jobId;
        }
        request.Status = status;
        await _dbContext.SaveChangesAsync();

        return request;
    }
    
    /// <inheritdoc />
    public async Task ResumeTranslationRequests()
    {
        var requests = await _dbContext.TranslationRequests
            .Where(tr => tr.Status == TranslationStatus.Pending || 
                         tr.Status == TranslationStatus.InProgress)
            .ToListAsync();

        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        var queuedJobIds = monitoringApi.EnqueuedJobs("translation", 0, int.MaxValue)
            .Select(queuedJob => new
            {
                JobId = queuedJob.Key,
                Request = queuedJob.Value?.Job?.Args?.FirstOrDefault() as TranslationRequest
            })
            .Where(queuedJob => queuedJob.Request != null)
            .ToLookup(queuedJob => queuedJob.Request!.Id, queuedJob => queuedJob.JobId);

        foreach (var request in requests)
        {
            if (request.JobId == null)
            {
                // Async translation job. Set as Interrupted and don't run
                // Those cannot be resumed
                await UpdateTranslationRequest(request, TranslationStatus.Interrupted);
                await _eventService.LogEvent(request.Id, TranslationStatus.Interrupted);
                continue;
            }

            foreach (var queuedJobId in queuedJobIds[request.Id])
            {
                BackgroundJob.Delete(queuedJobId);
                _logger.LogWarning(
                    "Removed queued translation job {JobId} for request {RequestId} before resuming.",
                    queuedJobId, request.Id);
            }

            var jobId = _backgroundJobClient.Enqueue<TranslationJob>(job =>
                job.Execute(request, CancellationToken.None)
            );
            await UpdateTranslationRequest(request, TranslationStatus.Pending, jobId);
        }
    }
    
    /// <inheritdoc />
    public async Task<PagedResult<TranslationRequest>> GetTranslationRequests(
        string? searchQuery,
        string? orderBy,
        bool ascending,
        int pageNumber,
        int pageSize)
    {
        var query = _dbContext.TranslationRequests
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrEmpty(searchQuery))
        {
            query = query.Where(translationRequest => translationRequest.Title.ToLower().Contains(searchQuery.ToLower()));
        }
    
        query = orderBy switch
        {
            "Title" => ascending 
                ? query.OrderBy(m => m.Title) 
                : query.OrderByDescending(m => m.Title),
            "CreatedAt" => ascending
                ? query.OrderByDescending(tr => tr.CreatedAt)
                : query.OrderBy(tr => tr.CreatedAt),
            "CompletedAt" => ascending
                ? query.OrderByDescending(tr => tr.CompletedAt)
                : query.OrderBy(tr => tr.CompletedAt),
            _ => ascending
                ? query.OrderByDescending(tr => tr.CreatedAt)
                : query.OrderBy(tr => tr.CreatedAt)
        };
        
        var totalCount = await query.CountAsync();
        var requests = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        await AttachTokenUsage(requests);
        await RememberMissingCancelledProgress(requests);
        await RememberMissingCompletedQuality(requests);
        foreach (var request in requests)
        {
            request.Badges = (await _shelf.LabelsAsync(
                request.TranslatedSubtitle ?? request.SubtitleToTranslate)).ToList();
        }

        return new PagedResult<TranslationRequest>
        {
            Items = requests,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }
    
    private async Task AttachTokenUsage(List<TranslationRequest> requests)
    {
        if (requests.Count == 0)
        {
            return;
        }

        var ids = requests.Select(request => request.Id).ToList();
        var usage = await _dbContext.ProviderOperationalEvents
            .Where(item =>
                item.TranslationRequestId != null &&
                ids.Contains(item.TranslationRequestId.Value) &&
                item.Outcome == "success")
            .Select(item => new
            {
                item.TranslationRequestId,
                item.Provider,
                item.InputTokens,
                item.OutputTokens
            })
            .ToListAsync();

        foreach (var request in requests)
        {
            var metered = usage
                .Where(item =>
                    item.TranslationRequestId == request.Id &&
                    PayPerTokenProviders.Contains(item.Provider) &&
                    (item.InputTokens.HasValue || item.OutputTokens.HasValue))
                .ToList();
            if (metered.Count == 0)
            {
                continue;
            }

            request.ShowTokenUsage = true;
            request.InputTokens = metered.Sum(item => item.InputTokens ?? 0);
            request.OutputTokens = metered.Sum(item => item.OutputTokens ?? 0);
        }
    }

    /// <inheritdoc />
    public async Task ClearMediaHash(TranslationRequest translationRequest)
    {
        if (translationRequest.MediaId.HasValue)
        {
            switch (translationRequest.MediaType)
            {
                case MediaType.Movie:
                    var movie = await _dbContext.Movies.FirstOrDefaultAsync(m => m.Id == translationRequest.MediaId.Value);
                    if (movie != null)
                    {
                        movie.MediaHash = string.Empty;
                    }
                    break;
                
                case MediaType.Episode:
                    var episode = await _dbContext.Episodes.FirstOrDefaultAsync(e => e.Id == translationRequest.MediaId.Value);
                    if (episode != null)
                    {
                        episode.MediaHash = string.Empty;
                    }
                    break;
            }
            await _dbContext.SaveChangesAsync();
        }
    }

    /// <inheritdoc />
    public async Task<BatchTranslatedLine[]> TranslateContentAsync(
        TranslateAbleSubtitleContent translateAbleContent,
        CancellationToken parentCancellationToken)
    {
        // Resolve internal media id from the client's ArrMediaId
        var mediaId = await GetMediaId(translateAbleContent.ArrMediaId, translateAbleContent.MediaType);

        // Derive canonical title from the media lookup (FormatMediaTitle queries the DB for
        // the resolved movie/episode title). The client's Title becomes an optional override.
        var canonicalTitle = await FormatMediaTitle(mediaId, translateAbleContent.MediaType);
        var contentTitle = !string.IsNullOrWhiteSpace(translateAbleContent.Title?.Trim())
            ? translateAbleContent.Title.Trim()
            : canonicalTitle;

        var translationRequest = new TranslationRequest
        {
            MediaId = mediaId,
            Title = contentTitle,
            SourceLanguage = translateAbleContent.SourceLanguage,
            TargetLanguage = translateAbleContent.TargetLanguage,
            SubtitleToTranslate = translateAbleContent.SourceSubtitlePath,
            TranslatedSubtitle = translateAbleContent.TranslatedSubtitlePath,
            MediaType = translateAbleContent.MediaType,
            Status = TranslationStatus.InProgress
        };

        // Link cancel token with new source to be able to cancel the async translation
        var asyncTranslationCancellationTokenSource = new CancellationTokenSource();
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken, asyncTranslationCancellationTokenSource.Token);
        var cancellationToken = cancellationTokenSource.Token;

        try
        {
            BatchTranslatedLine[]? results;
            // Get Translation Settings
            var settings = await _settingService.GetSettings([
                SettingKeys.Translation.UseBatchTranslation,
                SettingKeys.Translation.ServiceType,
                SettingKeys.Translation.MaxBatchSize,
                SettingKeys.Translation.StripSubtitleFormatting,
                SettingKeys.Translation.StripSubtitleHtml,
                SettingKeys.Translation.PreserveLineBreaks
            ]);
            var preserveLineBreaks = settings[SettingKeys.Translation.PreserveLineBreaks] == "true";
            var chain = TranslationChain.Parse(settings[SettingKeys.Translation.ServiceType], _logger);
            await _promptProfiles.ResolveChainAsync(chain, cancellationToken: cancellationToken);
            TranslationChain.StampLanguages(
                chain,
                translateAbleContent.SourceLanguage,
                translateAbleContent.TargetLanguage);
            var services = _translationServiceFactory.CreateTranslationServices(chain);
            var serviceType = services.Count > 0 ? services[0].Name : "unknown";
            if (services.Count == 0)
            {
                throw new TranslationException(
                    $"No usable translation services configured: [{string.Join(", ", chain.Select(e => e.ProviderNormalized))}]");
            }
            var translationService = services[0].Service;

            // Skip if an active content-translation row for this media+target already exists.
            var existingId = await _dbContext.TranslationRequests
                .Where(r => r.MediaId == translationRequest.MediaId
                         && r.MediaType == translationRequest.MediaType
                         && r.Title == translationRequest.Title
                         && r.SourceLanguage == translationRequest.SourceLanguage
                         && r.TargetLanguage == translationRequest.TargetLanguage
                         && (r.Status == TranslationStatus.Pending || r.Status == TranslationStatus.InProgress))
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingId != null)
            {
                _logger.LogInformation(
                    "Duplicate content-translation request skipped for mediaId {MediaId} ({MediaType}) '{Title}' {Src}->{Tgt} (existing id {Id}).",
                    translationRequest.MediaId, translationRequest.MediaType,
                    translationRequest.Title,
                    translationRequest.SourceLanguage, translationRequest.TargetLanguage, existingId.Value);
                return Array.Empty<BatchTranslatedLine>();
            }

            // Add TranslationRequest
            _dbContext.TranslationRequests.Add(translationRequest);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _promptProfiles.ResolveChainAsync(
                chain, translationRequest.Id, cancellationToken);
            await _eventService.LogEvent(translationRequest.Id, TranslationStatus.InProgress);
            await UpdateActiveCount();

            // Add translation as a async translation request with cancellation source
            _asyncTranslationJobs.TryAdd(translationRequest.Id, cancellationTokenSource);


            // Process Translation
            var stripSubtitleFormatting = settings[SettingKeys.Translation.StripSubtitleFormatting] == "true";
            var stripSubtitleHtml = SubtitleHtml.Enabled(
                settings.GetValueOrDefault(SettingKeys.Translation.StripSubtitleHtml));
            if (settings[SettingKeys.Translation.UseBatchTranslation] == "true"
                && translateAbleContent.Lines.Count > 1
                && services.Any(e => e.Service is IBatchTranslationService))
            {
                _logger.LogInformation("Processing batch translation request with {lineCount} lines from {sourceLanguage} to {targetLanguage}",
                    translateAbleContent.Lines.Count, translateAbleContent.SourceLanguage, translateAbleContent.TargetLanguage);

                var subtitleTranslator = new SubtitleTranslationService(
                    services,
                    _logger,
                    _progressService,
                    _providerHealth,
                    _classifier);
                var totalSize = translateAbleContent.Lines.Count;
                var maxSize = int.TryParse(settings[SettingKeys.Translation.MaxBatchSize], out var batchSize)
                    ? batchSize
                    : 10000;

                _logger.LogDebug("Batch translation configuration: maxSize={maxSize}, stripFormatting={stripFormatting}, totalLines={totalLines}",
                    maxSize, stripSubtitleFormatting, totalSize);

                var subtitleItems = translateAbleContent.Lines.Select(item => new SubtitleItem
                {
                    Position = item.Position,
                    Lines = new List<string> { stripSubtitleHtml ? SubtitleHtml.Strip(item.Line) : item.Line },
                    PlaintextLines = new List<string> { stripSubtitleHtml ? SubtitleHtml.Strip(item.Line) : item.Line }
                }).ToList();

                await subtitleTranslator.TranslateSubtitlesBatch(
                    subtitleItems,
                    translationRequest,
                    stripSubtitleFormatting,
                    preserveLineBreaks,
                    maxSize,
                    cancellationToken);
                if (stripSubtitleHtml)
                {
                    foreach (var subtitle in subtitleItems)
                    {
                        SubtitleHtml.StripTranslated(subtitle);
                    }
                }

                results = subtitleItems.Select(subtitle => new BatchTranslatedLine
                {
                    Position = subtitle.Position,
                    Line = string.Join(" ", subtitle.TranslatedLines)
                }).ToArray();

                _logger.LogInformation("Batch translation completed successfully. Processed {resultCount} translated lines", results.Length);

                await HandleAsyncTranslationCompletion(translationRequest, serviceType, translationService, results, cancellationToken);
                return results;
            }
            else
            {
                _logger.LogInformation("Using individual line translation for {lineCount} lines from {sourceLanguage} to {targetLanguage}",
                    translateAbleContent.Lines.Count,
                    translateAbleContent.SourceLanguage,
                    translateAbleContent.TargetLanguage);

                var subtitleTranslator = new SubtitleTranslationService(
                    services,
                    _logger,
                    providerHealth: _providerHealth,
                    classifierGate: _classifier);
                var tempResults = new List<BatchTranslatedLine>();

                var iteration = 1;
                var total = translateAbleContent.Lines.Count();
                foreach (var item in translateAbleContent.Lines)
                {
                    var sourceLine = stripSubtitleHtml ? SubtitleHtml.Strip(item.Line) : item.Line;
                    var translateLine = new TranslateAbleSubtitleLine
                    {
                        SubtitleLine = sourceLine,
                        SourceLanguage = translateAbleContent.SourceLanguage,
                        TargetLanguage = translateAbleContent.TargetLanguage
                    };

                    var translatedText = "";
                    string? serviceUsed = null;
                    LanguagePair? pairUsed = null;
                    if (!string.IsNullOrWhiteSpace(translateLine.SubtitleLine))
                    {
                        var result = await subtitleTranslator.TranslateSubtitleLine(
                            translateLine,
                            cancellationToken,
                            translationRequest.Id);
                        translatedText = result.Translation;
                        serviceUsed = result.Service;
                        pairUsed = result.Pair;
                        if (stripSubtitleFormatting)
                        {
                            translatedText = SubtitleFormatterService.RemoveMarkup(translatedText);
                        }

                        if (stripSubtitleHtml)
                        {
                            translatedText = SubtitleHtml.Strip(translatedText);
                        }
                    }

                    tempResults.Add(new BatchTranslatedLine
                    {
                        Position = item.Position,
                        Line = translatedText
                    });

                    await _progressService.EmitLine(translationRequest, item.Position, sourceLine, translatedText, serviceUsed, pairUsed);

                    var progress = (int)Math.Round((double)iteration * 100 / total);
                    await _progressService.Emit(translationRequest, progress);
                    iteration++;
                }

                _logger.LogInformation("Individual line translation completed. Processed {resultCount} lines", tempResults.Count);
                results = tempResults.ToArray();

                await HandleAsyncTranslationCompletion(translationRequest, serviceType, translationService, results, cancellationToken);
                return results;
            }
        }
        catch (OperationCanceledException ex)
        {
            // ExecuteUpdateAsync bypasses change-tracking so a concurrent write cannot abort this save.
            var now = DateTime.UtcNow;
            await _dbContext.TranslationRequests
                .Where(r => r.Id == translationRequest.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, TranslationStatus.Cancelled)
                    .SetProperty(r => r.ErrorMessage, ex.Message)
                    .SetProperty(r => r.CompletedAt, (DateTime?)now)
                    .SetProperty(r => r.UpdatedAt, now));
            translationRequest.CompletedAt = now;
            translationRequest.Status = TranslationStatus.Cancelled;
            translationRequest.ErrorMessage = ex.Message;
            await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Cancelled, ex.Message);
            await UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating subtitle content");
            var now = DateTime.UtcNow;
            await _dbContext.TranslationRequests
                .Where(r => r.Id == translationRequest.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, TranslationStatus.Failed)
                    .SetProperty(r => r.ErrorMessage, ex.Message)
                    .SetProperty(r => r.StackTrace, ex.ToString())
                    .SetProperty(r => r.CompletedAt, (DateTime?)now)
                    .SetProperty(r => r.UpdatedAt, now));
            translationRequest.CompletedAt = now;
            translationRequest.Status = TranslationStatus.Failed;
            translationRequest.ErrorMessage = ex.Message;
            translationRequest.StackTrace = ex.ToString();
            await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Failed, ex.Message);
            await UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            throw;
        }
        finally
        {
            // Remove async translation from async translation jobs
            _asyncTranslationJobs.TryRemove(translationRequest.Id, out _);
        }
    }

    /// <summary>
    /// Get the Lingarr's media id for the Episode or the Show
    /// </summary>
    private async Task<int> GetMediaId(int arrMediaId, MediaType mediaType)
    {
        switch (mediaType)
        {
            case MediaType.Episode:
                return await _mediaService.GetEpisodeIdOrSyncFromSonarrEpisodeId(arrMediaId);
            case MediaType.Movie:
                return await _mediaService.GetMovieIdOrSyncFromRadarrMovieId(arrMediaId);
            default:
                _logger.LogWarning("Unsupported media type: {MediaType} for translate content async", mediaType);
                return 0;
        }
    }

    /// <summary>
    /// Handles a successful async translation job
    /// </summary>
    private async Task HandleAsyncTranslationCompletion(
        TranslationRequest translationRequest,
        string serviceType,
        ITranslationService translationService,
        BatchTranslatedLine[] results,
        CancellationToken cancellationToken)
    {
        try
        {
            await _translationQuality.EvaluateAsync(translationRequest.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Subtitle quality evaluation failed for translation request {RequestId}; the translation remains completed.",
                translationRequest.Id);
        }
        await _statisticsService.UpdateTranslationStatisticsFromLines(
            translationRequest, serviceType, translationService.ModelName, results);

        var now = DateTime.UtcNow;
        await _dbContext.TranslationRequests
            .Where(r => r.Id == translationRequest.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, TranslationStatus.Completed)
                .SetProperty(r => r.CompletedAt, (DateTime?)now)
                .SetProperty(r => r.UpdatedAt, now), cancellationToken);
        translationRequest.CompletedAt = now;
        translationRequest.Status = TranslationStatus.Completed;
        await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Completed);
        await UpdateActiveCount();
        await _progressService.Emit(translationRequest, 100); // Tells the frontend to update translation request to a finished state
    }

    /// <summary>
    /// Formats the media title based on the media type and ID.
    /// </summary>
    /// <param name="mediaId">The internal media ID (Movie or Episode primary key)</param>
    /// <param name="mediaType">The type of media (Movie or Episode)</param>
    private async Task<string> FormatMediaTitle(int mediaId, MediaType mediaType)
    {
        switch (mediaType)
        {
            case MediaType.Movie:
                var movie = await _dbContext.Movies
                    .FirstOrDefaultAsync(m => m.Id == mediaId);
                return movie?.Title ?? "Unknown Movie";

            case MediaType.Episode:
                var episode = await _dbContext.Episodes
                    .Include(e => e.Season)
                    .ThenInclude(s => s.Show)
                    .FirstOrDefaultAsync(e => e.Id == mediaId);

                if (episode == null)
                    return "Unknown Episode";

                // Format: "Show Title - S01E02 - Episode Title"
                return $"{episode.Season.Show.Title} - " +
                       $"S{episode.Season.SeasonNumber:D2}E{episode.EpisodeNumber:D2} - " +
                       $"{episode.Title}";

            default:
                throw new ArgumentException($"Unsupported media type: {mediaType}");
        }
    }
}
