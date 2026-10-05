using Lingarr.Core.Entities;
using Lingarr.Server.Models.TranslationQuality;

namespace Lingarr.Server.Interfaces.Services;

public interface ITranslationQualityService
{
    /// <summary>
    /// Scores the lines that were actually saved. Missing positions are unfinished
    /// work, not a quality failure. Duplicate positions keep the newest row.
    /// </summary>
    int? ScorePartial(IReadOnlyList<TranslationRequestLine> lines, string targetLanguage);

    Task<TranslationQualitySummary> EvaluateAsync(
        int translationRequestId,
        CancellationToken cancellationToken = default);

    Task<TranslationQualityDetail?> GetAsync(
        int translationRequestId,
        string? severity = null,
        string? category = null,
        CancellationToken cancellationToken = default);
}
