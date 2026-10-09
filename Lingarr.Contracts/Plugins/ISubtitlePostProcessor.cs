namespace Lingarr.Contracts.Plugins;

public enum SubtitlePostProcessKind
{
    Language,
    Style,
    LineFit,
    QualityGate
}

/// <summary>
/// Rewrites a finished target subtitle. The host keeps the source file and the
/// caption tag, including ocr.
/// </summary>
public interface ISubtitlePostProcessor
{
    string Provider { get; }

    SubtitlePostProcessKind Kind { get; }

    Task<SubtitlePostProcessResult> ProcessAsync(
        SubtitlePostProcessInput input,
        CancellationToken cancellationToken);
}

public sealed class SubtitlePostProcessInput
{
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
    public required bool SourceIsOcr { get; init; }
    public required string TargetText { get; init; }
    public string? Glossary { get; init; }
}

public sealed class SubtitlePostProcessResult
{
    public string? Text { get; init; }
    public bool Reject { get; init; }
    public string? Reason { get; init; }
}
