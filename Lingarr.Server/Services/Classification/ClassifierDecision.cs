namespace Lingarr.Server.Services.Classification;

public static class ClassifierProvider
{
    public const string Jev = "jev";
    public const string Luna = "luna";
}

public sealed record ClassifierQuestion(
    string Name,
    string Type,
    string Instructions,
    IReadOnlyDictionary<string, string>? Choices = null);

public interface IClassifierClient
{
    Task<ClassifierDecision?> Ask(
        string apiKey,
        object state,
        IReadOnlyList<ClassifierQuestion> questions,
        CancellationToken cancellationToken);
}

public sealed record ClassifierChoice(string Choice, double Confidence);

public sealed class ClassifierDecision
{
    public Dictionary<string, ClassifierChoice> Choices { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Predicates { get; } = new(StringComparer.Ordinal);
}
