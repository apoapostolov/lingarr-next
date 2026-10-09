using System.ComponentModel.DataAnnotations.Schema;
using Lingarr.Core.Enum;

namespace Lingarr.Core.Entities;

public class TranslationRequest : BaseEntity
{
    public string? JobId  { get; set; }
    public int? MediaId  { get; set; }
    public required string Title { get; set; }
    public required string SourceLanguage { get; set; }
    public required string TargetLanguage { get; set; }
    public string? SubtitleToTranslate { get; set; }
    public string? TranslatedSubtitle { get; set; }
    public required MediaType MediaType { get; set; }
    public required TranslationStatus Status { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public int? QualityScore { get; set; }
    public string? QualityGrade { get; set; }
    public string? QualityStatus { get; set; }
    /// <summary>Percent of subtitle lines saved when a request was cancelled. Null when unknown.</summary>
    public int? CachedProgress { get; set; }

    /// <summary>How many times the provider has cancelled this request.</summary>
    public int ProviderCancelAttempts { get; set; }

    /// <summary>Retry limit captured when the provider cancelled this request.</summary>
    public int ProviderCancelRetryMax { get; set; }

    /// <summary>A delayed retry is waiting to run.</summary>
    public bool ProviderCancelRetryPending { get; set; }

    [NotMapped]
    public long? InputTokens { get; set; }

    [NotMapped]
    public long? OutputTokens { get; set; }

    [NotMapped]
    public bool ShowTokenUsage { get; set; }

    [NotMapped]
    public List<string> Badges { get; set; } = [];
}
