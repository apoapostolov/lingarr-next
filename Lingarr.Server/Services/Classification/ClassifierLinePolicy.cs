namespace Lingarr.Server.Services.Classification;

/// <summary>
/// Thresholds for acting on a classifier answer. Uncertain answers do nothing.
/// </summary>
public static class ClassifierLinePolicy
{
    public const double SkipConfidence = 0.85;
    public const double RejectBelow = 0.35;

    public static bool ShouldSkip(string? choice, double confidence) =>
        confidence >= SkipConfidence && choice is "sound" or "credit";

    public static bool ShouldReject(double isRealTranslation) =>
        isRealTranslation < RejectBelow;
}
