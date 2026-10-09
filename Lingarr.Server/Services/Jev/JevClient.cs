using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lingarr.Server.Services.Classification;

namespace Lingarr.Server.Services.Jev;

public sealed class JevClient : IClassifierClient
{
    public const string Endpoint = "https://api.typesafe.ai/v1/systemone";
    public const string Model = "jev-latest";

    private readonly HttpClient _http;

    public JevClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ClassifierDecision?> Ask(
        string apiKey,
        object state,
        IReadOnlyList<ClassifierQuestion> questions,
        CancellationToken cancellationToken)
    {
        var jevQuestions = new Dictionary<string, object>();
        foreach (var question in questions)
        {
            jevQuestions[question.Name] = new
            {
                type = question.Type == "predicate" ? "noul" : question.Type,
                instructions = question.Instructions,
                criteria = question.Type == "predicate"
                    ? new Dictionary<string, string>
                    {
                        ["true"] = "A genuine translation into the target language.",
                        ["false"] = "The source copied through, a refusal, or chatter about translating."
                    }
                    : question.Choices
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { model = Model, state, questions = jevQuestions }),
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
            !document.RootElement.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var decision = new ClassifierDecision();
        foreach (var answer in answers.EnumerateObject())
        {
            if (answer.Value.ValueKind != JsonValueKind.Object ||
                !answer.Value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (type.GetString() == "choice" &&
                answer.Value.TryGetProperty("choice", out var choice) && choice.ValueKind == JsonValueKind.String)
            {
                var confidence = answer.Value.TryGetProperty("confidence", out var value) &&
                                 value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed) &&
                                 parsed is >= 0 and <= 1 ? parsed : 0;
                decision.Choices[answer.Name] = new ClassifierChoice(choice.GetString() ?? "", confidence);
            }
            else if (type.GetString() == "noul" &&
                     answer.Value.TryGetProperty("noul", out var noul) && noul.ValueKind == JsonValueKind.Number &&
                     noul.TryGetDouble(out var probability) && probability is >= 0 and <= 1)
            {
                decision.Predicates[answer.Name] = probability;
            }
        }

        return decision;
    }
}
