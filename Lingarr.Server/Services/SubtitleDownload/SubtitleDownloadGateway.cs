using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.SubtitleDownload;

public sealed class SubtitleDownloadGateway : ISubtitleDownloadGateway
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex HrefRegex = new(
        @"href\s*=\s*[""']([^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"\((19|20)\d{2}\)|[.\s]((19|20)\d{2})[.\s]", RegexOptions.Compiled);
    private static readonly Regex EpisodeTokenRegex = new(@"s(?<season>\d{1,2})e(?<episode>\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] LanguageMarkerCodes =
    [
        "en", "eng", "english",
        "bg", "bul", "bulgarian",
        "fr", "fra", "fre", "french",
        "de", "deu", "ger", "german",
        "es", "spa", "spanish",
        "pt", "por", "portuguese",
        "it", "ita", "italian",
        "ar", "ara", "arabic",
        "ru", "rus", "russian",
        "nl", "nld", "dutch",
        "pl", "pol", "polish",
        "tr", "tur", "turkish",
        "he", "heb", "hebrew",
        "el", "ell", "gre", "greek",
        "hu", "hun", "hungarian",
        "cs", "ces", "cze", "czech",
        "ro", "ron", "rum", "romanian",
        "uk", "ukr", "ukrainian",
        "zh", "zho", "chi", "chinese",
        "ja", "jpn", "japanese",
        "ko", "kor", "korean",
        "sv", "swe", "swedish",
        "fi", "fin", "finnish",
        "da", "dan", "danish",
        "no", "nor", "norwegian"
    ];

    private static readonly Dictionary<string, string> SearchPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["animekalesi"] = "https://www.animekalesi.com/?s={query}",
        ["animesubinfo"] = "http://animesub.info/szukaj.php?szukane={query}",
        ["bayflix"] = "https://bayflix.sb/?s={query}",
        ["greeksubs"] = "https://greeksubs.net/search/{query}",
        ["greeksubtitles"] = "http://gr.greek-subtitles.com/search.php?q={query}",
        ["hosszupuska"] = "http://hosszupuskasub.com/?s={query}",
        ["ktuvit"] = "https://www.ktuvit.me/Search.aspx?search={query}",
        ["legendasdivx"] = "https://www.legendasdivx.pt/modules.php?name=Downloads&d_op=search&query={query}",
        ["legendasnet"] = "https://legendas.net/?s={query}",
        ["nekur"] = "http://subtitri.nekur.net/?s={query}",
        ["pipocas"] = "https://pipocas.tv/subtitles?search={query}",
        ["prijevodionline"] = "https://www.prijevodi-online.org/?s={query}",
        ["soustitreseu"] = "https://www.sous-titres.eu/search.html?q={query}",
        ["subclub"] = "https://www.subclub.eu/index.php?act=search&q={query}",
        ["subf2m"] = "https://subf2m.co/subtitles/searchbytitle?query={query}",
        ["subscenter"] = "http://www.subscenter.info/he/?s={query}",
        ["subssabbz"] = "http://subs.sab.bz/index.php?act=search&movie={query}",
        ["subsunacs"] = "https://subsunacs.net/search.php?m={query}",
        ["subsynchro"] = "https://www.subsynchro.com/?s={query}",
        ["subtitrarinoi"] = "https://www.subtitrari-noi.ro/?s={query}",
        ["subtitriid"] = "https://subtitri.do.am/?s={query}",
        ["subtitulamostv"] = "https://www.subtitulamos.tv/search?q={query}",
        ["supersubtitles"] = "https://www.feliratok.eu/index.php?search={query}",
        ["titrari"] = "https://www.titrari.ro/?s={query}",
        ["titulky"] = "https://premium.titulky.com/?fulltext={query}",
        ["turkcealtyaziorg"] = "https://turkcealtyazi.org/find.php?q={query}",
        ["xsubs"] = "http://xsubs.tv/list.php?q={query}",
        ["yavkanet"] = "https://yavka.net/?s={query}",
        ["vladoonmooo"] = "https://vladoon.mooo.com/subs?q={query}",
        ["subsarr"] = "{base_url}/search?q={query}",
        ["subsdump"] = "{base_url}/api/search?q={query}",
        ["wizdom"] = "https://wizdom.xyz/search?q={query}"
    };

    private readonly IHttpClientFactory _http;
    private readonly ISettingService _settings;
    private readonly ILogger<SubtitleDownloadGateway> _logger;

    public SubtitleDownloadGateway(
        IHttpClientFactory http,
        ISettingService settings,
        ILogger<SubtitleDownloadGateway> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public async Task<SubtitleDownloadHit?> DownloadAsync(
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken)
    {
        if (query.MediaType == MediaType.Movie && !provider.SupportsMovies)
        {
            return null;
        }

        if (query.MediaType == MediaType.Episode && !provider.SupportsEpisodes)
        {
            return null;
        }

        try
        {
            var hit = await (provider.Kind switch
            {
                SubtitleDownloadKind.OpenSubtitlesCom => OpenSubtitlesComAsync(provider, query, cancellationToken),
                SubtitleDownloadKind.Subdl => SubdlAsync(provider, query, cancellationToken),
                SubtitleDownloadKind.Gestdown => GestdownAsync(query, cancellationToken),
                SubtitleDownloadKind.Assrt => AssrtAsync(provider, query, cancellationToken),
                SubtitleDownloadKind.BetaSeries => BetaSeriesAsync(provider, query, cancellationToken),
                SubtitleDownloadKind.Subsource => JsonSearchAsync(
                    provider, query, "https://api.subsource.net/api/v1/subtitles", "apikey", cancellationToken),
                SubtitleDownloadKind.SubsRo => JsonSearchAsync(
                    provider, query, "https://api.subs.ro/v1.0/search", "api_key", cancellationToken),
                SubtitleDownloadKind.SubX => JsonSearchAsync(
                    provider, query, "https://subx-api.duckdns.org/search", "api_key", cancellationToken),
                SubtitleDownloadKind.Titlovi => TitloviAsync(provider, query, cancellationToken),
                SubtitleDownloadKind.Napisy24 => Napisy24Async(provider, query, cancellationToken),
                SubtitleDownloadKind.SubtitleCat => SubtitleCatAsync(provider, query, cancellationToken),
                SubtitleDownloadKind.Yify => HtmlSearchAsync("https://yifysubtitles.ch/search?q={query}", provider, query, cancellationToken),
                SubtitleDownloadKind.TvSubtitles => HtmlSearchAsync("https://www.tvsubtitles.net/search.php?q={query}", provider, query, cancellationToken),
                SubtitleDownloadKind.Subtis => JsonSearchAsync(
                    provider, query, "https://api.subt.is/v1/search", null, cancellationToken),
                SubtitleDownloadKind.RegieLive => JsonSearchAsync(
                    provider, query, "https://api.regielive.ro/bazarr/search.php", null, cancellationToken),
                _ => HtmlSearchAsync(SearchPages.GetValueOrDefault(provider.Id), provider, query, cancellationToken)
            });
            if (hit != null && SubtitleBytes.MismatchesLanguage(hit.Content, query.Language))
            {
                _logger.LogInformation(
                    "{Provider} returned a subtitle that is not {Language} for {Title}.",
                    provider.DisplayName,
                    query.Language,
                    query.Title);
                return null;
            }

            return hit;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            // Exception messages and request URIs can contain API keys or passwords.
            _logger.LogWarning("Subtitle provider {Provider} search failed for {Title}: {Failure} (HTTP {Status}).",
                provider.DisplayName, query.Title, exception.GetType().Name,
                exception is HttpRequestException http ? (int?)http.StatusCode : null);
            return null;
        }
    }

    internal static string DecodeText(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset).GetString(bytes);
            }
            catch (ArgumentException)
            {
                if (charset.StartsWith("iso-8859", StringComparison.OrdinalIgnoreCase)
                    || charset.StartsWith("windows-", StringComparison.OrdinalIgnoreCase)
                    || charset.Equals("latin1", StringComparison.OrdinalIgnoreCase)
                    || charset.Equals("latin-1", StringComparison.OrdinalIgnoreCase))
                {
                    return Encoding.Latin1.GetString(bytes);
                }
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<string> ReadTextAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return DecodeText(bytes, response.Content.Headers.ContentType?.CharSet);
    }

    private async Task<string?> Field(SubtitleProviderDefinition provider, string key, bool secret)
    {
        var settingKey = provider.SettingKey(key);
        var value = secret
            ? await _settings.GetEncryptedSetting(settingKey)
            : await _settings.GetSetting(settingKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            var field = provider.Fields.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
            value = field?.Default;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private HttpClient Client()
    {
        var client = _http.CreateClient("subtitle-download");
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LingarrNext/1.1.6");
        }

        return client;
    }

    internal static string QueryTitle(SubtitleDownloadQuery query)
    {
        if (query.MediaType == MediaType.Episode)
        {
            return $"{SearchTitle(query.SeriesTitle ?? query.Title)} S{query.SeasonNumber:00}E{query.EpisodeNumber:00}";
        }

        var title = SearchTitle(query.Title);
        var year = YearOf(query);
        return year == null ? title : $"{title} {year}";
    }

    internal static string SearchTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "";
        }

        var builder = new StringBuilder(title.Length + 8);
        var spaced = true;
        foreach (var ch in title.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark
                || ch is '\'' or '\u2019' or '\u02BC')
            {
                continue;
            }

            if (ch == '&')
            {
                if (!spaced)
                {
                    builder.Append(' ');
                }

                builder.Append("and");
                spaced = false;
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                spaced = false;
                continue;
            }

            if (!spaced)
            {
                builder.Append(' ');
                spaced = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    internal static string FoldSearchText(string? text) => SearchTitle(text).ToLowerInvariant();

    internal static int? YearOf(SubtitleDownloadQuery query)
    {
        if (query.Year is > 1900)
        {
            return query.Year;
        }

        foreach (var text in new[] { query.FileName, query.Title })
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var match = YearRegex.Match(text);
            if (!match.Success)
            {
                continue;
            }

            var raw = match.Groups[1].Success ? match.Value.Trim('(', ')') : match.Groups[2].Value;
            if (int.TryParse(raw, out var year) && year is >= 1900 and <= 2100)
            {
                return year;
            }
        }

        return null;
    }

    private static string Lang3(string language)
    {
        var code = language.Trim().ToLowerInvariant();
        return code switch
        {
            "en" or "eng" => "eng",
            "bg" or "bul" => "bul",
            "fr" or "fra" or "fre" => "fra",
            "de" or "deu" or "ger" => "deu",
            "es" or "spa" => "spa",
            "pt" or "por" => "por",
            "it" or "ita" => "ita",
            "ru" or "rus" => "rus",
            "pl" or "pol" => "pol",
            "nl" or "nld" => "nld",
            "tr" or "tur" => "tur",
            "he" or "heb" => "heb",
            "el" or "ell" or "gre" => "ell",
            "hu" or "hun" => "hun",
            "cs" or "ces" or "cze" => "ces",
            "ro" or "ron" or "rum" => "ron",
            "uk" or "ukr" => "ukr",
            "zh" or "zho" or "chi" => "zho",
            "ja" or "jpn" => "jpn",
            "ko" or "kor" => "kor",
            "ar" or "ara" => "ara",
            "sv" or "swe" => "swe",
            "fi" or "fin" => "fin",
            "da" or "dan" => "dan",
            "no" or "nor" => "nor",
            _ => code.Length == 2 ? code : code[..Math.Min(3, code.Length)]
        };
    }

    private async Task<SubtitleDownloadHit?> FetchBytes(
        string provider,
        Uri uri,
        string? referer,
        CancellationToken cancellationToken,
        string? language = null,
        string? cookies = null,
        string? userAgent = null,
        Uri? credentialOrigin = null)
    {
        var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyHtmlHeaders(request, cookies, userAgent, credentialOrigin);
        if (!string.IsNullOrWhiteSpace(referer))
        {
            request.Headers.Referrer = new Uri(referer);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Subtitle provider {Provider} download failed on {Host}: HTTP {Status}.",
                provider, uri.Host, (int)response.StatusCode);
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length < 32)
        {
            return null;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = response.Content.Headers.ContentDisposition?.FileNameStar
                   ?? response.Content.Headers.ContentDisposition?.FileName
                   ?? Path.GetFileName(uri.AbsolutePath);
        var unpacked = SubtitleArchive.Unpack(bytes, name, out var extension, language);
        if (SubtitleBytes.IsHtml(unpacked))
        {
            return null;
        }

        return new SubtitleDownloadHit
        {
            Provider = provider,
            Content = unpacked,
            FileName = string.IsNullOrWhiteSpace(name) ? $"subtitle{extension}" : name.Trim('"'),
            Extension = extension
        };
    }

    private async Task<SubtitleDownloadHit?> SubtitleCatAsync(
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken)
    {
        var includeMachine = !string.Equals(
            await Field(provider, "include_machine_translated", false),
            "false",
            StringComparison.OrdinalIgnoreCase);
        if (!includeMachine && query.Language is not ("en" or "eng" or "english"))
        {
            return null;
        }

        return await HtmlSearchAsync(
            "https://www.subtitlecat.com/index.php?search={query}",
            provider,
            query,
            cancellationToken);
    }

    private async Task<SubtitleDownloadHit?> HtmlSearchAsync(
        string? template,
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var baseUrl = await Field(provider, "base_url", false);
        var page = template
            .Replace("{query}", Uri.EscapeDataString(QueryTitle(query)))
            .Replace("{base_url}", (baseUrl ?? "").TrimEnd('/'));
        var cookies = await Field(provider, "cookies", true);
        var userAgent = await Field(provider, "user_agent", false);
        var pageUri = new Uri(page);
        var html = await GetHtml(provider.Id, page, cookies, userAgent, cancellationToken, pageUri);
        if (html == null)
        {
            return null;
        }

        var ranked = RankLinks(html, pageUri, query);
        foreach (var candidate in ranked.Take(8))
        {
            if (IsSubtitleFile(candidate.AbsolutePath))
            {
                if (!HrefMatchesLanguage(candidate.AbsolutePath, query.Language)
                    && !HrefMatchesLanguage(candidate.OriginalString, query.Language))
                {
                    continue;
                }

                var hit = await FetchBytes(provider.Id, candidate, page, cancellationToken, query.Language,
                    cookies, userAgent, pageUri);
                if (KeepHit(hit, provider, query))
                {
                    return hit;
                }

                continue;
            }

            var followed = await FollowHtmlPage(provider, candidate, query, cancellationToken,
                cookies, userAgent, pageUri);
            if (followed != null)
            {
                return followed;
            }
        }

        return null;
    }

    private async Task<SubtitleDownloadHit?> FollowHtmlPage(
        SubtitleProviderDefinition provider,
        Uri page,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken,
        string? cookies,
        string? userAgent,
        Uri credentialOrigin)
    {
        var html = await GetHtml(provider.Id, page.AbsoluteUri, cookies, userAgent, cancellationToken, credentialOrigin);
        if (html == null)
        {
            return null;
        }

        foreach (var file in RankLinks(html, page, query))
        {
            if (!IsSubtitleFile(file.AbsolutePath) && !IsSubtitleFile(file.OriginalString))
            {
                continue;
            }

            if (!HrefMatchesLanguage(file.AbsolutePath, query.Language)
                && !HrefMatchesLanguage(file.OriginalString, query.Language))
            {
                continue;
            }

            var hit = await FetchBytes(provider.Id, file, page.ToString(), cancellationToken, query.Language,
                cookies, userAgent, credentialOrigin);
            if (KeepHit(hit, provider, query))
            {
                return hit;
            }
        }

        return null;
    }

    private async Task<string?> GetHtml(
        string provider,
        string page,
        string? cookies,
        string? userAgent,
        CancellationToken cancellationToken,
        Uri? credentialOrigin = null)
    {
        var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, page);
        ApplyHtmlHeaders(request, cookies, userAgent, credentialOrigin);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Subtitle provider {Provider} page failed on {Host}: HTTP {Status}.",
                provider, request.RequestUri!.Host, (int)response.StatusCode);
            return null;
        }
        return await ReadTextAsync(response, cancellationToken);
    }

    private static void ApplyHtmlHeaders(HttpRequestMessage request, string? cookies,
        string? userAgent, Uri? credentialOrigin)
    {
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(userAgent);
        }

        if (!string.IsNullOrWhiteSpace(cookies)
            && credentialOrigin != null
            && request.RequestUri?.GetLeftPart(UriPartial.Authority) == credentialOrigin.GetLeftPart(UriPartial.Authority))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookies);
        }
    }

    private bool KeepHit(SubtitleDownloadHit? hit, SubtitleProviderDefinition provider, SubtitleDownloadQuery query)
    {
        if (hit == null)
        {
            return false;
        }

        if (!SubtitleBytes.MismatchesLanguage(hit.Content, query.Language))
        {
            return true;
        }

        _logger.LogInformation(
            "{Provider} returned a subtitle that is not {Language} for {Title}.",
            provider.DisplayName,
            query.Language,
            query.Title);
        return false;
    }

    internal static string EncodeHref(string href)
    {
        var builder = new StringBuilder(href.Length + 8);
        foreach (var ch in href.Trim())
        {
            builder.Append(ch switch
            {
                ' ' => "%20",
                '[' => "%5B",
                ']' => "%5D",
                _ => ch.ToString()
            });
        }

        return builder.ToString();
    }

    internal static bool HrefMatchesLanguage(string href, string language)
    {
        var lower = href.ToLowerInvariant();
        if (HasLanguageMarker(lower, language))
        {
            return true;
        }

        return !HasAnyLanguageMarker(lower);
    }

    private static bool HasLanguageMarker(string lower, string language)
    {
        var code = language.Trim().ToLowerInvariant();
        var lang3 = Lang3(language);
        if (HasCodeMarker(lower, code) || HasCodeMarker(lower, lang3))
        {
            return true;
        }

        return code switch
        {
            "en" or "eng" => lower.Contains("english"),
            "bg" or "bul" => lower.Contains("bulgarian"),
            "fr" or "fra" or "fre" => lower.Contains("french"),
            "de" or "deu" or "ger" => lower.Contains("german"),
            "es" or "spa" => lower.Contains("spanish"),
            "it" or "ita" => lower.Contains("italian"),
            "pt" or "por" => lower.Contains("portuguese"),
            "ar" or "ara" => lower.Contains("arabic"),
            _ => false
        };
    }

    private static bool HasAnyLanguageMarker(string lower)
    {
        foreach (var code in LanguageMarkerCodes)
        {
            if (HasCodeMarker(lower, code) || (code.Length > 3 && lower.Contains(code)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCodeMarker(string lower, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        return lower.Contains($"-{code}.")
            || lower.Contains($".{code}.")
            || lower.Contains($"_{code}.")
            || lower.Contains($"/{code}/");
    }

    private static bool IsSubtitleFile(string href)
    {
        var lower = href.ToLowerInvariant();
        return lower.EndsWith(".srt")
            || lower.EndsWith(".ass")
            || lower.EndsWith(".ssa")
            || lower.EndsWith(".zip")
            || lower.EndsWith(".rar")
            || lower.Contains("/download");
    }

    private static bool IsResultPage(string href)
    {
        var lower = href.ToLowerInvariant().Replace('\\', '/');
        return lower.Contains("/subs/")
            || lower.StartsWith("subs/")
            || lower.Contains("/subtitles/")
            || lower.StartsWith("subtitles/")
            || lower.Contains("/movie-imdb")
            || lower.Contains("/movie/");
    }

    internal static List<Uri> RankLinks(string html, Uri page, SubtitleDownloadQuery query)
    {
        var ranked = new List<(int Score, Uri Uri)>();
        foreach (Match match in HrefRegex.Matches(html))
        {
            var href = EncodeHref(System.Net.WebUtility.HtmlDecode(match.Groups[1].Value));
            if (href.StartsWith("javascript", StringComparison.OrdinalIgnoreCase) || href == "#")
            {
                continue;
            }

            if (!Uri.TryCreate(page, href, out var absolute))
            {
                continue;
            }

            var lower = href.ToLowerInvariant();
            var path = absolute.AbsolutePath;
            if (!IsSubtitleFile(lower)
                && !IsSubtitleFile(path)
                && !IsResultPage(lower)
                && !IsResultPage(path)
                && !lower.Contains("download"))
            {
                continue;
            }

            var at = html.IndexOf(match.Value, StringComparison.OrdinalIgnoreCase);
            var window = at >= 0
                ? html.Substring(Math.Max(0, at - 160), Math.Min(html.Length - Math.Max(0, at - 160), 360)).ToLowerInvariant()
                : "";
            var score = ScoreCandidate(lower + " " + absolute.AbsolutePath, window, query);
            if (score < 3)
            {
                continue;
            }

            ranked.Add((score, absolute));
        }

        return ranked
            .OrderByDescending(item => item.Score)
            .Select(item => item.Uri)
            .Distinct()
            .ToList();
    }

    internal static int ScoreCandidate(string href, string window, SubtitleDownloadQuery query)
    {
        var hay = (href + " " + window).ToLowerInvariant();
        var score = 0;
        var title = FoldSearchText(
            query.MediaType == MediaType.Episode
                ? query.SeriesTitle ?? query.Title
                : query.Title);
        var year = YearOf(query);
        var episodeKey = EpisodeKey(query);
        if (episodeKey != null && HasOtherEpisode(hay, episodeKey))
        {
            return 0;
        }

        var shortAmbiguous = title.IndexOf(' ') < 0 && title.Length < 5;
        var titleHay = FoldSearchText(shortAmbiguous ? href : hay);
        var titleMatched = title.Length > 0 && TitleMatches(titleHay, title, year, episodeKey != null);
        if (titleMatched)
        {
            score += 3;
        }

        if (titleMatched && episodeKey != null && hay.Contains(episodeKey, StringComparison.Ordinal))
        {
            score += 3;
        }

        if (year != null && hay.Contains(year.Value.ToString()))
        {
            score += 2;
        }

        if (query.FileName != null)
        {
            if (query.FileName.Contains("YTS", StringComparison.OrdinalIgnoreCase) && hay.Contains("yts"))
            {
                score += 2;
            }

            if (query.FileName.Contains("WEBRip", StringComparison.OrdinalIgnoreCase) && hay.Contains("webrip"))
            {
                score += 1;
            }
        }

        if (IsSubtitleFile(href))
        {
            score += 5;
        }

        return score;
    }

    private static string? EpisodeKey(SubtitleDownloadQuery query)
    {
        if (query.MediaType != MediaType.Episode || query.SeasonNumber == null || query.EpisodeNumber == null)
        {
            return null;
        }

        return $"s{query.SeasonNumber.Value:00}e{query.EpisodeNumber.Value:00}";
    }

    private static bool HasOtherEpisode(string hay, string wanted)
    {
        foreach (Match match in EpisodeTokenRegex.Matches(hay))
        {
            var token = $"s{int.Parse(match.Groups["season"].Value):00}e{int.Parse(match.Groups["episode"].Value):00}";
            if (token != wanted)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TitleMatches(string foldedHay, string title, int? year, bool hasEpisode)
    {
        var padded = " " + foldedHay + " ";
        var shortAmbiguous = title.IndexOf(' ') < 0 && title.Length < 5;
        if (shortAmbiguous && year == null && !hasEpisode)
        {
            return false;
        }

        var needle = shortAmbiguous && year != null && !hasEpisode
            ? " " + title + " " + year.Value + " "
            : " " + title + " ";
        var start = 0;
        while (true)
        {
            var at = padded.IndexOf(needle, start, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            if (!shortAmbiguous)
            {
                return true;
            }

            var before = TokenBefore(padded, at);
            if (before.Length == 0 || IsAllDigits(before) || IsArticle(before))
            {
                return true;
            }

            start = at + 1;
        }
    }

    private static bool IsArticle(string token) => token is "the" or "a" or "an";

    private static string TokenBefore(string padded, int needleAt)
    {
        var end = needleAt;
        var start = end;
        while (start > 0 && padded[start - 1] != ' ')
        {
            start--;
        }

        return padded[start..end];
    }

    private static bool IsAllDigits(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (!char.IsDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<SubtitleDownloadHit?> OpenSubtitlesComAsync(
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken)
    {
        var apiKey = await Field(provider, "api_key", true);
        var username = await Field(provider, "username", false);
        var password = await Field(provider, "password", true);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var client = Client();
        using var login = new HttpRequestMessage(HttpMethod.Post, "https://api.opensubtitles.com/api/v1/login")
        {
            Content = JsonContent(new { username, password })
        };
        login.Headers.TryAddWithoutValidation("Api-Key", apiKey);
        login.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var loginResponse = await client.SendAsync(login, cancellationToken);
        loginResponse.EnsureSuccessStatusCode();

        using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync(cancellationToken));
        if (!loginDoc.RootElement.TryGetProperty("token", out var tokenElement))
        {
            return null;
        }

        var token = tokenElement.GetString();
        var host = loginDoc.RootElement.TryGetProperty("base_url", out var hostElement)
            ? hostElement.GetString()
            : "api.opensubtitles.com";
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "api.opensubtitles.com";
        }

        var useHash = !string.Equals(await Field(provider, "use_hash", false), "false", StringComparison.OrdinalIgnoreCase);
        var hash = useHash ? OpenSubtitlesHash.FromFile(query.VideoPath) : null;
        var type = query.MediaType == MediaType.Episode ? "episode" : "movie";
        var fileId = await OpenSubtitlesFileId(client, host, apiKey, token, query, type, hash, cancellationToken);
        if (fileId == null && !string.IsNullOrWhiteSpace(hash))
        {
            fileId = await OpenSubtitlesFileId(client, host, apiKey, token, query, type, null, cancellationToken);
        }

        if (fileId == null)
        {
            return null;
        }

        using var download = new HttpRequestMessage(HttpMethod.Post, $"https://{host.TrimEnd('/')}/api/v1/download")
        {
            Content = JsonContent(new { file_id = fileId.Value })
        };
        download.Headers.TryAddWithoutValidation("Api-Key", apiKey);
        download.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var downloadResponse = await client.SendAsync(download, cancellationToken);
        downloadResponse.EnsureSuccessStatusCode();

        using var downloadDoc = JsonDocument.Parse(await downloadResponse.Content.ReadAsStringAsync(cancellationToken));
        var link = downloadDoc.RootElement.TryGetProperty("link", out var linkElement)
            ? linkElement.GetString()
            : null;
        return string.IsNullOrWhiteSpace(link)
            ? null
            : await FetchBytes(provider.Id, new Uri(link), null, cancellationToken, query.Language);
    }

    private static async Task<int?> OpenSubtitlesFileId(
        HttpClient client,
        string host,
        string apiKey,
        string? token,
        SubtitleDownloadQuery query,
        string type,
        string? hash,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://{host.TrimEnd('/')}/api/v1/subtitles?query={Uri.EscapeDataString(query.SeriesTitle ?? query.Title)}" +
            $"&languages={Uri.EscapeDataString(query.Language)}" +
            $"&type={type}";
        var year = YearOf(query);
        if (year != null)
        {
            url += $"&year={year}";
        }

        if (query.SeasonNumber != null)
        {
            url += $"&season_number={query.SeasonNumber}";
        }

        if (query.EpisodeNumber != null)
        {
            url += $"&episode_number={query.EpisodeNumber}";
        }

        if (!string.IsNullOrWhiteSpace(hash))
        {
            url += $"&moviehash={hash}";
        }

        using var search = new HttpRequestMessage(HttpMethod.Get, url);
        search.Headers.TryAddWithoutValidation("Api-Key", apiKey);
        search.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        search.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var searchResponse = await client.SendAsync(search, cancellationToken);
        searchResponse.EnsureSuccessStatusCode();

        using var searchDoc = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync(cancellationToken));
        if (!searchDoc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("attributes", out var attributes))
            {
                continue;
            }

            if (!attributes.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var file in files.EnumerateArray())
            {
                if (file.TryGetProperty("file_id", out var id) && id.TryGetInt32(out var numeric))
                {
                    return numeric;
                }
            }
        }

        return null;
    }

    private async Task<SubtitleDownloadHit?> SubdlAsync(
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken)
    {
        var apiKey = await Field(provider, "api_key", true);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var type = query.MediaType == MediaType.Episode ? "tv" : "movie";
        var url =
            $"https://api.subdl.com/api/v1/subtitles?api_key={Uri.EscapeDataString(apiKey)}" +
            $"&film_name={Uri.EscapeDataString(query.SeriesTitle ?? query.Title)}" +
            $"&type={type}&languages={Uri.EscapeDataString(query.Language.ToUpperInvariant())}";
        var year = YearOf(query);
        if (year != null)
        {
            url += $"&year={year}";
        }

        if (query.SeasonNumber != null)
        {
            url += $"&season_number={query.SeasonNumber}";
        }

        if (query.EpisodeNumber != null)
        {
            url += $"&episode_number={query.EpisodeNumber}";
        }

        var client = Client();
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!doc.RootElement.TryGetProperty("subtitles", out var subtitles) || subtitles.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in subtitles.EnumerateArray())
        {
            var path = item.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var absolute = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? path
                : "https://dl.subdl.com" + path;
            var hit = await FetchBytes(provider.Id, new Uri(absolute), "https://subdl.com", cancellationToken, query.Language);
            if (hit != null)
            {
                return hit;
            }
        }

        return null;
    }

    private async Task<SubtitleDownloadHit?> GestdownAsync(SubtitleDownloadQuery query, CancellationToken cancellationToken)
    {
        if (query.MediaType != MediaType.Episode || query.SeasonNumber == null || query.EpisodeNumber == null)
        {
            return null;
        }

        var title = query.SeriesTitle ?? query.Title;
        var client = Client();
        using var shows = await client.GetAsync(
            $"https://api.gestdown.info/shows/search/{Uri.EscapeDataString(title)}",
            cancellationToken);
        shows.EnsureSuccessStatusCode();

        using var showDoc = JsonDocument.Parse(await shows.Content.ReadAsStringAsync(cancellationToken));
        if (!showDoc.RootElement.TryGetProperty("shows", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var lang = Lang3(query.Language);
        foreach (var show in list.EnumerateArray())
        {
            var id = show.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            using var subs = await client.GetAsync(
                $"https://api.gestdown.info/subtitles/get/{id}/{query.SeasonNumber}/{query.EpisodeNumber}/{lang}",
                cancellationToken);
            if (!subs.IsSuccessStatusCode)
            {
                continue;
            }

            using var subDoc = JsonDocument.Parse(await subs.Content.ReadAsStringAsync(cancellationToken));
            if (!subDoc.RootElement.TryGetProperty("matchingSubtitles", out var matching) || matching.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in matching.EnumerateArray())
            {
                if (item.TryGetProperty("completed", out var completed) && completed.ValueKind == JsonValueKind.False)
                {
                    continue;
                }

                var uri = item.TryGetProperty("downloadUri", out var download)
                    ? download.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                var absolute = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? uri
                    : "https://api.gestdown.info" + uri;
                var hit = await FetchBytes("gestdown", new Uri(absolute), "https://api.gestdown.info", cancellationToken, query.Language);
                if (hit != null)
                {
                    return hit;
                }
            }
        }

        return null;
    }

    private async Task<SubtitleDownloadHit?> AssrtAsync(
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken)
    {
        var token = await Field(provider, "token", true);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var client = Client();
        var search =
            $"https://api.assrt.net/v1/sub/search?token={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(QueryTitle(query))}";
        using var response = await client.GetAsync(search, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!doc.RootElement.TryGetProperty("sub", out var sub) ||
            !sub.TryGetProperty("subs", out var subs) ||
            subs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in subs.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetRawText() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            using var detail = await client.GetAsync(
                $"https://api.assrt.net/v1/sub/detail?token={Uri.EscapeDataString(token)}&id={id}",
                cancellationToken);
            if (!detail.IsSuccessStatusCode)
            {
                continue;
            }

            using var detailDoc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(cancellationToken));
            var url = detailDoc.RootElement.TryGetProperty("sub", out var detailSub) &&
                      detailSub.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var hit = await FetchBytes(provider.Id, new Uri(url), "https://assrt.net", cancellationToken, query.Language);
            if (hit != null)
            {
                return hit;
            }
        }

        return null;
    }

    private async Task<SubtitleDownloadHit?> BetaSeriesAsync(
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken)
    {
        if (query.MediaType != MediaType.Episode)
        {
            return null;
        }

        var token = await Field(provider, "token", true);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var client = Client();
        var search =
            $"https://api.betaseries.com/shows/search?key={Uri.EscapeDataString(token)}&title={Uri.EscapeDataString(query.SeriesTitle ?? query.Title)}";
        using var shows = await client.GetAsync(search, cancellationToken);
        shows.EnsureSuccessStatusCode();

        using var showDoc = JsonDocument.Parse(await shows.Content.ReadAsStringAsync(cancellationToken));
        if (!showDoc.RootElement.TryGetProperty("shows", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var show in list.EnumerateArray())
        {
            var id = show.TryGetProperty("id", out var idElement) ? idElement.GetRawText() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var subsUrl =
                $"https://api.betaseries.com/subtitles/show?key={Uri.EscapeDataString(token)}&id={id}" +
                $"&language={Uri.EscapeDataString(query.Language)}";
            using var subs = await client.GetAsync(subsUrl, cancellationToken);
            if (!subs.IsSuccessStatusCode)
            {
                continue;
            }

            using var subDoc = JsonDocument.Parse(await subs.Content.ReadAsStringAsync(cancellationToken));
            if (!subDoc.RootElement.TryGetProperty("subtitles", out var subtitles) || subtitles.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in subtitles.EnumerateArray())
            {
                var season = item.TryGetProperty("season", out var seasonEl) ? seasonEl.GetInt32() : -1;
                var episode = item.TryGetProperty("episode", out var episodeEl) ? episodeEl.GetInt32() : -1;
                if (season != query.SeasonNumber || episode != query.EpisodeNumber)
                {
                    continue;
                }

                var url = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var hit = await FetchBytes(provider.Id, new Uri(url), "https://www.betaseries.com", cancellationToken, query.Language);
                if (hit != null)
                {
                    return hit;
                }
            }
        }

        return null;
    }

    private async Task<SubtitleDownloadHit?> JsonSearchAsync(
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        string endpoint,
        string? apiField,
        CancellationToken cancellationToken)
    {
        var client = Client();
        var builder = new UriBuilder(endpoint);
        var queryString = $"q={Uri.EscapeDataString(QueryTitle(query))}&language={Uri.EscapeDataString(query.Language)}";
        if (!string.IsNullOrWhiteSpace(apiField))
        {
            var key = await Field(provider, apiField, true);
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            queryString += $"&{apiField}={Uri.EscapeDataString(key)}";
        }

        if (query.SeasonNumber != null)
        {
            queryString += $"&season={query.SeasonNumber}";
        }

        if (query.EpisodeNumber != null)
        {
            queryString += $"&episode={query.EpisodeNumber}";
        }

        var endpointQuery = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(endpointQuery) ? queryString : endpointQuery + "&" + queryString;
        using var response = await client.GetAsync(builder.Uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        foreach (var url in FindUrls(doc.RootElement))
        {
            if (!Uri.TryCreate(builder.Uri, url, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                continue;
            }

            var hit = await FetchBytes(provider.Id, uri, builder.Uri.GetLeftPart(UriPartial.Path),
                cancellationToken, query.Language);
            if (KeepHit(hit, provider, query))
            {
                return hit;
            }
        }

        return null;
    }

    internal static List<string> FindUrls(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return FindUrls(doc.RootElement);
    }

    internal static List<string> FindUrls(JsonElement element)
    {
        var urls = new List<string>();
        CollectUrls(element, urls);
        return urls;
    }

    private static void CollectUrls(JsonElement element, List<string> urls)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value) &&
                    (value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                     || value.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
                     || value.Contains("/download", StringComparison.OrdinalIgnoreCase)))
                {
                    urls.Add(value);
                }

                return;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectUrls(property.Value, urls);
                }

                return;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectUrls(item, urls);
                }

                return;
        }
    }

    private async Task<SubtitleDownloadHit?> TitloviAsync(
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken)
    {
        var username = await Field(provider, "username", false);
        var password = await Field(provider, "password", true);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var type = query.MediaType == MediaType.Episode ? "1" : "0";
        var url =
            "https://kodi.titlovi.com/api/subtitles/get?" +
            $"username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}" +
            $"&language={Uri.EscapeDataString(query.Language)}&query={Uri.EscapeDataString(QueryTitle(query))}&type={type}";
        return await JsonSearchAsync(provider, query, url, null, cancellationToken);
    }

    private async Task<SubtitleDownloadHit?> Napisy24Async(
        SubtitleProviderDefinition provider,
        SubtitleDownloadQuery query,
        CancellationToken cancellationToken)
    {
        var hash = OpenSubtitlesHash.FromFile(query.VideoPath);
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        var username = await Field(provider, "username", false) ?? "developer";
        var password = await Field(provider, "password", true) ?? "8qc64y21866r";
        var client = Client();
        var url =
            $"http://napisy24.pl/run/CheckSubAgent.php?post_form=1&data={hash}|{username}|{password}";
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length < 64)
        {
            return null;
        }

        var unpacked = SubtitleArchive.Unpack(bytes, "napisy24.zip", out var extension);
        return new SubtitleDownloadHit
        {
            Provider = provider.Id,
            Content = unpacked,
            FileName = $"napisy24{extension}",
            Extension = extension
        };
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
}
