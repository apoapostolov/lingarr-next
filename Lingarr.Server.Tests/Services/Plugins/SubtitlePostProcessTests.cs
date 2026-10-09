using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Plugins;
using Lingarr.Plugin.StyleSample;
using Lingarr.Server.Services.Plugins;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plugins;

public class SubtitlePostProcessTests
{
    [Fact]
    public async Task Execute_RunsLanguageBeforeStyle_AndKeepsOcrInThePath()
    {
        var target = Path.Combine(Path.GetTempPath(), "movie.bg.ocr.srt");
        var seed = Seed(target, "Hello \"there\"?");
        var outcome = await SubtitlePostProcessRunner.ExecuteAsync(
            seed.TargetText,
            [
                Step(new AppendProcessor("style", SubtitlePostProcessKind.Style, " S"), 50, "skip"),
                Step(new AppendProcessor("language", SubtitlePostProcessKind.Language, " L"), 10, "skip")
            ],
            seed,
            CancellationToken.None);

        Assert.Equal("Hello \"there\"? L S", outcome.Text);
        Assert.True(outcome.Write);
        Assert.EndsWith(".ocr.srt", seed.TargetPath, StringComparison.Ordinal);
        Assert.True(SubtitlePostProcessRunner.PathIsOcr(seed.TargetPath));
    }

    [Fact]
    public async Task Execute_WhenAProcessorThrows_KeepsTheOriginalForAFailPolicy()
    {
        var original = "1\n00:00:01,000 --> 00:00:02,000\nHello?\n";
        var outcome = await SubtitlePostProcessRunner.ExecuteAsync(
            original,
            [
                Step(new AppendProcessor("language", SubtitlePostProcessKind.Language, " L"), 1, "skip"),
                Step(new ThrowProcessor("style"), 1, PluginCatalog.PolicyFail)
            ],
            Seed("movie.es.srt", original),
            CancellationToken.None);

        Assert.True(outcome.Failed);
        Assert.False(outcome.Write);
        Assert.Equal(original, outcome.Text);
    }

    [Fact]
    public async Task Execute_SkipContinuesAfterAThrow()
    {
        var outcome = await SubtitlePostProcessRunner.ExecuteAsync(
            "Hello",
            [
                Step(new ThrowProcessor("language"), 1, PluginCatalog.PolicySkip),
                Step(new AppendProcessor("style", SubtitlePostProcessKind.Style, "!"), 1, "skip")
            ],
            Seed("movie.fr.srt", "Hello"),
            CancellationToken.None);

        Assert.False(outcome.Failed);
        Assert.Equal("Hello!", outcome.Text);
        Assert.True(outcome.Write);
    }

    [Fact]
    public void FrenchAndSpanishSamples_ChangeDialogueLinesOnly()
    {
        Assert.Equal("He said « hi »", InternationalQuotesProcessor.Apply("He said \"hi\"", "fr"));
        Assert.Equal("He said „hi“", InternationalQuotesProcessor.Apply("He said \"hi\"", "de"));
        Assert.Equal("He said ‘hi’", InternationalQuotesProcessor.Apply("He said \"hi\"", "gb"));
        Assert.Equal("He said “hi”", InternationalQuotesProcessor.Apply("He said \"hi\"", "us"));
        Assert.Equal("He said ”hi”", InternationalQuotesProcessor.Apply("He said \"hi\"", "se"));
        Assert.Equal("He said »hi«", InternationalQuotesProcessor.Apply("He said \"hi\"", "dk"));
        Assert.Equal("He said 「hi」", InternationalQuotesProcessor.Apply("He said \"hi\"", "jp"));
        Assert.Equal("He said — hi", InternationalQuotesProcessor.Apply("He said \"hi\"", "es"));
        Assert.Equal("He said 「hi」", InternationalQuotesProcessor.Apply("He said \"hi\"", "ja", "target"));
        Assert.Equal("He said „hi“", InternationalQuotesProcessor.Apply("He said \"hi\"", "ja", "de"));
        Assert.Equal("jp", DialogueMarks.CountryForLanguage("ja"));
        Assert.Equal("us", DialogueMarks.CountryForLanguage("en"));
        Assert.True(DialogueMarks.All.Length >= 60);
        var spanish = """
            1
            00:00:01,000 --> 00:00:02,000
            How are you?
            """;
        Assert.Contains(
            "00:00:01,000 --> 00:00:02,000",
            InternationalQuotesProcessor.Apply(spanish.Replace("\r\n", "\n"), "jp"),
            StringComparison.Ordinal);
        var restored = SpanishLanguageProcessor.Apply(spanish.Replace("\r\n", "\n"));
        Assert.Contains("¿How are you?", restored, StringComparison.Ordinal);
        Assert.Contains("00:00:01,000 --> 00:00:02,000", restored, StringComparison.Ordinal);
    }

    private static SubtitlePostProcessInput Seed(string target, string text) => new()
    {
        SourcePath = "movie.en.srt",
        TargetPath = target,
        SourceLanguage = "en",
        TargetLanguage = "fr",
        SourceIsOcr = SubtitlePostProcessRunner.PathIsOcr(target),
        TargetText = text
    };

    private static ReadyPostProcessor Step(ISubtitlePostProcessor processor, int order, string policy) => new()
    {
        Processor = processor,
        Order = order,
        FailurePolicy = policy
    };

    private sealed class AppendProcessor : ISubtitlePostProcessor
    {
        private readonly string _suffix;

        public AppendProcessor(string provider, SubtitlePostProcessKind kind, string suffix)
        {
            Provider = provider;
            Kind = kind;
            _suffix = suffix;
        }

        public string Provider { get; }
        public SubtitlePostProcessKind Kind { get; }

        public Task<SubtitlePostProcessResult> ProcessAsync(
            SubtitlePostProcessInput input,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SubtitlePostProcessResult { Text = input.TargetText + _suffix });
    }

    private sealed class ThrowProcessor : ISubtitlePostProcessor
    {
        public ThrowProcessor(string provider) => Provider = provider;
        public string Provider { get; }
        public SubtitlePostProcessKind Kind => SubtitlePostProcessKind.Style;

        public Task<SubtitlePostProcessResult> ProcessAsync(
            SubtitlePostProcessInput input,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }
}
