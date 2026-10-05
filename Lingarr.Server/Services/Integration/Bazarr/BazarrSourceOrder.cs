namespace Lingarr.Server.Services.Integration.Bazarr;

public static class BazarrSourceOrder
{
    public static bool ExtractFirst(string? value) =>
        !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    public static bool SourceIncludesEnglish(IReadOnlySet<string> sourceLanguages) =>
        sourceLanguages.Any(code =>
        {
            var token = code.Trim().ToLowerInvariant();
            return token is "en" or "eng" || token.StartsWith("en-", StringComparison.Ordinal);
        });
}
