using GTranslate.Translators;
using Lingarr.Contracts.Translation;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Services.Plugins;

namespace Lingarr.Server.Services.Translation;

public class TranslationFactory : ITranslationServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranslationFactory> _logger;

    public TranslationFactory(IServiceProvider serviceProvider,
        ILogger<TranslationFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public ITranslationService CreateTranslationService(string serviceType)
    {
        var languageCodeService = _serviceProvider.GetRequiredService<LanguageCodeService>();
        return serviceType.ToLower() switch
        {
            "libretranslate" => new LibreService(
                _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(),
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<LibreService>>(),
                languageCodeService),

            "google" => new GTranslatorService<GoogleTranslator>(
                _serviceProvider,
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                "google",
                "/app/Statics/google_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<GoogleTranslator>>(),
                languageCodeService
            ),

            "bing" => new GTranslatorService<BingTranslator>(
                _serviceProvider,
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                "bing",
                "/app/Statics/bing_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<BingTranslator>>(),
                languageCodeService
            ),

            "microsoft" => new GTranslatorService<MicrosoftTranslator>(
                _serviceProvider,
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                "microsoft",
                "/app/Statics/microsoft_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<MicrosoftTranslator>>(),
                languageCodeService
            ),

            "yandex" => new GTranslatorService<YandexTranslator>(
                _serviceProvider,
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                "yandex",
                "/app/Statics/yandex_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<YandexTranslator>>(),
                languageCodeService
            ),

            "deepl" => new DeepLService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<DeepLService>>(),
                languageCodeService
            ),

            "openai" => new OpenAiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<OpenAiService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "anthropic" => new AnthropicService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<AnthropicService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "localai" => new LocalAiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<LocalAiService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "deepseek" => new DeepSeekService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<DeepSeekService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "gemini" => new GoogleGeminiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<GoogleGeminiService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "openrouter" => new OpenRouterService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<OpenRouterService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "zai" => new ZaiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<ZaiService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "opencode-go" => new OpenCodeGoService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<OpenCodeGoService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "qwen" => new QwenService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<QwenService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "qwen-mt" => new QwenMtService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<QwenMtService>>(),
                languageCodeService
            ),

            "xai" => new XaiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<XaiService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "mistral" => new MistralService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<MistralService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "xai-oauth" => new XaiOAuthTranslationService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<XaiOAuthTranslationService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>(),
                _serviceProvider.GetRequiredService<IXaiOAuthSessionService>()
            ),

            // load any registered plugins
            _ => _serviceProvider.GetKeyedService<ITranslationService>(serviceType.ToLowerInvariant())
                 ?? throw new ArgumentException("Unsupported translation service type", nameof(serviceType))
        };
    }

    /// <inheritdoc />

    public IReadOnlyList<TranslationServiceEntry> CreateTranslationServices(IReadOnlyList<TranslationChainEntry> entries)
    {
        var services = new List<TranslationServiceEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var name = entry.ProviderNormalized;
            try
            {
                var service = CreateTranslationService(name);
                if (!string.IsNullOrWhiteSpace(entry.Model) && service is IModelOverridable overridable)
                {
                    overridable.OverrideModel(entry.Model);
                }
                if (service is IInstructionOverridable instructionOverridable)
                {
                    var context = entry.ResolvedContextPrompt;
                    var shelf = _serviceProvider.GetService<PluginShelf>();
                    if (shelf != null)
                    {
                        var block = shelf.PromptBlockAsync(
                            entry.RequestSourceLanguage,
                            entry.RequestTargetLanguage).GetAwaiter().GetResult();
                        if (!string.IsNullOrWhiteSpace(block))
                        {
                            context = string.IsNullOrWhiteSpace(context)
                                ? block
                                : context + "\n\n" + block;
                        }
                    }

                    instructionOverridable.OverrideInstructions(entry.ResolvedSystemPrompt, context);
                }
                services.Add(new TranslationServiceEntry(name, service, service as IBatchTranslationService, entry.Model));
            }
            catch (ArgumentException)
            {
                _logger.LogWarning("Skipping unknown translation service '{ServiceType}'.", name);
            }
        }
        return services;
    }

    public IReadOnlyList<TranslationServiceEntry> CreateTranslationServices(IReadOnlyList<string> serviceTypes)
    {
        var entries = serviceTypes
            .Select(name => new TranslationChainEntry { Provider = name })
            .ToList();
        return CreateTranslationServices(entries);
    }
}
