using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Translation;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Contracts.Models;
using Lingarr.Server.Services.Translation.Base;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// OpenRouter translation service.
/// Provides access to 100+ models through a unified OpenAI-compatible API.
/// </summary>
public class OpenRouterService : BaseMeteredLanguageService, IProofreadService
{
    private string? _endpoint = "https://openrouter.ai/api/v1/";
    private readonly HttpClient _httpClient;
    private readonly IRequestTemplateService _requestTemplateService;
    private string? _model;
    private string? _prompt;
    private string? _apiKey;
    private string? _requestTemplate;
    private double _temperature = 0.3;
    private int _maxTokens = 4096;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <inheritdoc />
    public override string? ModelName => _model;

    public OpenRouterService(
        ISettingService settings,
        HttpClient httpClient,
        ILogger<OpenRouterService> logger,
        LanguageCodeService languageCodeService,
        IRequestTemplateService requestTemplateService)
        : base(settings, logger, languageCodeService, "openrouter")
    {
        _httpClient = httpClient;
        _requestTemplateService = requestTemplateService;
    }

    private async Task InitializeAsync(string sourceLanguage, string targetLanguage)
    {
        if (_initialized) return;

        try
        {
            await _initLock.WaitAsync();
            if (_initialized) return;

            var settings = await _settings.GetSettings([
                SettingKeys.Translation.OpenRouter.Endpoint,
                SettingKeys.Translation.OpenRouter.Model,
                SettingKeys.Translation.OpenRouter.RequestTemplate,
                SettingKeys.Translation.OpenRouter.Temperature,
                SettingKeys.Translation.OpenRouter.MaxTokens,
                SettingKeys.Translation.AiPrompt,
                SettingKeys.Translation.AiContextPromptEnabled,
                SettingKeys.Translation.AiContextPrompt,
                SettingKeys.Translation.LanguageCodeFormat,
                SettingKeys.Translation.RequestTimeout,
                SettingKeys.Translation.RequestTimeoutForProvider("openrouter")
            ]);

            _endpoint = settings[SettingKeys.Translation.OpenRouter.Endpoint];
            if (string.IsNullOrWhiteSpace(_endpoint))
            {
                _endpoint = "https://openrouter.ai/api/v1/";
            }
            if (!_endpoint.EndsWith("/"))
            {
                _endpoint += "/";
            }

            _model = ResolveModel(settings[SettingKeys.Translation.OpenRouter.Model]);
            _requestTemplate = !string.IsNullOrEmpty(settings[SettingKeys.Translation.OpenRouter.RequestTemplate])
                ? settings[SettingKeys.Translation.OpenRouter.RequestTemplate]
                : _requestTemplateService.GetDefaultTemplate(SettingKeys.Translation.OpenRouter.RequestTemplate);
            var resolvedPrompt = ResolveSystemPrompt(settings[SettingKeys.Translation.AiPrompt]);
            _prompt = !string.IsNullOrEmpty(resolvedPrompt)
                ? resolvedPrompt
                : "Translate from {sourceLanguage} to {targetLanguage}. Only return the translated text without any additional explanation.";
            _contextPromptEnabled = settings[SettingKeys.Translation.AiContextPromptEnabled];
            _contextPrompt = ResolveContextPrompt(settings[SettingKeys.Translation.AiContextPrompt]);

            if (double.TryParse(settings[SettingKeys.Translation.OpenRouter.Temperature], 
                out var parsedTemp) && parsedTemp >= 0 && parsedTemp <= 2)
            {
                _temperature = parsedTemp;
            }

            if (int.TryParse(settings[SettingKeys.Translation.OpenRouter.MaxTokens], 
                out var parsedTokens) && parsedTokens > 0)
            {
                _maxTokens = parsedTokens;
            }

            SetLanguageReplacements(sourceLanguage, targetLanguage,
                settings[SettingKeys.Translation.LanguageCodeFormat]);

            _apiKey = await _settings.GetEncryptedSetting(SettingKeys.Translation.OpenRouter.ApiKey);

            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("OpenRouter API key is not configured.");
            }

            if (string.IsNullOrEmpty(_model))
            {
                throw new InvalidOperationException("OpenRouter model is not selected.");
            }

            _httpClient.Timeout = TimeSpan.FromMinutes(
                TranslationTimeoutPolicy.ResolveMinutes(settings, "openrouter"));

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public Task<string> ProofreadAsync(
        string sourceText,
        string translatedText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        ProofreadViaTranslateAsync(sourceText, translatedText, sourceLanguage, targetLanguage, cancellationToken);

    public override async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        List<string>? contextLinesBefore,
        List<string>? contextLinesAfter,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(sourceLanguage, targetLanguage);

        // Build context string from prior dialogue lines (separate from the line to translate)
        var contextBefore = string.Empty;
        if (_contextPromptEnabled == "true" && contextLinesBefore?.Count > 0)
        {
            contextBefore = string.Join("\n", contextLinesBefore);
        }

        var systemPrompt = _prompt?
            .Replace("{sourceLanguage}", _replacements["sourceLanguage"])
            .Replace("{targetLanguage}", _replacements["targetLanguage"]) ??
            $"Translate from {_replacements["sourceLanguage"]} to {_replacements["targetLanguage"]}. Only return the translated text without any additional explanation.";

        // Build request body from the configurable template.
        // {contextBefore} = prior dialogue lines (empty when disabled)
        // {userMessage} = just the line to translate
        var templateSource = !string.IsNullOrWhiteSpace(_requestTemplate)
            ? _requestTemplate!
            : (_requestTemplateService.GetDefaultTemplate(SettingKeys.Translation.OpenRouter.RequestTemplate)
               ?? _requestTemplateService.GetDefaultTemplate(SettingKeys.Translation.OpenAi.RequestTemplate)
               ?? "");
        if (string.IsNullOrWhiteSpace(templateSource))
        {
            throw new InvalidOperationException("OpenRouter request template is not configured.");
        }

        var requestTemplate = _requestTemplateService.BuildRequestBody(
            templateSource,
            new Dictionary<string, string>
            {
                ["model"] = _model!,
                ["systemPrompt"] = systemPrompt,
                ["userMessage"] = text,
                ["contextBefore"] = contextBefore,
                ["temperature"] = _temperature.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture),
                ["maxTokens"] = _maxTokens.ToString()
            });

        // BuildRequestBody uses JsonEncodedText which wraps values in quotes.
        // Fix temperature/max_tokens from strings to proper JSON numbers.
        using var bodyDoc = JsonDocument.Parse(requestTemplate);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        writer.WriteStartObject();
        foreach (var prop in bodyDoc.RootElement.EnumerateObject())
        {
            if (prop.Name == "temperature" && prop.Value.ValueKind == JsonValueKind.String &&
                double.TryParse(prop.Value.GetString(), 
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var tempVal))
            {
                writer.WriteNumber("temperature", tempVal);
            }
            else if (prop.Name == "max_tokens" && prop.Value.ValueKind == JsonValueKind.String &&
                int.TryParse(prop.Value.GetString(), out var tokenVal))
            {
                writer.WriteNumber("max_tokens", tokenVal);
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
        writer.Flush();

        var fixedBody = Encoding.UTF8.GetString(stream.ToArray());
        var content = new StringContent(fixedBody, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}chat/completions")
        {
            Content = content
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Add("HTTP-Referer", "https://github.com/apoapostolov/lingarr-next");
        request.Headers.Add("X-Title", "Lingarr Next");

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new TranslationException($"OpenRouter API error ({response.StatusCode}): {error}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);
        RecordUsage(doc.RootElement);

        var translatedText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return translatedText?.Trim() ?? string.Empty;
    }



    public override async Task<ModelsResponse> GetModels()
    {
        _apiKey ??= await _settings.GetEncryptedSetting(SettingKeys.Translation.OpenRouter.ApiKey);

        try
        {
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/apoapostolov/lingarr-next");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Lingarr Next");

            var response = await _httpClient.GetAsync("https://openrouter.ai/api/v1/models");

            if (!response.IsSuccessStatusCode)
            {
                return new ModelsResponse { Message = $"Failed to fetch models: {response.StatusCode}" };
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                return new ModelsResponse { Message = "No models data returned." };
            }

            var options = dataElement.EnumerateArray()
                .Select(model =>
                {
                    var id = model.GetProperty("id").GetString() ?? "";
                    var name = model.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;

                    // Bedroom default metamodel: free router first (cheap/low-quality OK for bulk subtitles).
                    var label = id switch
                    {
                        "openrouter/free" => "openrouter/free • Free metamodel (default for bulk)",
                        "openrouter/auto" => "openrouter/auto • Auto router",
                        _ => name
                    };

                    // Show cost per 1M mixed tokens instead of context length
                    // OpenRouter API returns pricing as strings like "0.0000005"
                    if (id is not ("openrouter/free" or "openrouter/auto")
                        && model.TryGetProperty("pricing", out var pricing)
                        && pricing.ValueKind == JsonValueKind.Object)
                    {
                        var promptPrice = ParsePricing(pricing, "prompt");
                        var completionPrice = ParsePricing(pricing, "completion");

                        if (promptPrice == 0m && completionPrice == 0m)
                        {
                            label += " • Free";
                        }
                        else if (promptPrice > 0m && completionPrice > 0m)
                        {
                            // Cost for 1M tokens: 500K input + 500K output
                            var costPer1M = promptPrice * 500000m + completionPrice * 500000m;
                            var formatted = costPer1M < 0.01m
                                ? $"{costPer1M:F4}"
                                : $"{costPer1M:F2}";
                            label += $" • ${formatted}/1M";
                        }
                    }

                    return new LabelValue { Label = label, Value = id };
                })
                .ToList();

            // Always surface free metamodel first even if the catalogue omits/renames it.
            options.RemoveAll(o => o.Value == "openrouter/free");
            options.Insert(0, new LabelValue
            {
                Label = "openrouter/free • Free metamodel (default for bulk)",
                Value = "openrouter/free"
            });

            options = options
                .OrderBy(x => x.Value switch
                {
                    "openrouter/free" => 0,
                    "openrouter/auto" => 1,
                    _ => 2
                })
                .ThenBy(x => x.Label)
                .ToList();

            return new ModelsResponse { Options = options };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching OpenRouter models");
            return new ModelsResponse { Message = ex.Message };
        }
    }

    /// <summary>
    /// Parses a pricing value from the OpenRouter API response.
    /// Pricing values are returned as strings like "0.0000005" or "-1".
    /// </summary>
    private static decimal ParsePricing(JsonElement pricing, string field)
    {
        if (!pricing.TryGetProperty(field, out var value))
            return -1m;

        if (value.ValueKind == JsonValueKind.Number)
            return value.GetDecimal();

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), 
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return -1m;
    }

    private void RecordUsage(JsonElement response)
    {
        if (!response.TryGetProperty("usage", out var usage))
        {
            return;
        }

        var inputTokens = usage.TryGetProperty("prompt_tokens", out var input)
            ? input.GetInt64()
            : 0;
        var outputTokens = usage.TryGetProperty("completion_tokens", out var output)
            ? output.GetInt64()
            : 0;
        decimal? reportedCost = null;
        if (usage.TryGetProperty("cost", out var cost))
        {
            if (cost.ValueKind == JsonValueKind.Number)
            {
                reportedCost = cost.GetDecimal();
            }
            else if (cost.ValueKind == JsonValueKind.String &&
                     decimal.TryParse(
                         cost.GetString(),
                         System.Globalization.NumberStyles.Any,
                         System.Globalization.CultureInfo.InvariantCulture,
                         out var parsed))
            {
                reportedCost = parsed;
            }
        }

        RecordUsage(_model, inputTokens, outputTokens, reportedCost);
    }
}
