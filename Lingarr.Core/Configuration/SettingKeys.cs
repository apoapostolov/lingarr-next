namespace Lingarr.Core.Configuration;

public static class SettingKeys
{
    public static class Dashboard
    {
        public const string ActivityWindowHours = "dashboard_activity_window_hours";
        public const string PrimaryLanguage = "dashboard_primary_language";
    }

    public static class Integration
    {
        public const string RadarrUrl = "radarr_url";
        public const string RadarrApiKey = "radarr_api_key";
        public const string RadarrDefaultInclude = "radarr_default_include";
        public const string SonarrUrl = "sonarr_url";
        public const string SonarrApiKey = "sonarr_api_key";
        public const string SonarrDefaultInclude = "sonarr_default_include";
        public const string RadarrSettingsCompleted = "radarr_settings_completed";
        public const string SonarrSettingsCompleted = "sonarr_settings_completed";
    }

    public static class Translation
    {
        public const string ServiceType = "service_type";
        public const string DefaultServiceType = "libretranslate";

        public static class OpenAi
        {
            public const string Model = "openai_model";
            public const string ApiKey = "openai_api_key";
            public const string RequestTemplate = "openai_request_template";
        }

        public static class Anthropic
        {
            public const string Model = "anthropic_model";
            public const string ApiKey = "anthropic_api_key";
            public const string Version = "anthropic_version";
            public const string RequestTemplate = "anthropic_request_template";
        }

        public static class LocalAi
        {
            public const string Model = "local_ai_model";
            public const string Endpoint = "local_ai_endpoint";
            public const string ApiKey = "local_ai_api_key";
            public const string ChatRequestTemplate = "local_ai_chat_request_template";
            public const string GenerateRequestTemplate = "local_ai_generate_request_template";
        }

        public static class DeepL
        {
            public const string DeeplApiKey = "deepl_api_key";
        }

        public static class Gemini
        {
            public const string Model = "gemini_model";
            public const string ApiKey = "gemini_api_key";
            public const string RequestTemplate = "gemini_request_template";
        }

        public static class DeepSeek
        {
            public const string Model = "deepseek_model";
            public const string ApiKey = "deepseek_api_key";
            public const string RequestTemplate = "deepseek_request_template";
        }


        public static class OpenRouter
        {
            public const string Model = "openrouter_model";
            public const string ApiKey = "openrouter_api_key";
            public const string Endpoint = "openrouter_endpoint";
            public const string RequestTemplate = "openrouter_request_template";
            public const string Temperature = "openrouter_temperature";
            public const string MaxTokens = "openrouter_max_tokens";
        }

        public static class Zai
        {
            public const string Model = "zai_model";
            public const string ApiKey = "zai_api_key";
            public const string Endpoint = "zai_endpoint";
            public const string RequestTemplate = "zai_request_template";
        }

        public static class OpenCodeGo
        {
            public const string Model = "opencode_go_model";
            public const string ApiKey = "opencode_go_api_key";
            public const string Endpoint = "opencode_go_endpoint";
            public const string RequestTemplate = "opencode_go_request_template";
        }

        public static class Qwen
        {
            public const string Model = "qwen_model";
            public const string ApiKey = "qwen_api_key";
            public const string Endpoint = "qwen_endpoint";
            public const string RequestTemplate = "qwen_request_template";
        }

        public static class QwenMt
        {
            public const string Model = "qwen_mt_model";
            public const string ApiKey = "qwen_api_key";
            public const string Endpoint = "qwen_endpoint";
        }

        public static class Xai
        {
            public const string Model = "xai_model";
            public const string ApiKey = "xai_api_key";
            public const string Endpoint = "xai_endpoint";
            public const string RequestTemplate = "xai_request_template";
        }

        public static class Mistral
        {
            public const string Model = "mistral_model";
            public const string ApiKey = "mistral_api_key";
            public const string Endpoint = "mistral_endpoint";
            public const string RequestTemplate = "mistral_request_template";
        }

        public static class XaiOAuth
        {
            public const string Model = "xai_oauth_model";
            public const string Endpoint = "xai_endpoint";
            public const string Connection = "xai_oauth_connection";
            public const string AccessToken = "xai_oauth_access_token";
            public const string RefreshToken = "xai_oauth_refresh_token";
            public const string ExpiresAt = "xai_oauth_expires_at";
        }

        public static class LibreTranslate
        {
            public const string Url = "libretranslate_url";
            public const string ApiKey = "libretranslate_api_key";
        }

        public const string SourceLanguages = "source_languages";
        public const string TargetLanguages = "target_languages";
        public const string AiPrompt = "ai_prompt";
        public const string AiContextPromptEnabled = "ai_context_prompt_enabled";
        public const string AiContextPrompt = "ai_context_prompt";
        public const string AiContextBefore = "ai_context_before";
        public const string AiContextAfter = "ai_context_after";
        public const string ProofreadPrompt = "proofread_prompt";
        public const string ProofreadUserPrompt = "proofread_user_prompt";
        public const string ActiveSystemPromptProfileId = "active_system_prompt_profile_id";
        public const string ActiveContextPromptProfileId = "active_context_prompt_profile_id";
        public const string FixOverlappingSubtitles = "fix_overlapping_subtitles";
        public const string StripSubtitleFormatting = "strip_subtitle_formatting";
        public const string PreserveLineBreaks = "preserve_line_breaks";
        public const string AddTranslatorInfo = "add_translator_info";
        public const string UseBatchTranslation = "use_batch_translation";
        public const string MaxBatchSize = "max_batch_size";
        public const string UseSubtitleTagging = "use_subtitle_tagging";
        public const string RemoveLanguageTag = "remove_language_tag";
        public const string SubtitleTag = "subtitle_tag";
        public const string IgnoreCaptions = "ignore_captions";
        public const string RequestTimeout = "request_timeout";

        public static string RequestTimeoutForProvider(string provider)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(provider);
            return $"{provider.Trim().ToLowerInvariant().Replace("-", "_")}_request_timeout";
        }
        public const string MaxRetries = "max_retries";
        public const string RetryDelay = "retry_delay";
        public const string RetryDelayMultiplier = "retry_delay_multiplier";
        public const string NavigateToDetailsOnRequest = "navigate_to_details_on_request";
        public const string LanguageCodeFormat = "language_code_format";
        /// <summary>When true, a later run can continue a cancelled request whose saved lines still score well.</summary>
        public const string CacheCancelledProgress = "cache_cancelled_progress";
        /// <summary>Minimum partial quality score, 0-100, required to continue a cancelled request.</summary>
        public const string CacheCancelledQualityThreshold = "cache_cancelled_quality_threshold";
        public const string TypesafeApiKey = "typesafe_api_key";
        public const string JevSkipNonDialogue = "jev_skip_non_dialogue";
        public const string JevRejectUntranslated = "jev_reject_untranslated";
    }

    public static class Automation
    {
        public const string AutomationEnabled = "automation_enabled";
        /// <summary>When false, scheduled jobs do not walk movie or episode folders.</summary>
        public const string LibraryDiskScanEnabled = "library_disk_scan_enabled";
        public const string LibraryLightSeenAt = "library_light_seen_at";
        public const string LibraryLightPlexSection = "library_light_plex_section";
        public const string LibraryLightPlexOffset = "library_light_plex_offset";
        public const string TranslationSchedule = "translation_schedule";
        public const string MaxTranslationsPerRun = "max_translations_per_run";
        public const string TranslationCycle = "translation_cycle";
        public const string MovieSchedule = "movie_schedule";
        public const string ShowSchedule = "show_schedule";
        public const string MovieAgeThreshold = "movie_age_threshold";
        public const string ShowAgeThreshold = "show_age_threshold";
        /// <summary>Durable cursor for automated movie cycle (survives restarts).</summary>
        public const string MovieProcessingIndex = "automation_movie_processing_index";
        /// <summary>Durable cursor for automated show/episode cycle (survives restarts).</summary>
        public const string ShowProcessingIndex = "automation_show_processing_index";
        public const string SubtitleNamingEnabled = "subtitle_naming_enabled";
        public const string SubtitleExtractEnabled = "subtitle_extract_enabled";
        public const string SubtitleMaintenanceSchedule = "subtitle_maintenance_schedule";
        public const string SubtitleExtractMaxPerRun = "subtitle_extract_max_per_run";
    }

    public static class MediaServers
    {
        public const string PlexUrl = "plex_url";
        public const string PlexToken = "plex_token";
        public const string PlexClientIdentifier = "plex_client_identifier";
        public const string PlexUsername = "plex_username";
        public const string PlexServerName = "plex_server_name";
        public const string PlexServerMachineId = "plex_server_machine_id";
        public const string PlexAuthMethod = "plex_auth_method";
        public const string PlexIgnoreEnvironment = "plex_ignore_environment";
        public const string PlexSetSelectedSubtitle = "plex_set_selected_subtitle";
        public const string PlexDefaultSubtitleLanguage = "plex_default_subtitle_language";
        public const string PlexTranslateOnLibraryNew = "plex_translate_on_library_new";
        public const string PlexTranslateMoviesOnLibraryNew = "plex_translate_movies_on_library_new";
        public const string PlexTranslateEpisodesOnLibraryNew = "plex_translate_episodes_on_library_new";
        public const string JellyfinUrl = "jellyfin_url";
        public const string JellyfinToken = "jellyfin_token";
    }

    public static class SubtitleValidation
    {
        public const string MaxFileSizeBytes = "subtitle_validation_maxfilesizebytes";
        public const string MaxSubtitleLength = "subtitle_validation_maxsubtitlelength";
        public const string MinSubtitleLength = "subtitle_validation_minsubtitlelength";
        public const string MinDurationMs = "subtitle_validation_mindurationms";
        public const string MaxDurationSecs = "subtitle_validation_maxdurationsecs";
        public const string ValidateSubtitles = "subtitle_validation_enabled";
    }

    public static class Authentication
    {
        public const string AuthEnabled = "auth_enabled";
        public const string ApiKey = "api_key";
        public const string OnboardingCompleted = "onboarding_completed";
    }

}
