using Hangfire;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Plugins;
using Lingarr.Contracts.Translation;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Plugins;
using Lingarr.Server.Services.Subtitle;
using Lingarr.Server.Services.Translation;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;
using SubtitleValidationOptions = Lingarr.Server.Models.SubtitleValidationOptions;

namespace Lingarr.Server.Jobs;

public class TranslationJob
{
    private readonly ILogger<TranslationJob> _logger;
    private readonly ISettingService _settings;
    private readonly LingarrDbContext _dbContext;
    private readonly IProgressService _progressService;
    private readonly ISubtitleService _subtitleService;
    private readonly IScheduleService _scheduleService;
    private readonly IStatisticsService _statisticsService;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly ITranslationRequestService _translationRequestService;
    private readonly ITranslationRequestEventService _eventService;
    private readonly IProviderHealthService _providerHealth;
    private readonly ITranslationQualityService _translationQuality;
    private readonly ITranslationPromptProfileService _promptProfiles;
    private readonly IPlexSubtitleSelector _plexSubtitles;
    private readonly IClassifierSubtitleGate _classifier;
    private readonly SubtitlePostProcessRunner _postProcess;
    private readonly PluginSignals _signals;

    public TranslationJob(
        ILogger<TranslationJob> logger,
        ISettingService settings,
        LingarrDbContext dbContext,
        IProgressService progressService,
        ISubtitleService subtitleService,
        IScheduleService scheduleService,
        IStatisticsService statisticsService,
        ITranslationServiceFactory translationServiceFactory,
        ITranslationRequestService translationRequestService,
        ITranslationRequestEventService eventService,
        IProviderHealthService providerHealth,
        ITranslationQualityService translationQuality,
        ITranslationPromptProfileService promptProfiles,
        IPlexSubtitleSelector plexSubtitles,
        IClassifierSubtitleGate classifier,
        SubtitlePostProcessRunner postProcess,
        PluginSignals signals)
    {
        _logger = logger;
        _settings = settings;
        _dbContext = dbContext;
        _progressService = progressService;
        _subtitleService = subtitleService;
        _scheduleService = scheduleService;
        _statisticsService = statisticsService;
        _translationServiceFactory = translationServiceFactory;
        _translationRequestService = translationRequestService;
        _eventService = eventService;
        _providerHealth = providerHealth;
        _translationQuality = translationQuality;
        _promptProfiles = promptProfiles;
        _plexSubtitles = plexSubtitles;
        _classifier = classifier;
        _postProcess = postProcess;
        _signals = signals;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("translation")]
    public async Task Execute(
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        var jobId = JobContextFilter.GetCurrentJobId();

        try
        {
            await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());
            cancellationToken.ThrowIfCancellationRequested();

            var stored = await _dbContext.TranslationRequests
                .Where(storedRequest => storedRequest.Id == translationRequest.Id)
                .Select(storedRequest => new
                {
                    storedRequest.Status,
                    storedRequest.ProviderCancelRetryPending
                })
                .FirstOrDefaultAsync(cancellationToken);
            if (stored is null || stored.Status == TranslationStatus.Completed)
            {
                _logger.LogInformation(
                    "Skipping translation job for request {RequestId}, it is no longer runnable ({Status}).",
                    translationRequest.Id, stored?.Status);
                return;
            }

            if (stored.Status == TranslationStatus.Cancelled && !stored.ProviderCancelRetryPending)
            {
                _logger.LogInformation(
                    "Skipping translation job for request {RequestId}, it was cancelled.",
                    translationRequest.Id);
                return;
            }

            var request = await _translationRequestService.UpdateTranslationRequest(translationRequest,
                TranslationStatus.InProgress,
                jobId);
            await _eventService.LogEvent(request.Id, TranslationStatus.InProgress);
            await _translationRequestService.UpdateActiveCount();

            _logger.LogInformation("TranslateJob started for subtitle: |Green|{filePath}|/Green|",
                translationRequest.SubtitleToTranslate);
            var settings = await _settings.GetSettings([
                SettingKeys.Translation.ServiceType,
                SettingKeys.Translation.FixOverlappingSubtitles,
                SettingKeys.Translation.StripSubtitleFormatting,
                SettingKeys.Translation.StripSubtitleHtml,
                SettingKeys.Translation.PreserveLineBreaks,
                SettingKeys.Translation.AddTranslatorInfo,

                SettingKeys.SubtitleValidation.ValidateSubtitles,
                SettingKeys.SubtitleValidation.MaxFileSizeBytes,
                SettingKeys.SubtitleValidation.MaxSubtitleLength,
                SettingKeys.SubtitleValidation.MinSubtitleLength,
                SettingKeys.SubtitleValidation.MinDurationMs,
                SettingKeys.SubtitleValidation.MaxDurationSecs,

                SettingKeys.Translation.AiContextPromptEnabled,
                SettingKeys.Translation.AiContextBefore,
                SettingKeys.Translation.AiContextAfter,
                SettingKeys.Translation.UseBatchTranslation,
                SettingKeys.Translation.MaxBatchSize,
                SettingKeys.Translation.RemoveLanguageTag,
                SettingKeys.Translation.UseSubtitleTagging,
                SettingKeys.Translation.SubtitleTag
            ]);
            var chain = TranslationChain.Parse(settings[SettingKeys.Translation.ServiceType], _logger);
            await _promptProfiles.ResolveChainAsync(chain, request.Id, cancellationToken);
            TranslationChain.StampLanguages(chain, request.SourceLanguage, request.TargetLanguage);
            var stripSubtitleFormatting = settings[SettingKeys.Translation.StripSubtitleFormatting] == "true";
            var stripSubtitleHtml = SubtitleHtml.Enabled(settings.GetValueOrDefault(SettingKeys.Translation.StripSubtitleHtml));
            var preserveLineBreaks = settings[SettingKeys.Translation.PreserveLineBreaks] == "true";
            var addTranslatorInfo = settings[SettingKeys.Translation.AddTranslatorInfo] == "true";
            var validateSubtitles = settings[SettingKeys.SubtitleValidation.ValidateSubtitles] != "false";
            var removeLanguageTag = settings[SettingKeys.Translation.RemoveLanguageTag] != "false";
            var contextPromptEnabled = settings[SettingKeys.Translation.AiContextPromptEnabled] == "true";

            var contextBefore = 0;
            var contextAfter = 0;
            if (contextPromptEnabled)
            {
                contextBefore = int.TryParse(settings[SettingKeys.Translation.AiContextBefore],
                    out var linesBefore)
                    ? linesBefore
                    : 0;
                contextAfter = int.TryParse(settings[SettingKeys.Translation.AiContextAfter],
                    out var linesAfter)
                    ? linesAfter
                    : 0;
            }

            // validate subtitles
            if (validateSubtitles)
            {
                var validationOptions = new SubtitleValidationOptions
                {
                    // File size setting - default to 2MB if parsing fails
                    MaxFileSizeBytes = long.TryParse(settings[SettingKeys.SubtitleValidation.MaxFileSizeBytes],
                        out var maxFileSizeBytes)
                        ? maxFileSizeBytes
                        : 2 * 1024 * 1024,

                    // Maximum characters per subtitle - default to 500 if parsing fails
                    MaxSubtitleLength = int.TryParse(settings[SettingKeys.SubtitleValidation.MaxSubtitleLength],
                        out var maxSubtitleLength)
                        ? maxSubtitleLength
                        : 500,

                    // Minimum characters per subtitle - default to 1 if parsing fails
                    MinSubtitleLength = int.TryParse(settings[SettingKeys.SubtitleValidation.MinSubtitleLength],
                        out var minSubtitleLength)
                        ? minSubtitleLength
                        : 2,

                    // Minimum duration in milliseconds - default to 500ms if parsing fails
                    MinDurationMs = double.TryParse(settings[SettingKeys.SubtitleValidation.MinDurationMs],
                        out var minDurationMs)
                        ? minDurationMs
                        : 500,

                    // Maximum duration in seconds - default to 10s if parsing fails
                    MaxDurationSecs = double.TryParse(settings[SettingKeys.SubtitleValidation.MaxDurationSecs],
                        out var maxDurationSecs)
                        ? maxDurationSecs
                        : 10,

                    // Used to determine content length when
                    StripSubtitleFormatting = stripSubtitleFormatting
                };

                if (!_subtitleService.ValidateSubtitle(request.SubtitleToTranslate, validationOptions))
                {
                    _logger.LogWarning("Subtitle is not valid according to configured preferences.");
                    throw new TaskCanceledException("Subtitle is not valid according to configured preferences.");
                }
            }

            // translate subtitles
            var services = _translationServiceFactory.CreateTranslationServices(chain);
            var serviceType = services.Count > 0 ? services[0].Name : "unknown";
            var translationService = services.Count > 0 ? services[0].Service : throw new TranslationException("No translation services available.");
            if (services.Count == 0)
            {
                throw new TranslationException($"No usable translation services configured: [{string.Join(", ", chain.Select(e => e.ProviderNormalized))}]");
            }
            translationService = services[0].Service;
            var translator = new SubtitleTranslationService(
                services,
                _logger,
                _progressService,
                _providerHealth,
                _classifier);
            var subtitles = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate);
            if (stripSubtitleHtml)
            {
                foreach (var subtitle in subtitles)
                {
                    SubtitleHtml.Strip(subtitle);
                }
            }

            // subtitle already carries a translation from an earlier prior run.
            // Group by Position and keep the most recent row in case the same position was used more than once.
            var persistedLines = (await _dbContext.TranslationRequestLines
                    .Where(line => line.TranslationRequestId == request.Id)
                    .Select(line => new { line.Id, line.Position, line.Target })
                    .ToListAsync(cancellationToken))
                .GroupBy(line => line.Position)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Id).First().Target);

            if (persistedLines.Count > 0)
            {
                _logger.LogInformation(
                    "Resuming translation for request {RequestId}: {Resumed} of {Total} lines already translated.",
                    request.Id, persistedLines.Count, subtitles.Count);

                foreach (var subtitle in subtitles)
                {
                    if (persistedLines.TryGetValue(subtitle.Position, out var target))
                    {
                        subtitle.TranslatedLines = [target];
                    }
                }
            }

            List<SubtitleItem> translatedSubtitles;
            if (settings[SettingKeys.Translation.UseBatchTranslation] == "true"
                && services.Any(entry => entry.Service is IBatchTranslationService))
            {
                var maxSize = int.TryParse(settings[SettingKeys.Translation.MaxBatchSize],
                    out var batchSize)
                    ? batchSize
                    : 10000;

                _logger.LogInformation(
                    "Using batch translation with max batch size: {maxBatchSize} for subtitle: {filePath}",
                    maxSize, translationRequest.SubtitleToTranslate);

                translatedSubtitles = await translator.TranslateSubtitlesBatch(
                    subtitles,
                    request,
                    stripSubtitleFormatting,
                    preserveLineBreaks,
                    maxSize,
                    cancellationToken);
            }
            else
            {
                if (contextPromptEnabled)
                {
                    _logger.LogInformation(
                        "Using individual translation with context (before: {contextBefore}, after: {contextAfter}) for subtitle: {filePath}",
                        contextBefore, contextAfter, translationRequest.SubtitleToTranslate);
                }

                translatedSubtitles = await translator.TranslateSubtitles(
                    subtitles,
                    request,
                    stripSubtitleFormatting,
                    preserveLineBreaks,
                    contextBefore,
                    contextAfter,
                    cancellationToken
                );
            }

            if (stripSubtitleHtml)
            {
                foreach (var subtitle in translatedSubtitles)
                {
                    SubtitleHtml.StripTranslated(subtitle);
                }
            }

            if (settings[SettingKeys.Translation.FixOverlappingSubtitles] == "true")
            {
                translatedSubtitles = _subtitleService.FixOverlappingSubtitles(translatedSubtitles);
            }

            if (addTranslatorInfo)
            {
                _subtitleService.AddTranslatorInfo(serviceType, translatedSubtitles, translationService);
            }

            if (stripSubtitleFormatting)
            {
                var format = translatedSubtitles[0].SsaFormat;
                if (format != null)
                {
                    format.Styles = [];
                }
            }

            // statistics tracking, only count subtitles translated by this run
            var newlyTranslatedSubtitles = persistedLines.Count == 0
                ? translatedSubtitles
                : translatedSubtitles.Where(s => !persistedLines.ContainsKey(s.Position)).ToList();
            var subtitleTag = "";
            if (settings[SettingKeys.Translation.UseSubtitleTagging] == "true")
            {
                subtitleTag = settings[SettingKeys.Translation.SubtitleTag];
            }

            await WriteSubtitles(request, translatedSubtitles, stripSubtitleFormatting, subtitleTag, removeLanguageTag);
            if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
            {
                await _postProcess.RunAsync(new SubtitlePostProcessJob
                {
                    SourcePath = request.SubtitleToTranslate ?? string.Empty,
                    TargetPath = request.TranslatedSubtitle,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage
                }, cancellationToken);
            }

            await ApplyPlexSafely(request, cancellationToken);
            await _signals.SelectOnServersAsync(ItemRef(request), cancellationToken);
            await EvaluateQualitySafely(request.Id, cancellationToken);
            await _statisticsService.UpdateTranslationStatisticsFromSubtitles(
                request, serviceType, translationService.ModelName, newlyTranslatedSubtitles);
            await HandleCompletion(jobName, request, cancellationToken);
            await _signals.NotifyAsync(new PluginNotice
            {
                Succeeded = true,
                Title = NoticeTitle(request),
                Path = request.TranslatedSubtitle
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await HandleCancellation(jobName, translationRequest);
        }
        catch (Exception ex)
        {
            await _translationRequestService.ClearMediaHash(translationRequest);
            translationRequest = await _translationRequestService.UpdateTranslationRequest(translationRequest, TranslationStatus.Failed,
                jobId);
            
            translationRequest.ErrorMessage = ex.Message;
            translationRequest.StackTrace = ex.ToString();
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Failed, ex.Message);
            await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
            await _translationRequestService.UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            await _signals.NotifyAsync(new PluginNotice
            {
                Succeeded = false,
                Title = NoticeTitle(translationRequest),
                Detail = ex.Message
            }, CancellationToken.None);
            throw;
        }
    }

    private static string NoticeTitle(TranslationRequest request) =>
        Path.GetFileName(request.TranslatedSubtitle ?? request.SubtitleToTranslate) ?? "Subtitle";

    private static MediaItemRef ItemRef(TranslationRequest request) => new()
    {
        Title = NoticeTitle(request),
        Path = request.TranslatedSubtitle,
        SubtitlePath = request.TranslatedSubtitle,
        Language = request.TargetLanguage
    };

    private async Task ApplyPlexSafely(TranslationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _plexSubtitles.ApplyTranslatedSubtitleAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Plex subtitle update failed for translation request {RequestId}. The translated file remains available.",
                request.Id);
        }
    }

    private async Task EvaluateQualitySafely(int translationRequestId, CancellationToken cancellationToken)
    {
        try
        {
            await _translationQuality.EvaluateAsync(translationRequestId, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Subtitle quality evaluation failed for translation request {RequestId}; the translated file remains available.",
                translationRequestId);
        }
    }

    private async Task WriteSubtitles(TranslationRequest translationRequest,
        List<SubtitleItem> translatedSubtitles,
        bool stripSubtitleFormatting,
        string subtitleTag,
        bool removeLanguageTag)
    {
        try
        {
            var targetLanguage = removeLanguageTag ? "" : translationRequest.TargetLanguage;

            var outputPath = _subtitleService.CreateFilePath(
                translationRequest.SubtitleToTranslate,
                targetLanguage,
                subtitleTag);

            await _subtitleService.WriteSubtitles(outputPath, translatedSubtitles, stripSubtitleFormatting);
            translationRequest.TranslatedSubtitle = outputPath;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("TranslateJob completed and created subtitle: |Green|{filePath}|/Green|",
                outputPath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    private async Task HandleCompletion(
        string jobName,
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
        translationRequest.CompletedAt = DateTime.UtcNow;
        translationRequest.Status = TranslationStatus.Completed;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Completed);
        await _translationRequestService.UpdateActiveCount();
        await _progressService.Emit(translationRequest, 100);
        await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
    }

    private async Task HandleCancellation(string jobName, TranslationRequest request)
    {
        _logger.LogInformation("Translation cancelled for subtitle: |Orange|{subtitlePath}|/Orange|",
            request.SubtitleToTranslate);
        var translationRequest =
            await _dbContext.TranslationRequests.FirstOrDefaultAsync(translationRequest =>
                translationRequest.Id == request.Id);

        if (translationRequest == null)
        {
            return;
        }

        if (translationRequest.Status == TranslationStatus.Cancelled
            && !translationRequest.ProviderCancelRetryPending
            && translationRequest.ProviderCancelAttempts == 0)
        {
            await _scheduleService.UpdateJobState(jobName, JobStatus.Cancelled.GetDisplayName());
            return;
        }

        var settings = await _settings.GetSettings([
            SettingKeys.Translation.ProviderCancelRetryCount,
            SettingKeys.Translation.ProviderCancelRetryHours
        ]);
        var max = ProviderCancelRetry.Count(
            settings.GetValueOrDefault(SettingKeys.Translation.ProviderCancelRetryCount));
        var hours = ProviderCancelRetry.Hours(
            settings.GetValueOrDefault(SettingKeys.Translation.ProviderCancelRetryHours));
        var plan = ProviderCancelRetry.Plan(translationRequest.ProviderCancelAttempts, max);

        translationRequest.CompletedAt = DateTime.UtcNow;
        translationRequest.Status = TranslationStatus.Cancelled;
        translationRequest.ProviderCancelAttempts = plan.Attempt;
        translationRequest.ProviderCancelRetryMax = plan.Max;
        translationRequest.ProviderCancelRetryPending = false;

        if (!plan.Retry)
        {
            translationRequest.ErrorMessage = "Translation was cancelled";
            await _dbContext.SaveChangesAsync();
            await _eventService.LogEvent(
                translationRequest.Id,
                TranslationStatus.Cancelled,
                "Translation was cancelled");
            await _translationRequestService.ClearMediaHash(translationRequest);
            await _translationRequestService.UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            await _scheduleService.UpdateJobState(jobName, JobStatus.Cancelled.GetDisplayName());
            return;
        }

        translationRequest.ProviderCancelRetryPending = true;
        translationRequest.ErrorMessage =
            $"The provider cancelled this translation. Lingarr will try again ({plan.Attempt}/{plan.Max}).";
        await _dbContext.SaveChangesAsync();

        var scheduledId = BackgroundJob.Schedule<TranslationJob>(
            job => job.Execute(translationRequest, CancellationToken.None),
            TimeSpan.FromHours(hours));
        translationRequest.JobId = scheduledId;
        await _dbContext.SaveChangesAsync();
        await _eventService.LogEvent(
            translationRequest.Id,
            TranslationStatus.Cancelled,
            translationRequest.ErrorMessage);
        await _translationRequestService.ClearMediaHash(translationRequest);
        await _translationRequestService.UpdateActiveCount();
        await _progressService.Emit(translationRequest, translationRequest.CachedProgress ?? 0);
        await _scheduleService.UpdateJobState(jobName, JobStatus.Cancelled.GetDisplayName());
    }
}
