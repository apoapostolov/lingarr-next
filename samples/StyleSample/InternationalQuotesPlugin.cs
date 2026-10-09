using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Contracts.Settings;

namespace Lingarr.Plugin.StyleSample;

public sealed record DialogueMark(string Code, string Country, string Open, string Close)
{
    public string Label => $"{Country} — {Open.Trim()} {Close.Trim()}".Trim();
}

public static class DialogueMarks
{
    public const string MatchTarget = "target";
    public const string DefaultCode = MatchTarget;

    public static readonly DialogueMark[] All =
    [
        Mark("us", "United States", "\u201c", "\u201d"),
        Mark("ca", "Canada", "\u201c", "\u201d"),
        Mark("au", "Australia", "\u201c", "\u201d"),
        Mark("nz", "New Zealand", "\u201c", "\u201d"),
        Mark("in", "India", "\u201c", "\u201d"),
        Mark("ph", "Philippines", "\u201c", "\u201d"),
        Mark("ng", "Nigeria", "\u201c", "\u201d"),
        Mark("za", "South Africa", "\u201c", "\u201d"),
        Mark("ke", "Kenya", "\u201c", "\u201d"),
        Mark("sg", "Singapore", "\u201c", "\u201d"),
        Mark("my", "Malaysia", "\u201c", "\u201d"),
        Mark("pk", "Pakistan", "\u201c", "\u201d"),
        Mark("kr", "South Korea", "\u201c", "\u201d"),
        Mark("cn", "China", "\u201c", "\u201d"),
        Mark("tr", "Turkey", "\u201c", "\u201d"),
        Mark("nl", "Netherlands", "\u201c", "\u201d"),
        Mark("id", "Indonesia", "\u201c", "\u201d"),
        Mark("th", "Thailand", "\u201c", "\u201d"),
        Mark("vn", "Vietnam", "\u201c", "\u201d"),
        Mark("il", "Israel", "\u201c", "\u201d"),
        Mark("mt", "Malta", "\u201c", "\u201d"),
        Mark("lv", "Latvia", "\u201c", "\u201d"),
        Mark("gb", "United Kingdom", "\u2018", "\u2019"),
        Mark("ie", "Ireland", "\u2018", "\u2019"),
        Mark("fr", "France", "\u00ab ", " \u00bb"),
        Mark("be", "Belgium", "\u00ab ", " \u00bb"),
        Mark("lu", "Luxembourg", "\u00ab ", " \u00bb"),
        Mark("it", "Italy", "\u00ab", "\u00bb"),
        Mark("gr", "Greece", "\u00ab", "\u00bb"),
        Mark("ru", "Russia", "\u00ab", "\u00bb"),
        Mark("ua", "Ukraine", "\u00ab", "\u00bb"),
        Mark("ch", "Switzerland", "\u00ab", "\u00bb"),
        Mark("sa", "Saudi Arabia", "\u00ab", "\u00bb"),
        Mark("eg", "Egypt", "\u00ab", "\u00bb"),
        Mark("ma", "Morocco", "\u00ab", "\u00bb"),
        Mark("dz", "Algeria", "\u00ab", "\u00bb"),
        Mark("ae", "United Arab Emirates", "\u00ab", "\u00bb"),
        Mark("es", "Spain", "\u2014 ", ""),
        Mark("mx", "Mexico", "\u2014 ", ""),
        Mark("ar", "Argentina", "\u2014 ", ""),
        Mark("cl", "Chile", "\u2014 ", ""),
        Mark("co", "Colombia", "\u2014 ", ""),
        Mark("pe", "Peru", "\u2014 ", ""),
        Mark("pt", "Portugal", "\u2014 ", ""),
        Mark("br", "Brazil", "\u2014 ", ""),
        Mark("dk", "Denmark", "\u00bb", "\u00ab"),
        Mark("no", "Norway", "\u00bb", "\u00ab"),
        Mark("de", "Germany", "\u201e", "\u201c"),
        Mark("at", "Austria", "\u201e", "\u201c"),
        Mark("cz", "Czechia", "\u201e", "\u201c"),
        Mark("sk", "Slovakia", "\u201e", "\u201c"),
        Mark("si", "Slovenia", "\u201e", "\u201c"),
        Mark("bg", "Bulgaria", "\u201e", "\u201c"),
        Mark("lt", "Lithuania", "\u201e", "\u201c"),
        Mark("ee", "Estonia", "\u201e", "\u201c"),
        Mark("pl", "Poland", "\u201e", "\u201d"),
        Mark("hu", "Hungary", "\u201e", "\u201d"),
        Mark("ro", "Romania", "\u201e", "\u201d"),
        Mark("hr", "Croatia", "\u201e", "\u201d"),
        Mark("rs", "Serbia", "\u201e", "\u201d"),
        Mark("se", "Sweden", "\u201d", "\u201d"),
        Mark("fi", "Finland", "\u201d", "\u201d"),
        Mark("jp", "Japan", "\u300c", "\u300d"),
        Mark("tw", "Taiwan", "\u300c", "\u300d"),
        Mark("hk", "Hong Kong", "\u300c", "\u300d")
    ];

    public static DialogueMark Find(string? code)
    {
        foreach (var mark in All)
        {
            if (string.Equals(mark.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return mark;
            }
        }

        return All[0];
    }

    public static DialogueMark ForTranslation(string? targetLanguage, string? choice)
    {
        if (!string.IsNullOrWhiteSpace(choice)
            && !string.Equals(choice, MatchTarget, StringComparison.OrdinalIgnoreCase))
        {
            return Find(choice);
        }

        return Find(CountryForLanguage(targetLanguage));
    }

    public static string CountryForLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "us";
        }

        var key = language.Trim().ToLowerInvariant().Replace('_', '-');
        if (LanguageCountries.TryGetValue(key, out var country))
        {
            return country;
        }

        var primary = key.Split('-')[0];
        return LanguageCountries.TryGetValue(primary, out country) ? country : "us";
    }

    private static readonly Dictionary<string, string> LanguageCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "us",
        ["en-us"] = "us",
        ["en-gb"] = "gb",
        ["en-uk"] = "gb",
        ["fr"] = "fr",
        ["fr-be"] = "be",
        ["fr-ch"] = "ch",
        ["fr-lu"] = "lu",
        ["de"] = "de",
        ["de-at"] = "at",
        ["de-ch"] = "ch",
        ["es"] = "es",
        ["es-mx"] = "mx",
        ["es-ar"] = "ar",
        ["es-cl"] = "cl",
        ["es-co"] = "co",
        ["es-pe"] = "pe",
        ["pt"] = "pt",
        ["pt-br"] = "br",
        ["it"] = "it",
        ["it-ch"] = "ch",
        ["nl"] = "nl",
        ["nl-be"] = "be",
        ["ja"] = "jp",
        ["zh"] = "cn",
        ["zh-cn"] = "cn",
        ["zh-hans"] = "cn",
        ["zh-tw"] = "tw",
        ["zh-hant"] = "tw",
        ["zh-hk"] = "hk",
        ["ko"] = "kr",
        ["ru"] = "ru",
        ["uk"] = "ua",
        ["pl"] = "pl",
        ["sv"] = "se",
        ["da"] = "dk",
        ["no"] = "no",
        ["nb"] = "no",
        ["nn"] = "no",
        ["fi"] = "fi",
        ["ar"] = "sa",
        ["el"] = "gr",
        ["tr"] = "tr",
        ["bg"] = "bg",
        ["cs"] = "cz",
        ["sk"] = "sk",
        ["hu"] = "hu",
        ["ro"] = "ro",
        ["hr"] = "hr",
        ["sr"] = "rs",
        ["sl"] = "si",
        ["lt"] = "lt",
        ["et"] = "ee",
        ["lv"] = "lv",
        ["he"] = "il",
        ["iw"] = "il",
        ["th"] = "th",
        ["vi"] = "vn",
        ["id"] = "id",
        ["hi"] = "in",
        ["ms"] = "my",
        ["ga"] = "ie",
        ["mt"] = "mt"
    };

    private static DialogueMark Mark(string code, string country, string open, string close) =>
        new(code, country, open, close);
}

public sealed class InternationalQuotesManifest : IPluginManifest
{
    public const string Id = "international-quotes";
    public const string CountryKey = "international_quotes_country";

    public string Provider => Id;
    public string DisplayName => "International quotes";
    public string? Description => "Uses the target language's dialogue marks, unless you pick another country.";
    public IReadOnlyList<PluginSettingField> Settings { get; } = [];
    public IReadOnlyList<PluginPanelContribution> Panels { get; } =
    [
        new()
        {
            Id = "quotes",
            Section = "plugins",
            TabId = "style",
            TabLabel = "Style",
            Title = "International quotes",
            Description = "Straight quotes follow the country of the target language. Pick a country to overwrite that style.",
            Fields =
            [
                new()
                {
                    Key = CountryKey,
                    Label = "Quote style",
                    Type = PluginSettingType.Dropdown,
                    Default = DialogueMarks.DefaultCode,
                    Description = "Match target language keeps the translated subtitle's country. Any other row overwrites it.",
                    Options =
                    [
                        new PluginSettingOption
                        {
                            Value = DialogueMarks.MatchTarget,
                            Label = "Match target language"
                        },
                        ..DialogueMarks.All
                            .OrderBy(mark => mark.Country, StringComparer.Ordinal)
                            .Select(mark => new PluginSettingOption { Value = mark.Code, Label = mark.Label })
                    ]
                }
            ]
        }
    ];
}

[PluginProvider(InternationalQuotesManifest.Id)]
public sealed class InternationalQuotesProcessor : ISubtitlePostProcessor
{
    private readonly ISettingsAccess _settings;

    public InternationalQuotesProcessor(ISettingsAccess settings)
    {
        _settings = settings;
    }

    public string Provider => InternationalQuotesManifest.Id;
    public SubtitlePostProcessKind Kind => SubtitlePostProcessKind.Style;

    public async Task<SubtitlePostProcessResult> ProcessAsync(
        SubtitlePostProcessInput input,
        CancellationToken cancellationToken)
    {
        var choice = await _settings.GetSettingAsync(InternationalQuotesManifest.CountryKey);
        var text = Apply(input.TargetText, input.TargetLanguage, choice);
        if (text == input.TargetText)
        {
            return new SubtitlePostProcessResult();
        }

        return new SubtitlePostProcessResult { Text = text };
    }

    public static string Apply(string text, string? country) =>
        Apply(text, targetLanguage: null, choice: country);

    public static string Apply(string text, string? targetLanguage, string? choice)
    {
        var mark = DialogueMarks.ForTranslation(targetLanguage, choice);
        var open = true;
        var result = new System.Text.StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character != '"')
            {
                result.Append(character);
                continue;
            }

            result.Append(open ? mark.Open : mark.Close);
            open = !open;
        }

        return result.ToString();
    }
}
