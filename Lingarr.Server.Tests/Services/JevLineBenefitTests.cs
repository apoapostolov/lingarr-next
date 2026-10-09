using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Translation;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Jev;
using Lingarr.Server.Services.Classification;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class JevLineBenefitTests
{
    [Theory]
    [InlineData("sound", 0.96, true)]
    [InlineData("credit", 0.85, true)]
    [InlineData("sound", 0.84, false)]
    [InlineData("dialogue", 0.99, false)]
    [InlineData("other", 0.99, false)]
    public void ShouldSkip_OnlyConfidentNonDialogue(string choice, double confidence, bool skip)
    {
        Assert.Equal(skip, ClassifierLinePolicy.ShouldSkip(choice, confidence));
    }

    [Theory]
    [InlineData(0.10, true)]
    [InlineData(0.34, true)]
    [InlineData(0.35, false)]
    [InlineData(0.90, false)]
    public void ShouldReject_OnlyWhenTheResultIsUnlikelyToBeATranslation(double probability, bool reject)
    {
        Assert.Equal(reject, ClassifierLinePolicy.ShouldReject(probability));
    }

    [Fact]
    public async Task Client_ReadsChoiceAndNoulFromTheTypeSafeShape()
    {
        var handler = new RecordingHandler("""
            {
              "model": "jev-1.13.0",
              "answers": {
                "p1": { "type": "choice", "choice": "sound", "confidence": 0.96 },
                "p2": { "type": "noul", "noul": 0.12 }
              }
            }
            """);
        var client = new JevClient(new HttpClient(handler));
        var decision = await client.Ask("test-key", "state", [
            new ClassifierQuestion("p1", "choice", "Classify the line", new Dictionary<string, string>
            {
                ["sound"] = "Sound cue", ["dialogue"] = "Spoken words"
            }),
            new ClassifierQuestion("p2", "predicate", "Is this a real translation?")
        ], CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal("sound", decision!.Choices["p1"].Choice);
        Assert.Equal(0.96, decision.Choices["p1"].Confidence);
        Assert.Equal(0.12, decision.Predicates["p2"]);
        Assert.Contains("Bearer test-key", handler.Authorization);
        Assert.Contains("jev-latest", handler.Body);
        using var request = JsonDocument.Parse(handler.Body);
        var questions = request.RootElement.GetProperty("questions");
        Assert.Equal("choice", questions.GetProperty("p1").GetProperty("type").GetString());
        Assert.Equal("Sound cue", questions.GetProperty("p1").GetProperty("criteria").GetProperty("sound").GetString());
        Assert.Equal("noul", questions.GetProperty("p2").GetProperty("type").GetString());
        Assert.True(ClassifierLinePolicy.ShouldSkip(decision.Choices["p1"].Choice, decision.Choices["p1"].Confidence));
        Assert.True(ClassifierLinePolicy.ShouldReject(decision.Predicates["p2"]));
    }

    [Fact]
    public async Task Skip_LeavesTheCueUntouchedAndTranslatesOnlyDialogue()
    {
        var calls = 0;
        var harness = CreateHarness(text =>
        {
            calls++;
            return "Hola";
        }, new ScriptedGate(skip: [1]));

        var result = await harness.TranslateSubtitles(
            [Line(1, "[Door slams]"), Line(2, "Hello there")],
            Request(),
            false,
            false,
            0,
            0,
            CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal("[Door slams]", string.Join(" ", result[0].TranslatedLines));
        Assert.Equal("Hola", string.Join(" ", result[1].TranslatedLines));
    }

    [Fact]
    public async Task Reject_DoesNotKeepARefusal()
    {
        var harness = CreateHarness(_ => "I cannot translate this", new ScriptedGate(reject: [1]));
        var result = await harness.TranslateSubtitles(
            [Line(1, "Hello there")],
            Request(),
            false,
            false,
            0,
            0,
            CancellationToken.None);

        Assert.Equal("Hello there", string.Join(" ", result[0].TranslatedLines));
    }

    [Fact]
    public async Task WithoutJev_ARefusalIsKept()
    {
        var harness = CreateHarness(_ => "I cannot translate this", gate: null);
        var result = await harness.TranslateSubtitles(
            [Line(1, "Hello there")],
            Request(),
            false,
            false,
            0,
            0,
            CancellationToken.None);

        Assert.Equal("I cannot translate this", string.Join(" ", result[0].TranslatedLines));
    }

    private static TranslationRequest Request() => new()
    {
        Title = "test",
        SourceLanguage = "en",
        TargetLanguage = "bg",
        MediaType = MediaType.Movie,
        Status = TranslationStatus.InProgress
    };

    private static SubtitleItem Line(int position, string text) => new()
    {
        Position = position,
        Lines = [text],
        PlaintextLines = [text]
    };

    private static SubtitleTranslationService CreateHarness(Func<string, string> translate, IClassifierSubtitleGate? gate)
    {
        var translation = new Mock<ITranslationService>();
        translation
            .Setup(service => service.GetLanguagePair(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string source, string target, CancellationToken _) =>
                new LanguagePair { Source = source, Target = target, Tier = MatchTier.Exact });
        translation
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, string _, string _, List<string>? _, List<string>? _, CancellationToken _) => translate(text));

        var progress = new Mock<IProgressService>();
        progress.Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>())).Returns(Task.CompletedTask);
        progress.Setup(service => service.EmitLine(
            It.IsAny<TranslationRequest>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<LanguagePair?>())).Returns(Task.CompletedTask);

        return new SubtitleTranslationService(
            [new TranslationServiceEntry("test", translation.Object, null)],
            NullLogger.Instance,
            progress.Object,
            classifierGate: gate);
    }

    private sealed class ScriptedGate : IClassifierSubtitleGate
    {
        private readonly HashSet<int> _skip;
        private readonly HashSet<int> _reject;

        public ScriptedGate(int[]? skip = null, int[]? reject = null)
        {
            _skip = (skip ?? []).ToHashSet();
            _reject = (reject ?? []).ToHashSet();
        }

        public Task<bool> SkipEnabled(CancellationToken cancellationToken) => Task.FromResult(_skip.Count > 0);

        public Task<bool> RejectEnabled(CancellationToken cancellationToken) => Task.FromResult(_reject.Count > 0);

        public Task<IReadOnlySet<int>> PositionsToSkip(
            IReadOnlyList<(int Position, string Text)> lines,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<int>>(_skip);

        public Task<IReadOnlySet<int>> PositionsToReject(
            IReadOnlyList<(int Position, string Source, string Translation)> lines,
            string sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<int>>(_reject);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;
        public string Body { get; private set; } = "";
        public string Authorization { get; private set; } = "";

        public RecordingHandler(string response)
        {
            _response = response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString() ?? "";
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            };
        }
    }
}
