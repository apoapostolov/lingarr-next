using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lingarr.Server.Services.Jev;

public sealed class JevClient
{
    public const string Endpoint = "https://api.typesafe.ai/v1/systemone";
    public const string Model = "jev-latest";

    private readonly HttpClient _http;

    public JevClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<JevDecision?> Ask(
        string apiKey,
        object state,
        IReadOnlyDictionary<string, object> questions,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { model = Model, state, questions }),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("answers", out var answers))
        {
            return null;
        }

        var decision = new JevDecision();
        foreach (var answer in answers.EnumerateObject())
        {
            if (!answer.Value.TryGetProperty("type", out var type))
            {
                continue;
            }

            if (type.GetString() == "choice" &&
                answer.Value.TryGetProperty("choice", out var choice))
            {
                var confidence = answer.Value.TryGetProperty("confidence", out var confidenceValue)
                    ? confidenceValue.GetDouble()
                    : 0;
                decision.Choices[answer.Name] = new JevChoice(choice.GetString() ?? "", confidence);
            }
            else if (type.GetString() == "noul" &&
                     answer.Value.TryGetProperty("noul", out var noul))
            {
                decision.Nouls[answer.Name] = noul.GetDouble();
            }
        }

        return decision;
    }
}

public sealed record JevChoice(string Choice, double Confidence);

public sealed class JevDecision
{
    public Dictionary<string, JevChoice> Choices { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Nouls { get; } = new(StringComparer.Ordinal);
}
