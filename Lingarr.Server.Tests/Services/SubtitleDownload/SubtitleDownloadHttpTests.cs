using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.SubtitleDownload;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.SubtitleDownload;

public class SubtitleDownloadHttpTests
{
    private const string Srt = "1\n00:00:01,000 --> 00:00:02,000\nHello, welcome to the example movie.\n";
    private static SubtitleDownloadQuery Query => new()
    {
        MediaType = MediaType.Movie, MediaId = 1, Title = "Example Movie", Year = 2024, Language = "en"
    };

    [Fact]
    public async Task HtmlSearch_KeepsConfiguredHeadersOnDetailAndDownload()
    {
        var provider = SubtitleDownloadCatalog.Find("turkcealtyaziorg")!;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetEncryptedSetting(provider.SettingKey("cookies"))).ReturnsAsync("session=fixture");
        settings.Setup(s => s.GetSetting(provider.SettingKey("user_agent"))).ReturnsAsync("FixtureBrowser/1.0");
        var calls = new List<string>();
        using var handler = new Handler(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            if (!request.Headers.TryGetValues("Cookie", out var values)
                || !string.Join(";", values).Contains("session=fixture")
                || request.Headers.UserAgent.ToString() != "FixtureBrowser/1.0")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return calls.Count switch
            {
                1 => Html("<a href='/subtitles/example-movie-2024.html'>Example Movie 2024</a>"),
                2 => Html("<a href='/download/example-movie-2024-en.srt'>Example Movie 2024 English</a>"),
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Srt) }
            };
        });
        var hit = await Gateway(handler, settings).DownloadAsync(provider, Query, CancellationToken.None);
        Assert.NotNull(hit);
        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public async Task JsonSearch_PreservesEndpointQueryAndResolvesRelativeDownload()
    {
        var provider = SubtitleDownloadCatalog.Find("titlovi")!;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetSetting(provider.SettingKey("username"))).ReturnsAsync("fixture-user");
        settings.Setup(s => s.GetEncryptedSetting(provider.SettingKey("password"))).ReturnsAsync("fixture-password");
        var calls = new List<Uri>();
        using var handler = new Handler(request =>
        {
            calls.Add(request.RequestUri!);
            if (calls.Count == 1)
            {
                Assert.Contains("username=fixture-user", request.RequestUri!.Query);
                Assert.Contains("password=fixture-password", request.RequestUri.Query);
                Assert.Contains("type=0", request.RequestUri.Query);
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"download":"/download/example.en.srt"}""") };
            }
            Assert.Equal("https://kodi.titlovi.com/download/example.en.srt", request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Srt) };
        });
        var hit = await Gateway(handler, settings).DownloadAsync(provider, Query, CancellationToken.None);
        Assert.NotNull(hit);
        Assert.Equal(2, calls.Count);
    }

    private static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html") };

    [Fact]
    public async Task HtmlSearch_DoesNotSendCookiesToAnotherHost()
    {
        var provider = SubtitleDownloadCatalog.Find("turkcealtyaziorg")!;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetEncryptedSetting(provider.SettingKey("cookies"))).ReturnsAsync("session=fixture");
        var count = 0;
        var cookieLeaked = false;
        using var handler = new Handler(request =>
        {
            count++;
            if (count == 1)
                return Html("<a href='https://downloads.test/example-movie-2024-en.srt'>Example Movie 2024 English</a>");
            cookieLeaked = request.Headers.Contains("Cookie");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Srt) };
        });
        Assert.NotNull(await Gateway(handler, settings).DownloadAsync(provider, Query, CancellationToken.None));
        Assert.Equal(2, count);
        Assert.False(cookieLeaked);
    }

    [Fact]
    public void RankLinks_DecodesHtmlQuerySeparators()
    {
        var links = SubtitleDownloadGateway.RankLinks(
            "<a href='/download/example-movie-2024-en.srt?id=1&amp;token=fixture'>Example Movie 2024 English</a>",
            new Uri("https://example.test/search"), Query);
        Assert.Single(links);
        Assert.Equal("?id=1&token=fixture", links[0].Query);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderFailure_WarnsWithoutLoggingCredentialBearingException(bool timeout)
    {
        using var handler = new Handler(_ => throw (timeout
            ? new TaskCanceledException("request https://example.test?api_key=fixture-secret timed out")
            : new HttpRequestException("request https://example.test?api_key=fixture-secret failed", null, HttpStatusCode.Unauthorized)));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("subtitle-download")).Returns(() => new HttpClient(handler, false));
        var logger = new Mock<ILogger<SubtitleDownloadGateway>>();
        var gateway = new SubtitleDownloadGateway(factory.Object, Mock.Of<ISettingService>(), logger.Object);
        Assert.Null(await gateway.DownloadAsync(SubtitleDownloadCatalog.Find("subtitlecat")!, Query, CancellationToken.None));
        Assert.Contains(logger.Invocations, i => i.Method.Name == "Log" && Equals(i.Arguments[0], LogLevel.Warning));
        Assert.All(logger.Invocations, i =>
        {
            if (i.Method.Name != "Log") return;
            Assert.DoesNotContain("fixture-secret", i.Arguments[2].ToString());
            Assert.Null(i.Arguments[3]);
        });
    }

    private static SubtitleDownloadGateway Gateway(HttpMessageHandler handler, Mock<ISettingService> settings)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("subtitle-download")).Returns(() => new HttpClient(handler, false));
        return new SubtitleDownloadGateway(factory.Object, settings.Object, NullLogger<SubtitleDownloadGateway>.Instance);
    }

    [Fact]
    public async Task HtmlSearch_TriesLaterResultAfterAStaleDetailPage()
    {
        var calls = 0;
        using var handler = new Handler(request =>
        {
            calls++;
            return calls switch
            {
                1 => Html("<a href='/subtitles/example-movie-2024-a.html'>Example Movie 2024</a>"
                    + "<a href='/subtitles/example-movie-2024-b.html'>Example Movie 2024</a>"),
                2 => new HttpResponseMessage(HttpStatusCode.NotFound),
                3 => Html("<a href='/download/example-movie-2024-en.srt'>Example Movie 2024 English</a>"),
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Srt) }
            };
        });
        var hit = await Gateway(handler, new Mock<ISettingService>()).DownloadAsync(
            SubtitleDownloadCatalog.Find("subtitlecat")!, Query, CancellationToken.None);
        Assert.NotNull(hit);
        Assert.Equal(4, calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
