using Lingarr.Contracts.Plugins;

namespace Lingarr.Server.Services.SubtitleDownload;

/// <summary>
/// Subtitle providers ported from Bazarr's ProviderList
/// (frontend/src/pages/Settings/Providers/list.tsx).
/// </summary>
public static class SubtitleDownloadCatalog
{
    public static IReadOnlyList<SubtitleProviderDefinition> All { get; } =
    [
        new()
        {
            Id = "animekalesi",
            DisplayName = "AnimeKalesi",
            Description = "Turkish Anime Series Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = false,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "animesubinfo",
            DisplayName = "AnimeSub.info",
            Description = "Polish Anime Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "bayflix",
            DisplayName = "Bayflix",
            Description = "Bulgarian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "assrt",
            DisplayName = "Assrt",
            Description = "Chinese Subtitles Provider",
            Kind = SubtitleDownloadKind.Assrt,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "token",
                    Label = "Token",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
            ]
        },
        new()
        {
            Id = "betaseries",
            DisplayName = "BetaSeries",
            Description = "French / English Provider for TV Shows Only",
            Kind = SubtitleDownloadKind.BetaSeries,
            SupportsMovies = false,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "token",
                    Label = "API KEY",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
            ]
        },
        new()
        {
            Id = "gestdown",
            DisplayName = "Gestdown (Addic7ed proxy)",
            Description = "Proxy for Addic7ed website. No need for login or cookies.",
            Kind = SubtitleDownloadKind.Gestdown,
            SupportsMovies = false,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "greeksubs",
            DisplayName = "GreekSubs",
            Description = "Greek Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "greeksubtitles",
            DisplayName = "GreekSubtitles",
            Description = "Greek Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "ktuvit",
            DisplayName = "Ktuvit",
            Description = "Hebrew Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "email",
                    Label = "Email",
                    Type = PluginSettingType.Text,
                    Required = true,
                },
                new()
                {
                    Key = "hashed_password",
                    Label = "Hashed password",
                    Type = PluginSettingType.Secret,
                    Required = false,
                    Description = "Hashed Password",
                },
            ]
        },
        new()
        {
            Id = "pipocas",
            DisplayName = "Pipocas.tv",
            Description = "Portuguese / Brazilian / English / Spanish Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "username",
                    Label = "Username",
                    Type = PluginSettingType.Text,
                    Required = true,
                },
                new()
                {
                    Key = "password",
                    Label = "Password",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
            ]
        },
        new()
        {
            Id = "legendasdivx",
            DisplayName = "LegendasDivx",
            Description = "Brazilian / Portuguese Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "username",
                    Label = "Username",
                    Type = PluginSettingType.Text,
                    Required = true,
                },
                new()
                {
                    Key = "password",
                    Label = "Password",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
                new()
                {
                    Key = "skip_wrong_fps",
                    Label = "Skip wrong FPS",
                    Type = PluginSettingType.Toggle,
                    Required = false,
                    Description = "Skip Wrong FPS",
                },
            ]
        },
        new()
        {
            Id = "legendasnet",
            DisplayName = "Legendas.net",
            Description = "Brazilian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "username",
                    Label = "Username",
                    Type = PluginSettingType.Text,
                    Required = true,
                },
                new()
                {
                    Key = "password",
                    Label = "Password",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
            ]
        },
        new()
        {
            Id = "napisy24",
            DisplayName = "Napisy24",
            Description = "Polish Subtitles Provider",
            Kind = SubtitleDownloadKind.Napisy24,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "username",
                    Label = "Username",
                    Type = PluginSettingType.Text,
                    Required = false,
                },
                new()
                {
                    Key = "password",
                    Label = "Password",
                    Type = PluginSettingType.Secret,
                    Required = false,
                },
            ]
        },
        new()
        {
            Id = "opensubtitlescom",
            DisplayName = "OpenSubtitles.com",
            Description = null,
            Kind = SubtitleDownloadKind.OpenSubtitlesCom,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "username",
                    Label = "Username",
                    Type = PluginSettingType.Text,
                    Required = true,
                },
                new()
                {
                    Key = "password",
                    Label = "Password",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
                new()
                {
                    Key = "use_hash",
                    Label = "Use file hash",
                    Type = PluginSettingType.Toggle,
                    Required = false,
                    Description = "Use Hash",
                },
                new()
                {
                    Key = "include_ai_translated",
                    Label = "Include AI translations",
                    Type = PluginSettingType.Toggle,
                    Required = false,
                    Description = "Include subtitles generated by opensubtitles.com's AI translation service in search results",
                },
                new()
                {
                    Key = "include_machine_translated",
                    Label = "Include machine translations",
                    Type = PluginSettingType.Toggle,
                    Required = false,
                    Description = "Include subtitles machine translated by users in search results",
                },
                new()
                {
                    Key = "api_key",
                    Label = "API key",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
            ]
        },
        new()
        {
            Id = "prijevodionline",
            DisplayName = "Prijevodi Online",
            Description = "Prijevodi Online website. No need for login or cookies.",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = false,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "regielive",
            DisplayName = "RegieLive",
            Description = "Romanian Subtitles Provider",
            Kind = SubtitleDownloadKind.RegieLive,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "soustitreseu",
            DisplayName = "Sous-Titres.eu",
            Description = "Mostly French Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "subclub",
            DisplayName = "SubClub.eu",
            Description = "Estonian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "subdl",
            DisplayName = "SubDL",
            Description = null,
            Kind = SubtitleDownloadKind.Subdl,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "api_key",
                    Label = "API key",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
                new()
                {
                    Key = "ai_translate",
                    Label = "AI-translate missing languages",
                    Type = PluginSettingType.Toggle,
                    Required = false,
                    Default = "false",
                    Description = "AI-translate missing languages (requires SubDL Plus or Pro; also includes AI-translated subtitles in results)",
                },
                new()
                {
                    Key = "include_ai_translated",
                    Label = "Include AI translations",
                    Type = PluginSettingType.Toggle,
                    Required = false,
                    Default = "false",
                    Description = "Include AI-translated subtitles in search results",
                },
            ]
        },
        new()
        {
            Id = "subf2m",
            DisplayName = "subf2m.co",
            Description = null,
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "verify_ssl",
                    Label = "Verify SSL",
                    Type = PluginSettingType.Toggle,
                    Required = false,
                    Default = "true",
                },
                new()
                {
                    Key = "user_agent",
                    Label = "User-Agent",
                    Type = PluginSettingType.Text,
                    Required = false,
                    Description = "User-agent header",
                },
            ]
        },
        new()
        {
            Id = "subsource",
            DisplayName = "subsource.net",
            Description = null,
            Kind = SubtitleDownloadKind.Subsource,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "apikey",
                    Label = "API key",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
            ]
        },
        new()
        {
            Id = "subsarr",
            DisplayName = "Subsarr",
            Description = "Self-hosted Subscene subtitle provider. (requires slimcdk/subsarr https://github.com/slimcdk/subsarr)",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "base_url",
                    Label = "Address",
                    Type = PluginSettingType.Url,
                    Required = false,
                    Description = "Base URL",
                },
            ]
        },
        new()
        {
            Id = "subsdump",
            DisplayName = "SubsDump",
            Description = "Connect Bazarr to a self-hosted SubsDump instance for fast subtitle search and downloads from your local Subscene archive.",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "base_url",
                    Label = "Address",
                    Type = PluginSettingType.Url,
                    Required = false,
                    Description = "Base URL",
                },
                new()
                {
                    Key = "api_key",
                    Label = "API key",
                    Type = PluginSettingType.Secret,
                    Required = true,
                    Description = "API Key (optional)",
                },
            ]
        },
        new()
        {
            Id = "subssabbz",
            DisplayName = "Subs.sab.bz",
            Description = "Bulgarian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "subsro",
            DisplayName = "Subs.ro",
            Description = "Romanian Subtitles Provider",
            Kind = SubtitleDownloadKind.SubsRo,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "api_key",
                    Label = "API key",
                    Type = PluginSettingType.Secret,
                    Required = true,
                    Description = "API Key",
                },
            ]
        },
        new()
        {
            Id = "subsunacs",
            DisplayName = "Subsunacs.net",
            Description = "Bulgarian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "subtis",
            DisplayName = "Subtis",
            Description = "Spanish Subtitles Provider for Movies",
            Kind = SubtitleDownloadKind.Subtis,
            SupportsMovies = true,
            SupportsEpisodes = false,
            Fields = []
        },
        new()
        {
            Id = "subtitrarinoi",
            DisplayName = "Subtitrari-noi.ro",
            Description = "Romanian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "subtitriid",
            DisplayName = "subtitri.id.lv",
            Description = "Latvian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "subtitulamostv",
            DisplayName = "Subtitulamos.tv",
            Description = "Spanish Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "subx",
            DisplayName = "SubX",
            Description = "SubX API is a search and download API for Subdivx, a LatAm Spanish / Spanish Subtitles Provider",
            Kind = SubtitleDownloadKind.SubX,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "api_key",
                    Label = "API key",
                    Type = PluginSettingType.Secret,
                    Required = true,
                    Description = "API Key",
                },
            ]
        },
        new()
        {
            Id = "subtitlecat",
            DisplayName = "Subtitle Cat",
            Description = "Multi-language Subtitles Provider",
            Kind = SubtitleDownloadKind.SubtitleCat,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "include_machine_translated",
                    Label = "Include machine translations",
                    Type = PluginSettingType.Toggle,
                    Required = false,
                    Description = "Include subtitles machine translated by SubtitleCat in search results",
                },
            ]
        },
        new()
        {
            Id = "titlovi",
            DisplayName = "Titlovi",
            Description = null,
            Kind = SubtitleDownloadKind.Titlovi,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "username",
                    Label = "Username",
                    Type = PluginSettingType.Text,
                    Required = false,
                },
                new()
                {
                    Key = "password",
                    Label = "Password",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
            ]
        },
        new()
        {
            Id = "titrari",
            DisplayName = "Titrari.ro",
            Description = "Mostly Romanian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "titulky",
            DisplayName = "Titulky.com",
            Description = "CZ/SK Subtitles Provider. Available only with VIP.",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "username",
                    Label = "Username",
                    Type = PluginSettingType.Text,
                    Required = true,
                },
                new()
                {
                    Key = "password",
                    Label = "Password",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
                new()
                {
                    Key = "approved_only",
                    Label = "Approved only",
                    Type = PluginSettingType.Toggle,
                    Required = false,
                    Description = "Skip unapproved subtitles",
                },
                new()
                {
                    Key = "skip_wrong_fps",
                    Label = "Skip wrong FPS",
                    Type = PluginSettingType.Toggle,
                    Required = false,
                    Description = "Skip subtitles with mismatched fps to video's",
                },
            ]
        },
        new()
        {
            Id = "turkcealtyaziorg",
            DisplayName = "Turkcealtyazi.org",
            Description = "Turkish Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "cookies",
                    Label = "Cookies",
                    Type = PluginSettingType.Secret,
                    Required = false,
                    Description = "Cookies, e.g., PHPSESSID=abc; wikisubtitlesuser=xyz; wikisubtitlespass=efg",
                },
                new()
                {
                    Key = "user_agent",
                    Label = "User-Agent",
                    Type = PluginSettingType.Text,
                    Required = false,
                    Description = "User-Agent, e.g., Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:95.0) Gecko/20100101 Firefox/95.0",
                },
            ]
        },
        new()
        {
            Id = "xsubs",
            DisplayName = "XSubs",
            Description = "Greek Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields =
            [
                new()
                {
                    Key = "username",
                    Label = "Username",
                    Type = PluginSettingType.Text,
                    Required = true,
                },
                new()
                {
                    Key = "password",
                    Label = "Password",
                    Type = PluginSettingType.Secret,
                    Required = true,
                },
            ]
        },
        new()
        {
            Id = "yavkanet",
            DisplayName = "Yavka.net",
            Description = "Bulgarian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "vladoonmooo",
            DisplayName = "Vladoon Mooo",
            Description = "Bulgarian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "hosszupuska",
            DisplayName = "Hosszupuska",
            Description = "Hungarian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = false,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "nekur",
            DisplayName = "Nekur",
            Description = "Latvian Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = false,
            Fields = []
        },
        new()
        {
            Id = "subscenter",
            DisplayName = "Subscenter",
            Description = "Hebrew Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "subsynchro",
            DisplayName = "SubSynchro",
            Description = "French Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "supersubtitles",
            DisplayName = "Supersubtitles",
            Description = null,
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "tvsubtitles",
            DisplayName = "TVSubtitles",
            Description = null,
            Kind = SubtitleDownloadKind.TvSubtitles,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "wizdom",
            DisplayName = "Wizdom.xyz",
            Description = "Wizdom.xyz Subtitles Provider",
            Kind = SubtitleDownloadKind.HtmlSearch,
            SupportsMovies = true,
            SupportsEpisodes = true,
            Fields = []
        },
        new()
        {
            Id = "yifysubtitles",
            DisplayName = "YIFY Subtitles",
            Description = null,
            Kind = SubtitleDownloadKind.Yify,
            SupportsMovies = true,
            SupportsEpisodes = false,
            Fields = []
        }
    ];

    public static SubtitleProviderDefinition? Find(string id) =>
        All.FirstOrDefault(provider =>
            string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));
}
