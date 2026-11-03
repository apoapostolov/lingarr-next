using System;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models.TranslationQuality;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class TranslationQualityServiceTests
{
    [Fact]
    public async Task EvaluateAsync_CleanLines_ReturnsExcellentWithoutChangingTranslation()
    {
        await using var database = CreateDatabase();
        var request = await SeedRequest(
            database,
            ("Hello.", "Привіт."),
            ("How are you?", "Як справи?"));
        var originalTargets = await database.TranslationRequestLines
            .OrderBy(x => x.Position)
            .Select(x => x.Target)
            .ToArrayAsync();

        var result = await CreateService(database).EvaluateAsync(request.Id);

        Assert.Equal(100, result.Score);
        Assert.Equal("Excellent", result.Grade);
        Assert.Equal(originalTargets, await database.TranslationRequestLines
            .OrderBy(x => x.Position)
            .Select(x => x.Target)
            .ToArrayAsync());
    }

    [Fact]
    public async Task EvaluateAsync_EmptyLine_UsesCriticalFloorAndFinding()
    {
        await using var database = CreateDatabase();
        var request = await SeedRequest(
            database,
            ("Hello.", ""),
            ("How are you?", "Як справи?"));
        var service = CreateService(database);

        var result = await service.EvaluateAsync(request.Id);
        var detail = await service.GetAsync(request.Id);

        Assert.True(result.Score <= 39);
        Assert.Equal("Failed quality gate", result.Grade);
        Assert.Contains(detail!.Findings, finding =>
            finding.RuleId == "integrity.target_empty" &&
            finding.Severity == QualitySeverity.Critical);
    }

    [Fact]
    public async Task EvaluateAsync_ModelChatterAndChangedNumber_AreReported()
    {
        await using var database = CreateDatabase();
        var request = await SeedRequest(
            database,
            ("Meet me at 8.", "Translation: Зустрінь мене о 9."));
        var service = CreateService(database);

        await service.EvaluateAsync(request.Id);
        var detail = await service.GetAsync(request.Id);

        Assert.Contains(detail!.Findings, finding => finding.RuleId == "model.meta_commentary");
        Assert.Contains(detail.Findings, finding => finding.RuleId == "content.number_changed");
    }

    [Fact]
    public async Task EvaluateAsync_ReevaluationCreatesNewImmutableAssessment()
    {
        await using var database = CreateDatabase();
        var request = await SeedRequest(database, ("Hello.", "Привіт."));
        var service = CreateService(database);

        var first = await service.EvaluateAsync(request.Id);
        var second = await service.EvaluateAsync(request.Id);

        Assert.NotEqual(first.AssessmentId, second.AssessmentId);
        Assert.Equal(2, await database.TranslationQualityAssessments.CountAsync());
        Assert.Equal(second.AssessmentId, (await service.GetAsync(request.Id))!.Summary!.AssessmentId);
    }

    [Fact]
    public async Task EvaluateAsync_SubtitleMarkupIsNotAPlaceholder()
    {
        await using var database = CreateDatabase();
        var request = await SeedRequest(
            database,
            (@"{\an5}{en: Advisor}Advisor", @"{\an5} {en: Съветник} Съветник"));
        var service = CreateService(database);

        await service.EvaluateAsync(request.Id);
        var detail = await service.GetAsync(request.Id);

        Assert.DoesNotContain(detail!.Findings, finding => finding.RuleId == "placeholder_changed");
    }

    [Fact]
    public async Task EvaluateAsync_RealPlaceholderChange_IsStillReported()
    {
        await using var database = CreateDatabase();
        var request = await SeedRequest(
            database,
            ("Format the drive before {date}.", "Форматирайте диска преди."));
        var service = CreateService(database);

        await service.EvaluateAsync(request.Id);
        var detail = await service.GetAsync(request.Id);

        Assert.Contains(detail!.Findings, finding => finding.RuleId == "placeholder_changed");
    }

    [Fact]
    public async Task EvaluateAsync_BlankSourceWithBlankTarget_IsNotAnIntegrityFailure()
    {
        await using var database = CreateDatabase();
        var request = await SeedRequest(
            database,
            ("", ""),
            ("Hello.", "Привіт."));
        var service = CreateService(database);

        var result = await service.EvaluateAsync(request.Id);
        var detail = await service.GetAsync(request.Id);

        Assert.Equal(100, result.Score);
        Assert.DoesNotContain(detail!.Findings, finding => finding.RuleId == "integrity.target_empty");
    }

    [Fact]
    public async Task EvaluateAsync_RepeatedTargetFromIdenticalSources_IsNotFlagged()
    {
        await using var database = CreateDatabase();
        var request = await SeedRequest(
            database,
            ("The Apothecary Diaries", "Дневниците на аптеката"),
            ("The Apothecary Diaries", "Дневниците на аптеката"),
            ("The Apothecary Diaries", "Дневниците на аптеката"));
        var service = CreateService(database);

        await service.EvaluateAsync(request.Id);
        var detail = await service.GetAsync(request.Id);

        Assert.DoesNotContain(detail!.Findings, finding => finding.RuleId == "content.repeated_output");
    }

    [Fact]
    public async Task EvaluateAsync_RepeatedTargetFromDifferentSources_IsFlagged()
    {
        await using var database = CreateDatabase();
        var request = await SeedRequest(
            database,
            ("Where is the station?", "Същият ред."),
            ("Bring the lantern.", "Същият ред."),
            ("Wait for me here.", "Същият ред."));
        var service = CreateService(database);

        await service.EvaluateAsync(request.Id);
        var detail = await service.GetAsync(request.Id);

        Assert.Contains(detail!.Findings, finding => finding.RuleId == "content.repeated_output");
    }

    private static TranslationQualityService CreateService(LingarrDbContext database) =>
        new(database, NullLogger<TranslationQualityService>.Instance);

    private static LingarrDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static async Task<TranslationRequest> SeedRequest(
        LingarrDbContext database,
        params (string source, string target)[] lines)
    {
        var request = new TranslationRequest
        {
            Title = "Quality test",
            SourceLanguage = "en",
            TargetLanguage = "uk",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed
        };
        database.TranslationRequests.Add(request);
        await database.SaveChangesAsync();

        database.TranslationRequestLines.AddRange(lines.Select((line, index) =>
            new TranslationRequestLine
            {
                TranslationRequestId = request.Id,
                Position = index + 1,
                Source = line.source,
                Target = line.target,
                Service = "test"
            }));
        await database.SaveChangesAsync();
        return request;
    }
}
