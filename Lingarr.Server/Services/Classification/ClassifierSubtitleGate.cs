using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Jev;

namespace Lingarr.Server.Services.Classification;

public class ClassifierSubtitleGate : IClassifierSubtitleGate
{
    public const int LinesPerCall = 16;

    private readonly ISettingService _settings;
    private readonly IClassifierClient _jev;
    private readonly IClassifierClient _luna;
    private readonly ILogger<ClassifierSubtitleGate> _logger;

    public ClassifierSubtitleGate(
        ISettingService settings,
        IHttpClientFactory httpClientFactory,
        ILogger<ClassifierSubtitleGate> logger)
    {
        _settings = settings;
        _jev = new JevClient(httpClientFactory.CreateClient("jev"));
        _luna = new LunaDecisionsClient(httpClientFactory.CreateClient("openai-decisions"));
        _logger = logger;
    }

    public Task<bool> SkipEnabled(CancellationToken cancellationToken) =>
        SwitchOn(SettingKeys.Translation.JevSkipNonDialogue);

    public Task<bool> RejectEnabled(CancellationToken cancellationToken) =>
        SwitchOn(SettingKeys.Translation.JevRejectUntranslated);

    public async Task<IReadOnlySet<int>> PositionsToSkip(
        IReadOnlyList<(int Position, string Text)> lines,
        CancellationToken cancellationToken)
    {
        if (!await SkipEnabled(cancellationToken))
        {
            return new HashSet<int>();
        }

        var skipped = new HashSet<int>();
        foreach (var chunk in Chunk(lines))
        {
            var questions = chunk.Select(line => new ClassifierQuestion(
                Key(line.Position),
                "choice",
                "What kind of subtitle line is the line whose id is " + line.Position + "? Judge only that line.",
                new Dictionary<string, string>
                {
                    ["dialogue"] = "Spoken words a viewer reads, including names said aloud and short replies.",
                    ["sound"] = "A hearing-impaired note or sound cue, usually in brackets, such as [door slams] or (sighs). Not spoken dialogue.",
                    ["credit"] = "A credit, copyright line, or advertisement. Not spoken dialogue.",
                    ["other"] = "Not enough text to tell, or none of the other labels."
                })).ToList();

            var decision = await Ask(chunk.Select(line => new { id = line.Position, text = line.Text }), questions, cancellationToken);
            if (decision == null)
            {
                continue;
            }

            foreach (var line in chunk)
            {
                if (decision.Choices.TryGetValue(Key(line.Position), out var choice) &&
                    ClassifierLinePolicy.ShouldSkip(choice.Choice, choice.Confidence))
                {
                    skipped.Add(line.Position);
                }
            }
        }

        return skipped;
    }

    public async Task<IReadOnlySet<int>> PositionsToReject(
        IReadOnlyList<(int Position, string Source, string Translation)> lines,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (!await RejectEnabled(cancellationToken))
        {
            return new HashSet<int>();
        }

        var rejected = new HashSet<int>();
        foreach (var chunk in Chunk(lines))
        {
            var questions = chunk.Select(line => new ClassifierQuestion(
                Key(line.Position),
                "predicate",
                "For the pair whose id is " + line.Position + ", the translation is a genuine rendering of the source into the target language. It is not a copy of the source, a refusal, or a comment about being unable to translate.")).ToList();

            var decision = await Ask(
                chunk.Select(line => new
                {
                    id = line.Position,
                    source = line.Source,
                    translation = line.Translation,
                    sourceLanguage,
                    targetLanguage
                }),
                questions,
                cancellationToken);
            if (decision == null)
            {
                continue;
            }

            foreach (var line in chunk)
            {
                if (decision.Predicates.TryGetValue(Key(line.Position), out var probability) &&
                    ClassifierLinePolicy.ShouldReject(probability))
                {
                    rejected.Add(line.Position);
                }
            }
        }

        return rejected;
    }

    private async Task<bool> SwitchOn(string key)
    {
        var credentials = await Credentials();
        return credentials != null &&
               string.Equals(await _settings.GetSetting(key), "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(IClassifierClient Client, string Key)?> Credentials()
    {
        var provider = await _settings.GetSetting(SettingKeys.Translation.ClassifierProvider);
        var (client, keyName) = provider switch
        {
            null or "" or ClassifierProvider.Jev => (_jev, SettingKeys.Translation.TypesafeApiKey),
            ClassifierProvider.Luna => (_luna, SettingKeys.Translation.OpenAi.ApiKey),
            _ => (null, null)
        };
        if (client == null || keyName == null) return null;

        var apiKey = await _settings.GetEncryptedSetting(keyName);
        return string.IsNullOrWhiteSpace(apiKey) ? null : (client, apiKey);
    }

    private async Task<ClassifierDecision?> Ask(
        object state,
        IReadOnlyList<ClassifierQuestion> questions,
        CancellationToken cancellationToken)
    {
        var credentials = await Credentials();
        if (credentials == null) return null;

        try
        {
            return await credentials.Value.Client.Ask(credentials.Value.Key, state, questions, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                          exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogInformation(exception, "Classifier request failed. The line keeps the normal translation path.");
            return null;
        }
    }

    private static string Key(int position) => "p" + position;

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> lines)
    {
        for (var index = 0; index < lines.Count; index += LinesPerCall)
        {
            yield return lines.Skip(index).Take(LinesPerCall).ToList();
        }
    }
}
