using System.Diagnostics;
using System.Net;
using Lingarr.Contracts.Plugins;
using Lingarr.Contracts.Translation;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.ProviderHealth;
using Lingarr.Server.Services.Plugins;
using Lingarr.Server.Services.Translation;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public sealed class ProviderHealthService : IProviderHealthService
{
    private const int PolicyVersion = 1;
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromHours(48);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(30);

    private readonly LingarrDbContext _dbContext;
    private readonly IPluginRegistry _registry;
    private readonly ISettingService _settings;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly PluginLoader _plugins;
    private readonly ILogger<ProviderHealthService> _logger;

    public ProviderHealthService(
        LingarrDbContext dbContext,
        IPluginRegistry registry,
        ISettingService settings,
        ITranslationServiceFactory translationServiceFactory,
        PluginLoader plugins,
        ILogger<ProviderHealthService> logger)
    {
        _dbContext = dbContext;
        _registry = registry;
        _settings = settings;
        _translationServiceFactory = translationServiceFactory;
        _plugins = plugins;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProviderHealthResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var chain = TranslationChain.Parse(
            await _settings.GetSetting(SettingKeys.Translation.ServiceType));
        var chainOrder = chain
            .Select((entry, index) => new { entry.ProviderNormalized, index })
            .GroupBy(entry => entry.ProviderNormalized)
            .ToDictionary(group => group.Key, group => group.Min(entry => entry.index));
        var chainModels = chain
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Model))
            .GroupBy(entry => entry.ProviderNormalized)
            .ToDictionary(group => group.Key, group => group.First().Model);

        var responses = new List<ProviderHealthResponse>(_registry.All.Count);
        foreach (var plugin in TranslationProviders())
        {
            var configuration = await GetConfigurationStatus(plugin, cancellationToken);
            var snapshot = await ComputeAsync(
                plugin.Manifest.Provider,
                configuration.Configured,
                cancellationToken);

            responses.Add(ToResponse(
                plugin.Manifest.Provider,
                plugin.Manifest.DisplayName,
                chainModels.GetValueOrDefault(plugin.Manifest.Provider),
                configuration,
                snapshot));
        }

        return responses
            .OrderBy(item => chainOrder.GetValueOrDefault(item.Provider, int.MaxValue))
            .ThenBy(item => item.DisplayName)
            .ToList();
    }

    public async Task<IReadOnlyList<ProviderHealthEventResponse>> GetEventsAsync(
        string provider,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 200);
        return await _dbContext.ProviderOperationalEvents
            .AsNoTracking()
            .Where(item => item.Provider == provider.ToLowerInvariant())
            .OrderByDescending(item => item.OccurredAt)
            .Take(safeLimit)
            .Select(item => new ProviderHealthEventResponse
            {
                Id = item.Id,
                Provider = item.Provider,
                Model = item.Model,
                Operation = item.Operation,
                Outcome = item.Outcome,
                ErrorFamily = item.ErrorFamily,
                IsTransient = item.IsTransient,
                DurationMs = item.DurationMs,
                RetryCount = item.RetryCount,
                TranslationRequestId = item.TranslationRequestId,
                OccurredAt = item.OccurredAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task RecordAsync(
        ProviderOperationalResult result,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = result.Provider.Trim().ToLowerInvariant();
        var operationalEvent = new ProviderOperationalEvent
        {
            Provider = normalizedProvider,
            Model = result.Model,
            Operation = result.Operation,
            Outcome = result.Outcome,
            ErrorFamily = result.ErrorFamily,
            IsTransient = result.IsTransient,
            DurationMs = result.DurationMs,
            RetryCount = result.RetryCount,
            TranslationRequestId = result.TranslationRequestId,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            EstimatedCostUsd = result.EstimatedCostUsd,
            OccurredAt = DateTime.UtcNow,
            PolicyVersion = PolicyVersion
        };
        _dbContext.ProviderOperationalEvents.Add(operationalEvent);

        // A translation is already on its way out at this point. Losing the record of it is
        // better than failing the request that produced it, and the next event re-evaluates.
        if (!await SqliteWriteRetry.TrySaveAsync(
                _dbContext,
                _logger,
                $"the {operationalEvent.Operation} outcome for {normalizedProvider}",
                cancellationToken,
                SqliteWriteRetry.BriefDelays))
        {
            _dbContext.Entry(operationalEvent).State = EntityState.Detached;
            _logger.LogWarning(
                "The {Operation} outcome for {Provider} was dropped because the database stayed locked.",
                operationalEvent.Operation,
                normalizedProvider);
            return;
        }

        var plugin = _registry.Find(normalizedProvider);
        if (plugin is not null)
        {
            var configuration = await GetConfigurationStatus(plugin, cancellationToken);
            await EvaluateAsync(normalizedProvider, configuration.Configured, cancellationToken);
        }
    }

    public async Task<ProviderProbeResponse> ProbeAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        var plugin = _registry.Find(provider);
        if (plugin is null)
        {
            throw new KeyNotFoundException($"Provider '{provider}' is not registered.");
        }

        var configuration = await GetConfigurationStatus(plugin, cancellationToken);
        if (!configuration.Configured)
        {
            return new ProviderProbeResponse
            {
                Provider = plugin.Manifest.Provider,
                SourceLanguage = "",
                TargetLanguage = "",
                Supported = false,
                Success = false,
                DurationMs = 0,
                ErrorFamily = "configuration",
                Message = "Complete the required provider settings before running a test."
            };
        }

        var sourceLanguage = FirstLanguage(
            await _settings.GetSetting(SettingKeys.Translation.SourceLanguages),
            "en");
        var targetLanguage = FirstLanguage(
            await _settings.GetSetting(SettingKeys.Translation.TargetLanguages),
            "es");
        var chain = TranslationChain.Parse(
            await _settings.GetSetting(SettingKeys.Translation.ServiceType));
        var configuredEntry = chain.FirstOrDefault(
            entry => entry.ProviderNormalized == plugin.Manifest.Provider);
        var model = configuredEntry?.Model;

        var service = _translationServiceFactory.CreateTranslationService(plugin.Manifest.Provider);
        if (!string.IsNullOrWhiteSpace(model) && service is IModelOverridable overridable)
        {
            overridable.OverrideModel(model);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var pair = await service.GetLanguagePair(sourceLanguage, targetLanguage, cancellationToken);
            if (pair is null)
            {
                stopwatch.Stop();
                await RecordAsync(new ProviderOperationalResult
                {
                    Provider = plugin.Manifest.Provider,
                    Model = model,
                    Operation = "probe",
                    Outcome = "warning",
                    ErrorFamily = "unsupported_language",
                    IsTransient = false,
                    DurationMs = stopwatch.ElapsedMilliseconds
                }, cancellationToken);

                return new ProviderProbeResponse
                {
                    Provider = plugin.Manifest.Provider,
                    Model = model,
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    Supported = false,
                    Success = false,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    ErrorFamily = "unsupported_language",
                    Message = "This provider does not support the currently selected language pair."
                };
            }

            _ = await service.TranslateAsync(
                "Lingarr Next health check.",
                pair.Source,
                pair.Target,
                null,
                null,
                cancellationToken);
            stopwatch.Stop();

            await RecordAsync(new ProviderOperationalResult
            {
                Provider = plugin.Manifest.Provider,
                Model = model,
                Operation = "probe",
                Outcome = "success",
                DurationMs = stopwatch.ElapsedMilliseconds
            }, cancellationToken);

            return new ProviderProbeResponse
            {
                Provider = plugin.Manifest.Provider,
                Model = model,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                Supported = true,
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = "The provider responded successfully."
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var classification = Classify(exception);
            await RecordAsync(new ProviderOperationalResult
            {
                Provider = plugin.Manifest.Provider,
                Model = model,
                Operation = "probe",
                Outcome = "failure",
                ErrorFamily = classification.Family,
                IsTransient = classification.Transient,
                DurationMs = stopwatch.ElapsedMilliseconds
            }, cancellationToken);

            return new ProviderProbeResponse
            {
                Provider = plugin.Manifest.Provider,
                Model = model,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                Supported = true,
                Success = false,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ErrorFamily = classification.Family,
                Message = RecoveryMessage(classification.Family)
            };
        }
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        foreach (var plugin in TranslationProviders())
        {
            var configuration = await GetConfigurationStatus(plugin, cancellationToken);
            await EvaluateAsync(
                plugin.Manifest.Provider,
                configuration.Configured,
                cancellationToken);
        }

        // Retention is housekeeping: it runs after the readings, it is attempted once, and any
        // failure is a log line rather than a failed reconciliation. A locked or timed-out delete
        // costs nothing but the next hour's attempt.
        var cutoff = DateTime.UtcNow - RetentionWindow;
        try
        {
            await _dbContext.ProviderOperationalEvents
                .Where(item => item.OccurredAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Skipped the retention delete of old provider events; it runs again on the next reconciliation.");
        }
    }

    private IEnumerable<RegisteredPlugin> TranslationProviders() =>
        _registry.All.Where(plugin =>
            plugin.IsBuiltIn || _plugins.ProvidesTranslation(plugin.Manifest.Provider));

    public static (string Family, bool Transient) Classify(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return ("cancelled", false);
        }

        if (exception is TimeoutException or TaskCanceledException)
        {
            return ("timeout", true);
        }

        if (exception is HttpRequestException httpException)
        {
            return httpException.StatusCode switch
            {
                HttpStatusCode.Unauthorized => ("authentication", false),
                HttpStatusCode.Forbidden => ("authorization", false),
                HttpStatusCode.TooManyRequests => ("rate_limit", true),
                >= HttpStatusCode.InternalServerError => ("provider_unavailable", true),
                _ => ("network", true)
            };
        }

        if (exception is ArgumentException)
        {
            return ("invalid_request", false);
        }

        if (exception is InvalidOperationException)
        {
            return ("configuration", false);
        }

        return exception.InnerException is not null
            ? Classify(exception.InnerException)
            : ("unknown", false);
    }

    /// <summary>
    /// Evaluates a provider and stores the reading, so a stored snapshot only moves when an event
    /// or the hourly reconciliation changes it.
    /// </summary>
    private async Task<ProviderHealthSnapshot> EvaluateAsync(
        string provider,
        bool configured,
        CancellationToken cancellationToken)
    {
        var computed = await ComputeAsync(provider, configured, cancellationToken);
        var snapshot = await _dbContext.ProviderHealthSnapshots
            .SingleOrDefaultAsync(item => item.Provider == computed.Provider, cancellationToken);

        if (snapshot is null)
        {
            snapshot = computed;
            _dbContext.ProviderHealthSnapshots.Add(snapshot);
        }
        else
        {
            // A recorded outcome can land a newer reading while this one is being computed. The
            // newer one wins; a reconciliation must not write an older view over it.
            if (snapshot.EvaluatedAt > computed.EvaluatedAt)
            {
                return snapshot;
            }

            CopyReading(computed, snapshot);
        }

        if (await SqliteWriteRetry.TrySaveAsync(
                _dbContext,
                _logger,
                $"the health reading for {computed.Provider}",
                cancellationToken))
        {
            return snapshot;
        }

        _dbContext.Entry(snapshot).State = EntityState.Detached;
        return computed;
    }

    /// <summary>
    /// Reads a provider's recent events and returns its state without writing anything. The
    /// dashboard polls this, and a read must not queue behind a library sync holding the writer.
    /// </summary>
    private async Task<ProviderHealthSnapshot> ComputeAsync(
        string provider,
        bool configured,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = provider.ToLowerInvariant();
        var now = DateTime.UtcNow;
        var observationStart = now - ObservationWindow;
        var events = await _dbContext.ProviderOperationalEvents
            .AsNoTracking()
            .Where(item =>
                item.Provider == normalizedProvider
                && item.OccurredAt >= now - TimeSpan.FromDays(7))
            .OrderBy(item => item.OccurredAt)
            .ToListAsync(cancellationToken);
        var observed = events.Where(item => item.OccurredAt >= observationStart).ToList();
        var successes = observed.Where(item => item.Outcome == "success").ToList();
        var failures = observed.Where(item => item.Outcome == "failure").ToList();
        var warnings = observed.Where(item => item.Outcome == "warning").ToList();
        var consecutiveFailures = events
            .AsEnumerable()
            .Reverse()
            .TakeWhile(item => item.Outcome == "failure")
            .Count();

        var (state, reason) = EvaluateState(
            configured,
            events,
            observed,
            successes,
            failures,
            warnings,
            consecutiveFailures,
            now);

        return new ProviderHealthSnapshot
        {
            Provider = normalizedProvider,
            State = state,
            Reason = reason,
            LastSuccessAt = events.LastOrDefault(item => item.Outcome == "success")?.OccurredAt,
            LastFailureAt = events.LastOrDefault(item => item.Outcome == "failure")?.OccurredAt,
            LastWarningAt = events.LastOrDefault(item => item.Outcome == "warning")?.OccurredAt,
            ConsecutiveFailures = consecutiveFailures,
            SuccessCount = successes.Count,
            FailureCount = failures.Count,
            MedianDurationMs = Median(observed.Select(item => item.DurationMs)),
            EvaluatedAt = now,
            PolicyVersion = PolicyVersion
        };
    }

    private static void CopyReading(ProviderHealthSnapshot source, ProviderHealthSnapshot target)
    {
        target.State = source.State;
        target.Reason = source.Reason;
        target.LastSuccessAt = source.LastSuccessAt;
        target.LastFailureAt = source.LastFailureAt;
        target.LastWarningAt = source.LastWarningAt;
        target.ConsecutiveFailures = source.ConsecutiveFailures;
        target.SuccessCount = source.SuccessCount;
        target.FailureCount = source.FailureCount;
        target.MedianDurationMs = source.MedianDurationMs;
        target.EvaluatedAt = source.EvaluatedAt;
        target.PolicyVersion = source.PolicyVersion;
    }

    private static (string State, string Reason) EvaluateState(
        bool configured,
        IReadOnlyList<ProviderOperationalEvent> events,
        IReadOnlyList<ProviderOperationalEvent> observed,
        IReadOnlyList<ProviderOperationalEvent> successes,
        IReadOnlyList<ProviderOperationalEvent> failures,
        IReadOnlyList<ProviderOperationalEvent> warnings,
        int consecutiveFailures,
        DateTime now)
    {
        if (!configured)
        {
            return (ProviderHealthStates.NotConfigured, "Required provider settings are missing.");
        }

        if (observed.Count == 0)
        {
            return (ProviderHealthStates.NotChecked, "Configured, but no recent translation or test has been observed.");
        }

        var latestFailure = failures.LastOrDefault();
        var systemicFamilies = new[] { "authentication", "authorization", "configuration", "quota" };
        if (latestFailure?.ErrorFamily is not null
            && systemicFamilies.Contains(latestFailure.ErrorFamily)
            && (successes.Count == 0 || latestFailure.OccurredAt > successes.Last().OccurredAt))
        {
            return (ProviderHealthStates.Unavailable, RecoveryMessage(latestFailure.ErrorFamily));
        }

        var failures24h = failures.Count(item => item.OccurredAt >= now - TimeSpan.FromHours(24));
        var successes24h = successes.Count(item => item.OccurredAt >= now - TimeSpan.FromHours(24));
        if (failures24h >= 10 && successes24h == 0)
        {
            return (ProviderHealthStates.Unavailable, "Repeated failures have continued for at least the current day.");
        }

        var events15m = observed.Where(item => item.OccurredAt >= now - TimeSpan.FromMinutes(15)).ToList();
        var failureRate15m = events15m.Count == 0
            ? 0
            : events15m.Count(item => item.Outcome == "failure") / (double)events15m.Count;
        if (consecutiveFailures >= 3
            || (events15m.Count >= 3 && failureRate15m > 0.5 && events.Any(item => item.Outcome == "success")))
        {
            return (ProviderHealthStates.RecentlyUnavailable, "Several recent attempts failed after this provider had worked before.");
        }

        var materialAttempts = successes.Count + failures.Count;
        var failureRate = materialAttempts == 0 ? 0 : failures.Count / (double)materialAttempts;
        if (warnings.Count > 0 || failures.Count > 0 || failureRate >= 0.05)
        {
            return (ProviderHealthStates.NeedsAttention, "The provider is usable, but recent warnings or recoverable failures need attention.");
        }

        if (successes.Count > 0)
        {
            return (ProviderHealthStates.Healthy, "A recent translation or provider test completed successfully.");
        }

        return (ProviderHealthStates.NotChecked, "Configured, but no successful translation or test has been observed.");
    }

    private async Task<(bool Configured, IReadOnlyList<string> MissingFields)> GetConfigurationStatus(
        RegisteredPlugin plugin,
        CancellationToken cancellationToken)
    {
        var missingFields = new List<string>();
        foreach (var field in plugin.Manifest.Settings.Where(item => item.Required))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = field.Type == PluginSettingType.Secret
                ? await _settings.GetEncryptedSetting(field.Key)
                : await _settings.GetSetting(field.Key);
            if (string.IsNullOrWhiteSpace(value))
            {
                missingFields.Add(field.Label);
            }
        }

        return (missingFields.Count == 0, missingFields);
    }

    private static ProviderHealthResponse ToResponse(
        string provider,
        string displayName,
        string? model,
        (bool Configured, IReadOnlyList<string> MissingFields) configuration,
        ProviderHealthSnapshot snapshot)
    {
        var attempts = snapshot.SuccessCount + snapshot.FailureCount;
        return new ProviderHealthResponse
        {
            Provider = provider,
            DisplayName = displayName,
            Model = model,
            Configured = configuration.Configured,
            MissingFields = configuration.MissingFields,
            State = snapshot.State,
            StatusLabel = StatusLabel(snapshot.State),
            Reason = snapshot.Reason,
            LastSuccessAt = snapshot.LastSuccessAt,
            LastFailureAt = snapshot.LastFailureAt,
            LastWarningAt = snapshot.LastWarningAt,
            ConsecutiveFailures = snapshot.ConsecutiveFailures,
            SuccessCount = snapshot.SuccessCount,
            FailureCount = snapshot.FailureCount,
            SuccessRate = attempts == 0
                ? null
                : Math.Round(snapshot.SuccessCount / (double)attempts * 100, 1),
            MedianDurationMs = snapshot.MedianDurationMs,
            EvaluatedAt = snapshot.EvaluatedAt
        };
    }

    private static string FirstLanguage(string? setting, string fallback)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return fallback;
        }

        try
        {
            if (setting.TrimStart().StartsWith('['))
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(setting)?.FirstOrDefault()
                       ?? fallback;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return fallback;
        }

        return setting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? fallback;
    }

    private static long? Median(IEnumerable<long> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0) return null;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }

    private static string StatusLabel(string state) => state switch
    {
        ProviderHealthStates.NotConfigured => "Not configured",
        ProviderHealthStates.NotChecked => "Not checked",
        ProviderHealthStates.Healthy => "Healthy",
        ProviderHealthStates.NeedsAttention => "Needs attention",
        ProviderHealthStates.RecentlyUnavailable => "Recently unavailable",
        ProviderHealthStates.Unavailable => "Unavailable",
        _ => "Unknown"
    };

    private static string RecoveryMessage(string family) => family switch
    {
        "authentication" => "The provider rejected its credentials. Check the API key.",
        "authorization" => "The credentials do not have permission to use this provider or model.",
        "configuration" => "The provider configuration is incomplete or invalid.",
        "quota" => "The provider quota or account balance is unavailable.",
        "rate_limit" => "The provider is rate limiting requests. Wait and try again.",
        "timeout" => "The provider did not respond before the timeout.",
        "network" => "Lingarr Next could not reach the provider.",
        "provider_unavailable" => "The provider is temporarily unavailable.",
        _ => "The provider test failed. Open Logs for technical details."
    };
}
