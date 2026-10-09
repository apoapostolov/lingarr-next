using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.ProviderHealth;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Plugins;
using Lingarr.Server.Services.Plugins.Manifests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class ProviderHealthServiceTests
{
    [Fact]
    public async Task RecordAsync_Success_MarksConfiguredProviderHealthy()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);

        await service.RecordAsync(new ProviderOperationalResult
        {
            Provider = "microsoft",
            Operation = "translate",
            Outcome = "success",
            DurationMs = 125
        });

        var health = Assert.Single(await service.GetAllAsync());
        Assert.Equal(ProviderHealthStates.Healthy, health.State);
        Assert.Equal(100, health.SuccessRate);
        Assert.Equal(125, health.MedianDurationMs);
    }

    [Fact]
    public async Task RecordAsync_MeteredUsage_PersistsUsageOnOperationalEvent()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);

        await service.RecordAsync(new ProviderOperationalResult
        {
            Provider = "openai",
            Model = "gpt-4o-mini",
            Operation = "batch",
            Outcome = "success",
            DurationMs = 125,
            InputTokens = 1_200,
            OutputTokens = 800,
            EstimatedCostUsd = 0.00066m
        });

        var operationalEvent = Assert.Single(database.ProviderOperationalEvents);
        Assert.Equal(1_200, operationalEvent.InputTokens);
        Assert.Equal(800, operationalEvent.OutputTokens);
        Assert.Equal(0.00066m, operationalEvent.EstimatedCostUsd);
    }

    [Fact]
    public async Task RecordAsync_ThreeTransientFailuresAfterSuccess_MarksRecentlyUnavailable()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);

        await service.RecordAsync(Result("success"));
        await service.RecordAsync(Result("failure", "timeout", true));
        await service.RecordAsync(Result("failure", "timeout", true));
        await service.RecordAsync(Result("failure", "timeout", true));

        var health = Assert.Single(await service.GetAllAsync());
        Assert.Equal(ProviderHealthStates.RecentlyUnavailable, health.State);
        Assert.Equal(3, health.ConsecutiveFailures);
    }

    [Fact]
    public async Task RecordAsync_AuthenticationFailure_MarksProviderUnavailable()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);

        await service.RecordAsync(Result("failure", "authentication"));

        var health = Assert.Single(await service.GetAllAsync());
        Assert.Equal(ProviderHealthStates.Unavailable, health.State);
        Assert.Contains("API key", health.Reason);
    }

    [Fact]
    public void Classify_HttpStatus_UsesStableErrorFamilies()
    {
        Assert.Equal(
            ("authentication", false),
            ProviderHealthService.Classify(new HttpRequestException(
                "Unauthorized",
                null,
                HttpStatusCode.Unauthorized)));
        Assert.Equal(
            ("rate_limit", true),
            ProviderHealthService.Classify(new HttpRequestException(
                "Rate limited",
                null,
                HttpStatusCode.TooManyRequests)));
    }

    private static ProviderOperationalResult Result(
        string outcome,
        string? errorFamily = null,
        bool transient = false)
    {
        return new ProviderOperationalResult
        {
            Provider = "microsoft",
            Operation = "translate",
            Outcome = outcome,
            ErrorFamily = errorFamily,
            IsTransient = transient,
            DurationMs = 100
        };
    }

    private static LingarrDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static ProviderHealthService CreateService(LingarrDbContext database)
    {
        var pluginLoader = new PluginLoader(NullLogger<PluginLoader>.Instance);
        IPluginManifest[] manifests = [new MicrosoftTranslatePluginManifest()];
        var registry = new PluginRegistry(
            manifests,
            pluginLoader,
            NullLogger<PluginRegistry>.Instance);

        var settings = new Mock<ISettingService>();
        settings.Setup(item => item.GetSetting(SettingKeys.Translation.ServiceType))
            .ReturnsAsync("""[{"provider":"microsoft"}]""");

        return new ProviderHealthService(
            database,
            registry,
            settings.Object,
            Mock.Of<ITranslationServiceFactory>(),
            pluginLoader,
            NullLogger<ProviderHealthService>.Instance);
    }
}
