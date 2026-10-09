using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lingarr.Server.Services.Classification;

/// <summary>OpenAI Decisions API adapter. Codex login tokens are not used for this endpoint.</summary>
public sealed class LunaDecisionsClient : IClassifierClient
{
    public const string Endpoint = "https://api.openai.com/v1/decisions";
    public const string Model = "gpt-6-luna";

    private readonly HttpClient _http;

    public LunaDecisionsClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ClassifierDecision?> Ask(
        string apiKey,
        object state,
        IReadOnlyList<ClassifierQuestion> questions,
        CancellationToken cancellationToken)
    {
        var requests = questions.Select(question => question.Type == "choice"
            ? (object)new
            {
                type = "choice",
                name = question.Name,
                instructions = question.Instructions,
                choices = question.Choices!.Select(choice => new { value = choice.Key, description = choice.Value })
            }
            : new
            {
                type = "predicate",
                name = question.Name,
                instructions = question.Instructions
            }).ToArray();

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { model = Model, input = JsonSerializer.Serialize(state), questions = requests }),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var decision = new ClassifierDecision();
        foreach (var answer in answers.EnumerateArray())
        {
            if (answer.ValueKind != JsonValueKind.Object ||
                !answer.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                !answer.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var key = name.GetString();
            if (string.IsNullOrEmpty(key)) continue;

            if (type.GetString() == "choice" &&
                answer.TryGetProperty("choice", out var choice) && choice.ValueKind == JsonValueKind.String &&
                answer.TryGetProperty("confidence", out var confidence) && confidence.ValueKind == JsonValueKind.Number &&
                confidence.TryGetDouble(out var confidenceValue) && confidenceValue is >= 0 and <= 1)
            {
                decision.Choices[key] = new ClassifierChoice(choice.GetString() ?? "", confidenceValue);
            }
            else if (type.GetString() == "predicate" &&
                     answer.TryGetProperty("probability", out var probability) && probability.ValueKind == JsonValueKind.Number &&
                     probability.TryGetDouble(out var probabilityValue) && probabilityValue is >= 0 and <= 1)
            {
                decision.Predicates[key] = probabilityValue;
            }
        }

        return decision;
    }
}
