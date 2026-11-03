using System.Linq;
using System.Net;
using GTranslate.Translators;
using Lingarr.Contracts.Exceptions;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Translation.Base;

namespace Lingarr.Server.Services.Translation;

public class GTranslatorService<T> : BaseLanguageService where T : ITranslator
{
    private const int MicrosoftTranslatorMaxCharacters = 1000;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _provider;
    private T? _translator;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // retry settings
    private int _maxRetries;
    private TimeSpan _retryDelay;
    private int _retryDelayMultiplier;
    private TimeSpan _attemptTimeout;
    private TimeSpan _requestTimeout;

    /// <inheritdoc />
    public override string? ModelName => null;

    public GTranslatorService(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        string provider,
        string languageFilePath,
        ISettingService settings,
        ILogger logger,
        LanguageCodeService languageCodeService) : base(settings, logger, languageCodeService, languageFilePath)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _provider = provider;
    }

    private async Task InitializeAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            var settings = await _settings.GetSettings([
                SettingKeys.Translation.MaxRetries,
                SettingKeys.Translation.RetryDelay,
                SettingKeys.Translation.RetryDelayMultiplier,
                SettingKeys.Translation.RequestTimeout,
                SettingKeys.Translation.RequestTimeoutForProvider(_provider),
                SettingKeys.Translation.RequestAttemptTimeout,
                SettingKeys.Translation.RequestAttemptTimeoutForProvider(_provider)
            ]);

            _maxRetries = int.TryParse(settings[SettingKeys.Translation.MaxRetries], out var maxRetries)
                ? maxRetries
                : 3;

            var retryDelaySeconds = int.TryParse(settings[SettingKeys.Translation.RetryDelay], out var delaySeconds)
                ? delaySeconds
                : 2;
            _retryDelay = TimeSpan.FromSeconds(retryDelaySeconds);

            _retryDelayMultiplier = int.TryParse(settings[SettingKeys.Translation.RetryDelayMultiplier], out var multiplier)
                ? multiplier
                : 2;

            var requestTimeout = TimeSpan.FromMinutes(
                TranslationTimeoutPolicy.ResolveMinutes(settings, _provider));
            var attemptSeconds = TranslationTimeoutPolicy.ResolveAttemptSeconds(settings, _provider);
            _requestTimeout = requestTimeout;
            _attemptTimeout = TimeSpan.FromSeconds(Math.Min(attemptSeconds, requestTimeout.TotalSeconds));

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = requestTimeout;

            _translator ??= ActivatorUtilities.CreateInstance<T>(_serviceProvider, httpClient);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public override async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        List<string>? contextLinesBefore,
        List<string>? contextLinesAfter,
        CancellationToken cancellationToken)
    {
        await InitializeAsync();

        // One deadline for the whole call, retries included: HttpClient.Timeout bounds a single
        // request, so without this a slow provider could spend attempt after attempt past the
        // configured request timeout.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_requestTimeout);

        var segments = GetTranslationSegments(text);
        if (segments.Count > 1)
        {
            _logger.LogWarning(
                "Splitting Microsoft Translator input into {SegmentCount} chunks because the line length {Length} exceeds the {Limit}-character limit.",
                segments.Count,
                text.Length,
                MicrosoftTranslatorMaxCharacters);
        }

        var translatedSegments = new List<string>(segments.Count);
        foreach (var segment in segments)
        {
            translatedSegments.Add(await TranslateSegmentAsync(
                segment,
                sourceLanguage,
                targetLanguage,
                cancellationToken,
                budget.Token));
        }

        return string.Join(" ", translatedSegments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private async Task<string> TranslateSegmentAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken,
        CancellationToken budgetToken)
    {
        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries + 1; attempt++)
        {
            using var attemptCancel = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, budgetToken);
            attemptCancel.CancelAfter(_attemptTimeout);

            try
            {
                var result = await _translator!.TranslateAsync(
                        text,
                        targetLanguage,
                        sourceLanguage)
                    .WaitAsync(attemptCancel.Token)
                    .ConfigureAwait(false);

                return result.Translation;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (budgetToken.IsCancellationRequested)
            {
                // The whole request deadline is gone; another attempt would exceed the budget the
                // provider was given, so stop here and let the caller decide about a retry.
                var seconds = _requestTimeout.TotalSeconds;
                _logger.LogError(
                    "{ServiceName} used its whole {Seconds}s request budget across {Attempt} attempt(s).",
                    _provider, seconds, attempt);
                throw new TranslationException(
                    $"The provider did not answer within the {seconds:F0} second request timeout.");
            }
            catch (OperationCanceledException) when (attemptCancel.IsCancellationRequested)
            {
                // The call never answered inside the attempt budget. The provider keeps its full
                // request timeout for the retry, so a dead connection costs seconds, not the job.
                var seconds = _attemptTimeout.TotalSeconds;
                if (attempt > _maxRetries)
                {
                    _logger.LogError(
                        "{ServiceName} did not answer within {Seconds}s on attempt {Attempt} of {Total}.",
                        _provider, seconds, attempt, _maxRetries + 1);
                    throw new TranslationException(
                        $"The provider did not answer within {seconds:F0} seconds after {_maxRetries + 1} attempts.");
                }

                _logger.LogWarning(
                    "{ServiceName} did not answer within {Seconds}s. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    _provider, seconds, delay, attempt, _maxRetries);

                await DelayWithinBudget(delay, cancellationToken, budgetToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                || IsTransientNetworkFailure(ex))
            {
                if (attempt > _maxRetries)
                {
                    _logger.LogError(ex, "Max retries exhausted ({StatusCode}) for text: {Text}", ex.StatusCode, text);
                    throw new TranslationException($"Retry limit reached after {ex.StatusCode}.", ex);
                }

                _logger.LogWarning(
                    "{ServiceName} received {StatusCode}. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    _provider, ex.StatusCode, delay, attempt, _maxRetries);

                await DelayWithinBudget(delay, cancellationToken, budgetToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (Exception ex) when (IsTransientNetworkFailure(ex) && attempt <= _maxRetries)
            {
                // Timeouts / connection resets from free Microsoft (and peers) should not fail the job on first hit.
                _logger.LogWarning(ex,
                    "{ServiceName} transient error. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    _provider, delay, attempt, _maxRetries);

                await DelayWithinBudget(delay, cancellationToken, budgetToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during translation attempt {Attempt}", attempt);
                throw new TranslationException("Unexpected error occurred during translation.", ex);
            }
        }

        throw new TranslationException("Translation failed after maximum retry attempts.");
    }

    private List<string> GetTranslationSegments(string text)
    {
        if (!IsMicrosoftTranslator() || text.Length <= MicrosoftTranslatorMaxCharacters)
        {
            return [text];
        }

        return TranslationTextChunker.Split(text, MicrosoftTranslatorMaxCharacters);
    }

    private bool IsMicrosoftTranslator()
    {
        return string.Equals(_provider, "microsoft", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientNetworkFailure(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            switch (current)
            {
                case TimeoutException:
                case IOException:
                case HttpRequestException http when http.StatusCode is null
                    or HttpStatusCode.RequestTimeout
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout:
                    return true;
                case TaskCanceledException tce when !tce.CancellationToken.IsCancellationRequested:
                    // HttpClient timeout surfaces as TaskCanceledException without a caller cancel.
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Waits between attempts without ever overrunning the request budget. A budget that runs out
    /// during the wait ends the call with a timeout, so a retry sequence cannot outlive the timeout
    /// the provider was configured with.
    /// </summary>
    private async Task DelayWithinBudget(
        TimeSpan delay,
        CancellationToken cancellationToken,
        CancellationToken budgetToken)
    {
        try
        {
            await Task.Delay(WithJitter(delay), budgetToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "{ServiceName} had no request budget left to retry. Attempted with {Seconds}s per attempt.",
                _provider, _attemptTimeout.TotalSeconds);
            throw new TranslationException(
                $"The provider did not answer within the {_requestTimeout.TotalSeconds:F0} second request timeout.");
        }
    }

    private static TimeSpan WithJitter(TimeSpan delay)
    {
        // +/- 20% jitter to avoid thundering herds against free translator endpoints.
        var factor = 0.8 + (Random.Shared.NextDouble() * 0.4);
        return TimeSpan.FromMilliseconds(Math.Max(100, delay.TotalMilliseconds * factor));
    }
}
