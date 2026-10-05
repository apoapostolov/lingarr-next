using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.Jev;

public class JevSubtitleGate : IJevSubtitleGate
{
    public const int LinesPerCall = 16;

    private readonly ISettingService _settings;
    private readonly JevClient _client;
    private readonly ILogger<JevSubtitleGate> _logger;

    public JevSubtitleGate(
        ISettingService settings,
        IHttpClientFactory httpClientFactory,
        ILogger<JevSubtitleGate> logger)
    {
        _settings = settings;
        _client = new JevClient(httpClientFactory.CreateClient("jev"));
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
            var questions = new Dictionary<string, object>();
            foreach (var line in chunk)
            {
                questions[Key(line.Position)] = new
                {
                    type = "choice",
                    instructions = "What kind of subtitle line is the line whose id is " + line.Position + "? Judge only that line.",
                    criteria = new Dictionary<string, string>
                    {
                        ["dialogue"] = "Spoken words a viewer reads, including names said aloud and short replies.",
                        ["sound"] = "A hearing-impaired note or sound cue, usually in brackets, such as [door slams] or (sighs). Not spoken dialogue.",
                        ["credit"] = "A credit, copyright line, or advertisement. Not spoken dialogue.",
                        ["other"] = "Not enough text to tell, or none of the other labels."
                    }
                };
            }

            var decision = await Ask(chunk.Select(line => new { id = line.Position, text = line.Text }), questions, cancellationToken);
            if (decision == null)
            {
                continue;
            }

            foreach (var line in chunk)
            {
                if (!decision.Choices.TryGetValue(Key(line.Position), out var choice))
                {
                    continue;
                }

                if (JevLinePolicy.ShouldSkip(choice.Choice, choice.Confidence))
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
            var questions = new Dictionary<string, object>();
            foreach (var line in chunk)
            {
                questions[Key(line.Position)] = new
                {
                    type = "noul",
                    instructions = "For the pair whose id is " + line.Position + ", the translation is a real rendering of the source into the target language. It is not a copy of the source, a refusal, or a comment about being unable to translate.",
                    criteria = new Dictionary<string, string>
                    {
                        ["true"] = "A genuine translation into the target language.",
                        ["false"] = "The source copied through, a refusal, or chatter about translating."
                    }
                };
            }

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
                if (decision.Nouls.TryGetValue(Key(line.Position), out var probability) &&
                    JevLinePolicy.ShouldReject(probability))
                {
                    rejected.Add(line.Position);
                }
            }
        }

        return rejected;
    }

    private async Task<bool> SwitchOn(string key)
    {
        if (string.IsNullOrWhiteSpace(await _settings.GetEncryptedSetting(SettingKeys.Translation.TypesafeApiKey)))
        {
            return false;
        }

        return string.Equals(await _settings.GetSetting(key), "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<JevDecision?> Ask(
        object state,
        IReadOnlyDictionary<string, object> questions,
        CancellationToken cancellationToken)
    {
        var apiKey = await _settings.GetEncryptedSetting(SettingKeys.Translation.TypesafeApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            return await _client.Ask(apiKey, state, questions, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogInformation(exception, "Jev did not answer. The line keeps the normal translation path.");
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
