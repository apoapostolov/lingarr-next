using System.Text.Json;
using System.Text.RegularExpressions;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.TranslationQuality;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public partial class TranslationQualityService : ITranslationQualityService
{
    private const int RulesetVersion = 1;
    private readonly LingarrDbContext _dbContext;
    private readonly ILogger<TranslationQualityService> _logger;

    public TranslationQualityService(
        LingarrDbContext dbContext,
        ILogger<TranslationQualityService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<TranslationQualitySummary> EvaluateAsync(
        int translationRequestId,
        CancellationToken cancellationToken = default)
    {
        var request = await _dbContext.TranslationRequests
            .FirstOrDefaultAsync(x => x.Id == translationRequestId, cancellationToken)
            ?? throw new KeyNotFoundException($"Translation request {translationRequestId} was not found.");

        var lines = await _dbContext.TranslationRequestLines
            .Where(x => x.TranslationRequestId == translationRequestId)
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var previousAssessments = await _dbContext.TranslationQualityAssessments
            .Where(x =>
                x.TranslationRequestId == translationRequestId &&
                x.EvaluationStatus != QualityEvaluationStatus.Superseded)
            .ToListAsync(cancellationToken);
        foreach (var previous in previousAssessments)
            previous.EvaluationStatus = QualityEvaluationStatus.Superseded;

        var assessment = new TranslationQualityAssessment
        {
            TranslationRequestId = translationRequestId,
            RulesetVersion = RulesetVersion,
            Grade = "Not available",
            EvaluationStatus = QualityEvaluationStatus.Unavailable,
            LineCount = lines.Count,
            EvaluatedAt = DateTime.UtcNow
        };

        if (lines.Count == 0)
        {
            _dbContext.TranslationQualityAssessments.Add(assessment);
            request.QualityScore = null;
            request.QualityGrade = assessment.Grade;
            request.QualityStatus = assessment.EvaluationStatus;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ToSummary(assessment);
        }

        var findings = EvaluateLines(lines, request.TargetLanguage, includeGaps: true);
        var scores = lines
            .Select(line => Math.Max(0, 100 - findings
                .Where(f => f.TranslationRequestLineId == line.Id)
                .GroupBy(f => f.RuleId)
                .Sum(group => group.Max(f => f.Penalty))))
            .OrderBy(score => score)
            .ToArray();

        var average = scores.Average();
        var lowTailCount = Math.Max(1, (int)Math.Ceiling(scores.Length * 0.05));
        var lowTail = scores.Take(lowTailCount).Average();
        var score = (int)Math.Round((average * 0.75) + (lowTail * 0.25));

        var structuralCritical = findings.Any(f =>
            f.Severity == QualitySeverity.Critical &&
            f.Category == "Integrity");
        if (structuralCritical)
            score = Math.Min(score, 49);

        var emptyCount = findings.Count(f => f.RuleId == "integrity.target_empty");
        if (emptyCount > lines.Count * 0.05)
            score = Math.Min(score, 39);

        assessment.Score = Math.Clamp(score, 0, 100);
        assessment.Grade = GradeFor(assessment.Score.Value);
        assessment.AverageLineScore = Math.Round(average, 1);
        assessment.LowTailScore = Math.Round(lowTail, 1);
        assessment.CriticalCount = findings.Count(f => f.Severity == QualitySeverity.Critical);
        assessment.ErrorCount = findings.Count(f => f.Severity == QualitySeverity.Error);
        assessment.WarningCount = findings.Count(f => f.Severity == QualitySeverity.Warning);
        assessment.EvaluationStatus = QualityEvaluationStatus.Completed;

        _dbContext.TranslationQualityAssessments.Add(assessment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var finding in findings)
            finding.TranslationQualityAssessmentId = assessment.Id;
        _dbContext.TranslationLineQualityFindings.AddRange(findings);

        request.QualityScore = assessment.Score;
        request.QualityGrade = assessment.Grade;
        request.QualityStatus = assessment.EvaluationStatus;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Quality assessment {AssessmentId} scored translation request {RequestId} at {Score}.",
            assessment.Id, translationRequestId, assessment.Score);
        return ToSummary(assessment);
    }

    public async Task<TranslationQualityDetail?> GetAsync(
        int translationRequestId,
        string? severity = null,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var assessment = await _dbContext.TranslationQualityAssessments
            .Where(x => x.TranslationRequestId == translationRequestId)
            .OrderByDescending(x => x.EvaluatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (assessment == null)
            return null;

        var query = _dbContext.TranslationLineQualityFindings
            .Where(x => x.TranslationQualityAssessmentId == assessment.Id);
        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(x => x.Severity == severity.ToLower());
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(x => x.Category == category);

        var findings = await query
            .OrderBy(x => x.LinePosition)
            .ThenBy(x => x.Id)
            .Select(x => new TranslationQualityFindingResponse(
                x.Id,
                x.TranslationRequestLineId,
                x.LinePosition,
                x.RuleId,
                x.Category,
                x.Severity,
                x.Penalty,
                x.Summary,
                x.MetadataJson))
            .ToListAsync(cancellationToken);

        return new TranslationQualityDetail(ToSummary(assessment), findings);
    }

    public int? ScorePartial(IReadOnlyList<TranslationRequestLine> lines, string targetLanguage)
    {
        var newest = lines
            .GroupBy(line => line.Position)
            .Select(group => group.OrderByDescending(line => line.Id).First())
            .OrderBy(line => line.Position)
            .ToList();
        if (newest.Count == 0)
        {
            return null;
        }

        var findings = EvaluateLines(newest, targetLanguage, includeGaps: false);
        var scores = newest
            .Select(line => Math.Max(0, 100 - findings
                .Where(finding => finding.TranslationRequestLineId == line.Id)
                .GroupBy(finding => finding.RuleId)
                .Sum(group => group.Max(finding => finding.Penalty))))
            .OrderBy(score => score)
            .ToArray();

        var average = scores.Average();
        var lowTailCount = Math.Max(1, (int)Math.Ceiling(scores.Length * 0.05));
        var lowTail = scores.Take(lowTailCount).Average();
        var score = (int)Math.Round((average * 0.75) + (lowTail * 0.25));

        var emptyCount = findings.Count(finding => finding.RuleId == "integrity.target_empty");
        if (emptyCount > newest.Count * 0.05)
        {
            score = Math.Min(score, 39);
        }

        return Math.Clamp(score, 0, 100);
    }

    private static List<TranslationLineQualityFinding> EvaluateLines(
        IReadOnlyList<TranslationRequestLine> lines,
        string targetLanguage,
        bool includeGaps)
    {
        var findings = new List<TranslationLineQualityFinding>();
        var positions = lines.GroupBy(line => line.Position).ToList();
        if (includeGaps)
        {
            foreach (var duplicate in positions.Where(group => group.Count() > 1))
            {
                foreach (var line in duplicate)
                    Add(findings, line, "integrity.position_duplicate", "Integrity", QualitySeverity.Critical, 100,
                        "This subtitle position appears more than once.", new { count = duplicate.Count() });
            }

            var orderedPositions = positions.Select(group => group.Key).OrderBy(x => x).ToArray();
            if (orderedPositions.Length > 1)
            {
                var missing = Enumerable.Range(orderedPositions[0], orderedPositions[^1] - orderedPositions[0] + 1)
                    .Except(orderedPositions)
                    .Count();
                if (missing > 0)
                {
                    Add(findings, null, "integrity.position_missing", "Integrity", QualitySeverity.Critical, 0,
                        "One or more subtitle positions are missing.", new { count = missing });
                }
            }
        }

        foreach (var line in lines)
            EvaluateLine(line, targetLanguage, findings);

        foreach (var group in lines
            .Where(line => Normalized(line.Target).Length >= 8)
            .GroupBy(line => Normalized(line.Target))
            .Where(group => group.Count() >= 3))
        {
            foreach (var line in group)
                Add(findings, line, "content.repeated_output", "Consistency", QualitySeverity.Warning, 5,
                    "The same translated text is repeated across several subtitle lines.",
                    new { count = group.Count() });
        }

        for (var index = 1; index < lines.Count; index++)
        {
            if (Normalized(lines[index - 1].Target).Length >= 8 &&
                Normalized(lines[index - 1].Target) == Normalized(lines[index].Target) &&
                Normalized(lines[index - 1].Source) != Normalized(lines[index].Source))
            {
                Add(findings, lines[index], "content.neighbour_duplication", "Consistency", QualitySeverity.Error, 20,
                    "This translation duplicates the previous line even though the source differs.");
            }
        }

        return findings;
    }

    private static void EvaluateLine(
        TranslationRequestLine line,
        string targetLanguage,
        ICollection<TranslationLineQualityFinding> findings)
    {
        var source = line.Source ?? string.Empty;
        var target = line.Target ?? string.Empty;
        var sourcePlain = StripFormatting(source).Trim();
        var targetPlain = StripFormatting(target).Trim();

        if (string.IsNullOrWhiteSpace(targetPlain))
        {
            Add(findings, line, "integrity.target_empty", "Integrity", QualitySeverity.Critical, 100,
                "The translated line is empty.");
            return;
        }

        if (ReplacementCharacterRegex().IsMatch(target))
            Add(findings, line, "format.invalid_unicode", "Formatting", QualitySeverity.Critical, 100,
                "The result contains a broken or replacement character.");
        if (ControlCharacterRegex().IsMatch(target))
            Add(findings, line, "format.control_character", "Formatting", QualitySeverity.Critical, 100,
                "The result contains an unexpected control character.");
        if (ModelRefusalRegex().IsMatch(targetPlain))
            Add(findings, line, "model.refusal", "Model output", QualitySeverity.Critical, 100,
                "The translator appears to have refused or explained the task instead of translating.");
        if (MetaCommentaryRegex().IsMatch(targetPlain))
            Add(findings, line, "model.meta_commentary", "Model output", QualitySeverity.Error, 20,
                "The result appears to contain translator commentary.");
        if (CodeFenceRegex().IsMatch(target))
            Add(findings, line, "model.code_fence", "Model output", QualitySeverity.Error, 20,
                "The result contains a code block wrapper.");
        if (JsonWrapperRegex().IsMatch(targetPlain))
            Add(findings, line, "model.json_wrapper", "Model output", QualitySeverity.Error, 20,
                "The result looks like JSON or a structured response rather than subtitle text.");
        if (RawErrorRegex().IsMatch(targetPlain))
            Add(findings, line, "model.raw_error", "Model output", QualitySeverity.Critical, 100,
                "The result appears to contain a raw provider error.");
        if (BatchIndexRegex().IsMatch(targetPlain))
            Add(findings, line, "model.batch_index_leak", "Model output", QualitySeverity.Error, 20,
                "An internal batch line number appears to have leaked into the subtitle.");

        if (Normalized(sourcePlain) == Normalized(targetPlain) && ContainsLetters(sourcePlain))
            Add(findings, line, "content.unchanged", "Content", QualitySeverity.Warning, 5,
                "The translation is unchanged from the source.",
                new { sourceLength = sourcePlain.Length, targetLength = targetPlain.Length });

        if (sourcePlain.Length >= 12 && targetPlain.Length < sourcePlain.Length * 0.25)
            Add(findings, line, "content.severe_truncation", "Content", QualitySeverity.Error, 20,
                "The translation is much shorter than the source.",
                new { ratio = Ratio(targetPlain.Length, sourcePlain.Length) });
        if (sourcePlain.Length >= 5 && targetPlain.Length > Math.Max(80, sourcePlain.Length * 4))
            Add(findings, line, "content.severe_expansion", "Content", QualitySeverity.Warning, 5,
                "The translation is unexpectedly much longer than the source.",
                new { ratio = Ratio(targetPlain.Length, sourcePlain.Length) });

        var sourceNumbers = NumberRegex().Matches(sourcePlain).Select(match => match.Value).ToArray();
        var targetNumbers = NumberRegex().Matches(targetPlain).Select(match => match.Value).ToArray();
        if (!sourceNumbers.SequenceEqual(targetNumbers))
            Add(findings, line, "content.number_changed", "Content", QualitySeverity.Error, 20,
                "A number was removed, added, or changed.",
                new { sourceCount = sourceNumbers.Length, targetCount = targetNumbers.Length });

        CompareTokenSets(findings, line, source, target, UrlRegex(), "url_changed",
            "A web address was removed or changed.");
        CompareTokenSets(findings, line, source, target, EmailRegex(), "email_changed",
            "An email address was removed or changed.");
        CompareTokenSets(findings, line, source, target, PlaceholderRegex(), "placeholder_changed",
            "A placeholder or template token was removed or changed.");

        var sourceTags = TagRegex().Matches(source).Select(match => match.Value.ToLowerInvariant()).ToArray();
        var targetTags = TagRegex().Matches(target).Select(match => match.Value.ToLowerInvariant()).ToArray();
        if (!sourceTags.SequenceEqual(targetTags))
            Add(findings, line, "formatting_tags_changed", "Formatting", QualitySeverity.Warning, 5,
                "Subtitle formatting tags were removed, added, or reordered.",
                new { sourceCount = sourceTags.Length, targetCount = targetTags.Length });
        if (HasUnbalancedTags(target))
            Add(findings, line, "unbalanced_tags", "Formatting", QualitySeverity.Error, 20,
                "The translated line has unbalanced formatting tags.");

        var sourceBreaks = LineBreakRegex().Matches(source).Count;
        var targetBreaks = LineBreakRegex().Matches(target).Count;
        if (sourceBreaks != targetBreaks)
            Add(findings, line, "line_breaks_changed", "Formatting", QualitySeverity.Warning, 5,
                "Line breaks changed during translation.",
                new { sourceCount = sourceBreaks, targetCount = targetBreaks });
        if (target.Length != target.Trim().Length)
            Add(findings, line, "boundary_whitespace", "Formatting", QualitySeverity.Info, 0,
                "The translated line starts or ends with extra whitespace.");

        var renderedLines = LineBreakRegex().Split(targetPlain);
        if (renderedLines.Length > 2)
            Add(findings, line, "too_many_lines", "Readability", QualitySeverity.Warning, 5,
                "The subtitle uses more than two displayed lines.",
                new { count = renderedLines.Length });
        if (renderedLines.Any(part => part.Length > 42))
            Add(findings, line, "line_too_long", "Readability", QualitySeverity.Warning, 5,
                "A displayed subtitle line is longer than 42 characters.",
                new { longest = renderedLines.Max(part => part.Length) });
        if (LetterRegex().Matches(targetPlain).Count >= 8 &&
            LetterRegex().Matches(targetPlain).All(match => char.IsUpper(match.Value[0])))
            Add(findings, line, "all_caps", "Readability", QualitySeverity.Warning, 5,
                "The translated line is entirely uppercase.");
        if (RepeatedPunctuationRegex().IsMatch(targetPlain))
            Add(findings, line, "repeated_punctuation", "Readability", QualitySeverity.Warning, 5,
                "The result contains unusually repeated punctuation.");

        EvaluateTargetScript(line, targetLanguage, targetPlain, findings);
    }

    private static void EvaluateTargetScript(
        TranslationRequestLine line,
        string targetLanguage,
        string target,
        ICollection<TranslationLineQualityFinding> findings)
    {
        var expected = ExpectedScript(targetLanguage);
        if (expected == null)
            return;
        var letters = LetterRegex().Matches(target).Select(match => match.Value[0]).ToArray();
        if (letters.Length < 20)
            return;
        var expectedCount = letters.Count(expected);
        if ((double)expectedCount / letters.Length < 0.35)
            Add(findings, line, "target_script_mismatch", "Language", QualitySeverity.Error, 20,
                "The result may be written in the wrong alphabet.",
                new { expectedRatio = Math.Round((double)expectedCount / letters.Length, 2) });
    }

    private static Func<char, bool>? ExpectedScript(string language)
    {
        var code = language.Split('-', '_')[0].Trim().ToLowerInvariant();
        if (new[] { "uk", "ru", "bg", "mk", "sr" }.Contains(code))
            return ch => ch is >= '\u0400' and <= '\u052f';
        if (code == "el")
            return ch => ch is >= '\u0370' and <= '\u03ff';
        if (new[] { "ja", "zh" }.Contains(code))
            return ch => ch is >= '\u3040' and <= '\u30ff' || ch is >= '\u3400' and <= '\u9fff';
        if (code == "ko")
            return ch => ch is >= '\uac00' and <= '\ud7af';
        return null;
    }

    private static void CompareTokenSets(
        ICollection<TranslationLineQualityFinding> findings,
        TranslationRequestLine line,
        string source,
        string target,
        Regex regex,
        string ruleId,
        string summary)
    {
        var sourceTokens = regex.Matches(source).Select(match => match.Value).OrderBy(x => x).ToArray();
        var targetTokens = regex.Matches(target).Select(match => match.Value).OrderBy(x => x).ToArray();
        if (!sourceTokens.SequenceEqual(targetTokens))
            Add(findings, line, ruleId, "Content", QualitySeverity.Error, 20, summary,
                new { sourceCount = sourceTokens.Length, targetCount = targetTokens.Length });
    }

    private static void Add(
        ICollection<TranslationLineQualityFinding> findings,
        TranslationRequestLine? line,
        string ruleId,
        string category,
        string severity,
        int penalty,
        string summary,
        object? metadata = null)
    {
        if (line != null && findings.Any(f =>
                f.TranslationRequestLineId == line.Id && f.RuleId == ruleId))
            return;
        findings.Add(new TranslationLineQualityFinding
        {
            TranslationQualityAssessmentId = 0,
            TranslationRequestLineId = line?.Id,
            LinePosition = line?.Position,
            RuleId = ruleId,
            Category = category,
            Severity = severity,
            Penalty = penalty,
            Summary = summary,
            MetadataJson = JsonSerializer.Serialize(metadata ?? new { })
        });
    }

    private static bool HasUnbalancedTags(string value)
    {
        var openings = OpeningTagRegex().Matches(value).Count;
        var closings = ClosingTagRegex().Matches(value).Count;
        return openings != closings;
    }

    public static string GradeFor(int score) => score switch
    {
        >= 95 => "Excellent",
        >= 85 => "Good",
        >= 70 => "Review suggested",
        >= 50 => "Poor",
        _ => "Failed quality gate"
    };

    private static string StripFormatting(string value) => TagRegex().Replace(value, string.Empty);
    private static string Normalized(string value) =>
        WhitespaceRegex().Replace(value.Trim().ToLowerInvariant(), " ");
    private static bool ContainsLetters(string value) => LetterRegex().IsMatch(value);
    private static double Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round((double)numerator / denominator, 2);

    private static TranslationQualitySummary ToSummary(TranslationQualityAssessment assessment) =>
        new(
            assessment.Id,
            assessment.TranslationRequestId,
            assessment.Score,
            assessment.Grade,
            assessment.AverageLineScore,
            assessment.LowTailScore,
            assessment.CriticalCount,
            assessment.ErrorCount,
            assessment.WarningCount,
            assessment.LineCount,
            assessment.EvaluatedAt,
            assessment.EvaluationStatus);

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]", RegexOptions.Compiled)]
    private static partial Regex ControlCharacterRegex();
    [GeneratedRegex("\uFFFD", RegexOptions.Compiled)]
    private static partial Regex ReplacementCharacterRegex();
    [GeneratedRegex(@"^(?:i (?:can(?:not|'t)|won't)|sorry[,! ]|as an ai\b)|\b(?:unable to (?:translate|comply)|cannot assist)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ModelRefusalRegex();
    [GeneratedRegex(@"^(?:translation|translated text|here(?:'s| is) (?:the )?translation|note):\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MetaCommentaryRegex();
    [GeneratedRegex(@"```", RegexOptions.Compiled)]
    private static partial Regex CodeFenceRegex();
    [GeneratedRegex(@"^\s*[\{\[]\s*[""']?(?:translation|text|result|target)[""']?\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex JsonWrapperRegex();
    [GeneratedRegex(@"\b(?:http 4\d\d|http 5\d\d|rate limit|api error|internal server error|unauthorized)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RawErrorRegex();
    [GeneratedRegex(@"^\s*(?:line|index|item)\s*[#:]?\s*\d+\s*[:\-]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BatchIndexRegex();
    [GeneratedRegex(@"(?<!\p{L})[-+]?\d+(?:[.,]\d+)?(?!\p{L})", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();
    [GeneratedRegex(@"https?://[^\s<>{}]+|www\.[^\s<>{}]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();
    [GeneratedRegex(@"\b[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();
    [GeneratedRegex(@"\{[^{}\r\n]+\}|%\w+%|\$\{[^{}\r\n]+\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();
    [GeneratedRegex(@"</?(?:i|b|u|font|span|c)(?:\s+[^>]*)?>|\{\\[^}]+\}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TagRegex();
    [GeneratedRegex(@"<(?:i|b|u|font|span|c)(?:\s+[^>]*)?>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex OpeningTagRegex();
    [GeneratedRegex(@"</(?:i|b|u|font|span|c)>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ClosingTagRegex();
    [GeneratedRegex(@"\r\n|\r|\n|\\N", RegexOptions.Compiled)]
    private static partial Regex LineBreakRegex();
    [GeneratedRegex(@"\p{L}", RegexOptions.Compiled)]
    private static partial Regex LetterRegex();
    [GeneratedRegex(@"[!?.,:;…]{4,}", RegexOptions.Compiled)]
    private static partial Regex RepeatedPunctuationRegex();
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
