using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Classification;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class ClassifierProviderTests
{
    [Fact]
    public async Task Luna_UsesTranslationOpenAiKeyAndChoiceThreshold()
    {
        var handler = new RecordingHandler("""
            {"answers":[
              {"type":"choice","name":"p1","choice":"sound","confidence":0.96},
              {"type":"choice","name":"p2","choice":"dialogue","confidence":0.99}
            ]}
            """);
        var (gate, settings) = CreateGate("luna", handler, openAiKey: "shared-openai-key");

        var skipped = await gate.PositionsToSkip(
            [(1, "[door slams]"), (2, "Hello")], CancellationToken.None);

        Assert.Equal([1], skipped);
        Assert.Equal(LunaDecisionsClient.Endpoint, handler.Url);
        Assert.Equal("Bearer shared-openai-key", handler.Authorization);
        settings.Verify(s => s.GetEncryptedSetting(SettingKeys.Translation.OpenAi.ApiKey), Times.AtLeastOnce());
        using var request = JsonDocument.Parse(handler.Body);
        Assert.Equal("gpt-6-luna", request.RootElement.GetProperty("model").GetString());
        Assert.Contains("door slams", request.RootElement.GetProperty("input").GetString());
        var question = request.RootElement.GetProperty("questions")[0];
        Assert.Equal("choice", question.GetProperty("type").GetString());
        Assert.Equal("p1", question.GetProperty("name").GetString());
        Assert.Contains(question.GetProperty("choices").EnumerateArray(),
            choice => choice.GetProperty("value").GetString() == "sound");
    }

    [Fact]
    public async Task Luna_PredicateRejectsOnlyLowProbability()
    {
        var handler = new RecordingHandler("""
            {"answers":[
              {"type":"predicate","name":"p1","probability":0.12},
              {"type":"predicate","name":"p2","probability":0.88}
            ]}
            """);
        var (gate, _) = CreateGate("luna", handler, openAiKey: "shared-openai-key");

        var rejected = await gate.PositionsToReject(
            [(1, "Hello", "I cannot translate"), (2, "Goodbye", "Довиждане")],
            "en", "bg", CancellationToken.None);

        Assert.Equal([1], rejected);
        var question = JsonDocument.Parse(handler.Body).RootElement.GetProperty("questions")[0];
        Assert.Equal("predicate", question.GetProperty("type").GetString());
        Assert.Equal("p1", question.GetProperty("name").GetString());
        Assert.False(question.TryGetProperty("choices", out _));
    }

    [Fact]
    public async Task Luna_UsesTheSameIndependentSwitchesAsJev()
    {
        var handler = new RecordingHandler("{}");
        var (gate, settings) = CreateGate("luna", handler, openAiKey: "shared-openai-key");
        settings.Setup(s => s.GetSetting(SettingKeys.Translation.JevSkipNonDialogue)).ReturnsAsync("false");

        Assert.False(await gate.SkipEnabled(CancellationToken.None));
        Assert.True(await gate.RejectEnabled(CancellationToken.None));
        Assert.Empty(await gate.PositionsToSkip([(1, "[door slams]")], CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ExistingInstallationDefaultsToJevAndKeepsItsKey()
    {
        var handler = new RecordingHandler("""
            {"answers":{"p1":{"type":"choice","choice":"credit","confidence":0.92}}}
            """);
        var (gate, settings) = CreateGate(null, handler, jevKey: "old-jev-key");

        Assert.Equal([1], await gate.PositionsToSkip([(1, "Credits")], CancellationToken.None));
        Assert.Equal("Bearer old-jev-key", handler.Authorization);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", handler.Url);
        settings.Verify(s => s.GetEncryptedSetting(SettingKeys.Translation.OpenAi.ApiKey), Times.Never());
    }

    [Theory]
    [InlineData("luna")]
    [InlineData("unknown")]
    public async Task NoSelectedCredentialOrUnknownProvider_FailsOpen(string provider)
    {
        var handler = new RecordingHandler("{}");
        var (gate, _) = CreateGate(provider, handler, jevKey: "other-provider-key");

        Assert.False(await gate.SkipEnabled(CancellationToken.None));
        Assert.False(await gate.RejectEnabled(CancellationToken.None));
        Assert.Empty(await gate.PositionsToSkip([(1, "[noise]")], CancellationToken.None));
        Assert.Empty(await gate.PositionsToReject([(1, "Hi", "Hi")], "en", "bg", CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("{\"answers\":[{\"type\":\"refusal\",\"name\":\"p1\"}]}", HttpStatusCode.OK)]
    [InlineData("{\"answers\":[{\"type\":\"predicate\",\"name\":\"p1\",\"probability\":\"bad\"}]}", HttpStatusCode.OK)]
    [InlineData("[]", HttpStatusCode.OK)]
    [InlineData("{}", HttpStatusCode.Unauthorized)]
    public async Task LunaRefusalBadAnswerOrHttpError_KeepsNormalPath(string body, HttpStatusCode status)
    {
        var handler = new RecordingHandler(body, status);
        var (gate, _) = CreateGate("luna", handler, openAiKey: "test-key");

        Assert.Empty(await gate.PositionsToReject([(1, "Hello", "Hello")], "en", "bg", CancellationToken.None));
    }

    private static (ClassifierSubtitleGate Gate, Mock<ISettingService> Settings) CreateGate(
        string? provider,
        RecordingHandler handler,
        string? jevKey = null,
        string? openAiKey = null)
    {
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetSetting(SettingKeys.Translation.ClassifierProvider)).ReturnsAsync(provider);
        settings.Setup(s => s.GetSetting(SettingKeys.Translation.JevSkipNonDialogue)).ReturnsAsync("true");
        settings.Setup(s => s.GetSetting(SettingKeys.Translation.JevRejectUntranslated)).ReturnsAsync("true");
        settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Translation.TypesafeApiKey)).ReturnsAsync(jevKey);
        settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Translation.OpenAi.ApiKey)).ReturnsAsync(openAiKey);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));
        return (new ClassifierSubtitleGate(settings.Object, factory.Object,
            NullLogger<ClassifierSubtitleGate>.Instance), settings);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _status;
        public int Calls { get; private set; }
        public string Url { get; private set; } = "";
        public string Authorization { get; private set; } = "";
        public string Body { get; private set; } = "";

        public RecordingHandler(string response, HttpStatusCode status = HttpStatusCode.OK)
        {
            _response = response;
            _status = status;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Url = request.RequestUri?.ToString() ?? "";
            Authorization = request.Headers.Authorization?.ToString() ?? "";
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_response) };
        }
    }
}
