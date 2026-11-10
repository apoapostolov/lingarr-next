using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.ProviderHealth;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Plugins;
using Lingarr.Server.Services.Plugins.Manifests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task GetAllAsync_ReadsTheStateWithoutStoringASnapshot()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);

        var health = Assert.Single(await service.GetAllAsync());

        Assert.Equal(ProviderHealthStates.NotChecked, health.State);
        Assert.Empty(database.ProviderHealthSnapshots);
        Assert.False(database.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task RecordAsync_StoresTheReading()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);

        await service.RecordAsync(Result("success"));

        var snapshot = Assert.Single(database.ProviderHealthSnapshots);
        Assert.Equal(ProviderHealthStates.Healthy, snapshot.State);
        Assert.NotEqual(default, snapshot.EvaluatedAt);
    }

    [Fact]
    public async Task ReconcileAsync_RefreshesReadingsAfterHousekeeping()
    {
        // The InMemory provider has no ExecuteDelete, so retention needs a real database.
        var path = Path.Combine(Path.GetTempPath(), $"lingarr-health-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var database = new LingarrDbContext(options);
        await database.Database.EnsureCreatedAsync();
        try
        {
            var service = CreateService(database);
            await service.RecordAsync(Result("success"));
            database.ChangeTracker.Clear();

            await service.ReconcileAsync();

            var snapshot = Assert.Single(database.ProviderHealthSnapshots);
            Assert.Equal(ProviderHealthStates.Healthy, snapshot.State);
        }
        finally
        {
            await database.DisposeAsync();
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReconcileAsync_RefreshesReadingsWhenRetentionCannotRun()
    {
        // InMemory cannot run ExecuteDelete, which stands in for any provider-specific failure of
        // the retention delete: the readings must still be refreshed.
        await using var database = CreateDatabase();
        var logger = new RecordingLogger();
        var service = CreateService(database, logger);

        await service.ReconcileAsync();

        var snapshot = Assert.Single(database.ProviderHealthSnapshots.Where(item => item.Provider == "microsoft"));
        Assert.Equal(ProviderHealthStates.NotChecked, snapshot.State);
        Assert.Contains(logger.Messages, message => message.Contains("Skipped the retention delete"));
    }

    [Fact]
    public async Task ReconcileAsync_DoesNotOverwriteANewerReading()
    {
        await using var database = CreateDatabase();
        var service = CreateService(database);
        var newer = DateTime.UtcNow.AddMinutes(5);
        database.ProviderHealthSnapshots.Add(new ProviderHealthSnapshot
        {
            Provider = "microsoft",
            State = ProviderHealthStates.Unavailable,
            Reason = "recorded after this reconciliation started",
            EvaluatedAt = newer
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        await service.ReconcileAsync();

        var snapshot = Assert.Single(database.ProviderHealthSnapshots.Where(item => item.Provider == "microsoft"));
        Assert.Equal("recorded after this reconciliation started", snapshot.Reason);
        Assert.Equal(newer, snapshot.EvaluatedAt, TimeSpan.FromMilliseconds(1));
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

    private static ProviderHealthService CreateService(
        LingarrDbContext database,
        ILogger<ProviderHealthService>? logger = null)
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
            logger ?? NullLogger<ProviderHealthService>.Instance);
    }

    private sealed class RecordingLogger : ILogger<ProviderHealthService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
